using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.Preventif;

internal sealed record IntervenantLu(Guid? CompteId, string? Nom, string? Prestataire, Guid? ReferentCompteId, string? Referent);

/// <summary>Échéance courante d'un plan : date, ouverture de la fenêtre « À faire », position et statut enregistré éventuel.</summary>
internal sealed record EcheanceLue(DateOnly Echeance, DateOnly OuvertureFenetre, string Etat, string? Statut);

internal sealed record PlanLu(
    Guid Id, string Libelle, string Nature, string Base, string? Reference, string? Modele, string? PropositionCle, string? TypeIntervenant, CibleLue Cible,
    IntervenantLu? Intervenant, string? DonneurOrdre, PeriodeLue? Periode, string Mode, DateOnly PremiereEcheance, bool Suspendu, string? MotifSuspension,
    bool SuspensionAutomatique, EcheanceLue? Prochaine, uint Version);

internal sealed record EvenementPlanLu(DateTimeOffset Le, string Nature, Guid? AuteurCompteId, string? Auteur, JsonElement? Detail);

internal sealed record FichePlan(PlanLu Plan, IReadOnlyList<EvenementPlanLu> Historique);

/// <summary>Plan absent du site (404).</summary>
internal sealed class PlanIntrouvableException() : Exception("Plan introuvable sur ce site.");

/// <summary>Plan modifié par quelqu'un d'autre depuis sa lecture (409 « modifie_entre_temps »).</summary>
internal sealed class PlanModifieException() : Exception("Ce plan vient d'être modifié par quelqu'un d'autre : rechargez puis recommencez.");

/// <summary>Proposition déjà confirmée par un autre envoi (409 « deja_planifiee ») : le plan existe.</summary>
internal sealed class PropositionDejaPlanifieeException() : Exception("Cette proposition est déjà confirmée : le plan existe.");

/// <summary>
/// Plans préventifs d'un site (US-12, S12-02) : création par R/D (« Planifier le préventif »), directement ou en
/// confirmant une proposition, modification à la version lue (l'ancienne règle est tracée), suspension et reprise ;
/// consultation par les comptes internes du site. Échéances calculées à chaque lecture (Echeancier), à la date de Paris.
/// </summary>
internal sealed class ServicePlans(
    MaintenanceDbContext context, IAutorisations autorisations, IInventaireSite inventaire, IAnnuaireMembres annuaire, CompteCourant compte,
    TimeProvider horloge)
{
    private const string Technicien = "Technicien";

    internal async Task<IReadOnlyList<PlanLu>> ListerAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var plans = await context.Plans.AsNoTracking().Where(p => p.SiteId == siteId).OrderBy(p => p.Libelle).ThenBy(p => p.Id)
            .Select(p => new { Plan = p, Version = EF.Property<uint>(p, "xmin") }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await LireAsync(siteId, [.. plans.Select(p => (p.Plan, p.Version))], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Échéancier du site : plans actifs par échéance, en retard d'abord par construction (dates passées).</summary>
    internal async Task<IReadOnlyList<PlanLu>> EcheancierAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken) =>
        [.. (await ListerAsync(organisationId, siteId, cancellationToken).ConfigureAwait(false))
            .Where(p => p.Prochaine is not null).OrderBy(p => p.Prochaine!.Echeance).ThenBy(p => p.Libelle, StringComparer.Ordinal)];

    internal async Task<FichePlan> LireAsync(Guid organisationId, Guid siteId, Guid id, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var lu = await context.Plans.AsNoTracking().Where(p => p.Id == id && p.SiteId == siteId)
            .Select(p => new { Plan = p, Version = EF.Property<uint>(p, "xmin") }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new PlanIntrouvableException();
        var journal = await context.JournalPreventif.AsNoTracking().Where(e => e.PlanId == id).OrderByDescending(e => e.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var auteurs = await annuaire.NomsAsync([.. journal.Where(e => e.AuteurCompteId is not null).Select(e => e.AuteurCompteId!.Value).Distinct()], cancellationToken)
            .ConfigureAwait(false);
        var plan = (await LireAsync(siteId, [(lu.Plan, lu.Version)], cancellationToken).ConfigureAwait(false)).Single();
        return new FichePlan(plan, [.. journal.Select(e => new EvenementPlanLu(
            e.Le, e.Nature.ToString(), e.AuteurCompteId, e.AuteurCompteId is { } a ? auteurs.GetValueOrDefault(a) : null, Json(e.Detail)))]);
    }

    /// <summary>Plan saisi par R/D ; même commande rejouée : même plan.</summary>
    internal async Task<Guid> CreerAsync(Guid organisationId, Guid siteId, DemandePlan demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var commande = Commande(demande.CommandeId);
        var cible = await CibleAsync(siteId, demande.Cible ?? throw new SaisieInvalideException("cible", "Cible obligatoire."), cancellationToken)
            .ConfigureAwait(false);
        var definition = await DefinitionAsync(siteId, demande, base_: SaisiePreventif.Valeur<BasePlan>(demande.Base, "base"), cancellationToken)
            .ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var plan = new PlanPreventif(
            Guid.CreateVersion7(maintenant), organisationId, siteId, commande, propositionCle: null, cible,
            SaisiePreventif.Valeur<NaturePlan>(demande.Nature, "nature"), typeIntervenant: null, definition, Auteur, maintenant);
        return await AjouterAsync(plan, maintenant, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Confirmation d'une proposition du catalogue (décision du 30/10), écartée ou non : modèle, cible et référence « à
    /// confirmer » repris ; première échéance suggérée sauf choix de R/D. Une proposition ne donne qu'un plan.
    /// </summary>
    internal async Task<Guid> ConfirmerAsync(Guid organisationId, Guid siteId, DemandeConfirmation demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var commande = Commande(demande.CommandeId);
        var aujourdhui = JourCivil.Du(horloge.GetUtcNow());
        var proposition = await PropositionAsync(siteId, demande.Cle, aujourdhui, cancellationToken).ConfigureAwait(false);
        var modele = proposition.Modele;
        var intervenant = await IntervenantAsync(siteId, demande.IntervenantCompteId, demande.Prestataire, demande.ReferentCompteId, cancellationToken)
            .ConfigureAwait(false);
        var definition = new DefinitionPlan(
            modele.Libelle, modele.Base, modele.Reference, intervenant, SaisiePreventif.Facultatif<DonneurOrdre>(demande.DonneurOrdre, "donneurOrdre"),
            CalendrierPlan.Valider(modele.Periode, modele.Mode, demande.PremiereEcheance ?? proposition.Echeance, aujourdhui));
        var maintenant = horloge.GetUtcNow();
        var plan = new PlanPreventif(
            Guid.CreateVersion7(maintenant), organisationId, siteId, commande, proposition.Cle,
            new CibleDuPlan(proposition.Cible, proposition.BatimentId, proposition.ZoneId, proposition.ComposantId, proposition.CorpsEtat),
            modele.Nature, modele.Intervenant, definition, Auteur, maintenant);
        return await AjouterAsync(plan, maintenant, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Proposition écartée avec son motif ; elle reste visible. Déjà écartée : rien ne change.</summary>
    internal async Task EcarterAsync(Guid organisationId, Guid siteId, DemandeEcart demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var proposition = await PropositionAsync(siteId, demande.Cle, JourCivil.Du(maintenant), cancellationToken).ConfigureAwait(false);
        var ecart = new PropositionEcartee(
            Guid.CreateVersion7(maintenant), organisationId, siteId, proposition.Cle, SaisiePreventif.Valeur<MotifEcart>(demande.Motif, "motif"),
            demande.Commentaire, Auteur, maintenant);
        if (await context.Plans.AnyAsync(p => p.SiteId == siteId && p.PropositionCle == proposition.Cle, cancellationToken).ConfigureAwait(false))
        {
            throw new PropositionDejaPlanifieeException();
        }

        if (await context.PropositionsEcartees.AnyAsync(e => e.SiteId == siteId && e.Cle == proposition.Cle, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        context.PropositionsEcartees.Add(ecart);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear(); // écartée en parallèle : même résultat
        }
    }

    /// <summary>Définition modifiée à la version lue ; seules les échéances non matérialisées en dépendent. Sans changement, rien n'est écrit.</summary>
    internal async Task ModifierAsync(Guid organisationId, Guid siteId, Guid id, DemandePlan demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        var definition = await DefinitionAsync(siteId, demande, SaisiePreventif.Valeur<BasePlan>(demande.Base, "base"), cancellationToken).ConfigureAwait(false);
        var avant = plan.Releve();
        plan.Modifier(definition);
        var apres = plan.Releve();
        var changes = avant.Keys.Where(champ => !Equals(avant[champ], apres[champ])).ToList();
        if (changes.Count == 0)
        {
            return;
        }

        Tracer(plan, NatureEvenementPlan.Modification, horloge.GetUtcNow(), new
        {
            avant = changes.ToDictionary(champ => champ, champ => avant[champ]),
            apres = changes.ToDictionary(champ => champ, champ => apres[champ]),
        });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task SuspendreAsync(Guid organisationId, Guid siteId, Guid id, DemandeSuspension demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        plan.Suspendre(demande.Motif);
        Tracer(plan, NatureEvenementPlan.Suspension, horloge.GetUtcNow(), new { motif = plan.MotifSuspension });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task ReprendreAsync(Guid organisationId, Guid siteId, Guid id, DemandeVersion demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        plan.Reprendre();
        Tracer(plan, NatureEvenementPlan.Reprise, horloge.GetUtcNow(), detail: null);
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    private Guid Auteur => compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");

    private static Guid Commande(Guid? commandeId) =>
        commandeId is { } commande && commande != Guid.Empty ? commande : throw new SaisieInvalideException("commandeId", "Identifiant d'envoi obligatoire.");

    // Plan neuf, tracé ; rejeu de la même commande (ou course parallèle perdue) : le plan déjà enregistré.
    private async Task<Guid> AjouterAsync(PlanPreventif plan, DateTimeOffset maintenant, CancellationToken cancellationToken)
    {
        if (await DejaCreeAsync(plan, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja;
        }

        context.Plans.Add(plan);
        Tracer(plan, NatureEvenementPlan.Creation, maintenant, new
        {
            proposition = plan.PropositionCle,
            nature = plan.Nature.ToString(),
            cible = plan.Cible.ToString(),
            composantId = plan.ComposantId,
            zoneId = plan.ZoneId,
            batimentId = plan.BatimentId,
            corpsEtat = plan.CorpsEtat,
            regle = plan.Releve(),
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            return await DejaCreeAsync(plan, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Doublon signalé par la base mais introuvable.", doublon);
        }

        return plan.Id;
    }

    // Même commande : même plan si même envoi, sinon 409 ; proposition déjà confirmée par une autre commande : 409.
    private async Task<Guid?> DejaCreeAsync(PlanPreventif envoye, CancellationToken cancellationToken)
    {
        var recu = await context.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.CommandeId == envoye.CommandeId, cancellationToken).ConfigureAwait(false);
        if (recu is not null)
        {
            return recu.SiteId == envoye.SiteId && recu.MemeEnvoi(envoye) ? recu.Id : throw new CommandeReutiliseeException();
        }

        return envoye.PropositionCle is { } cle
            && await context.Plans.AnyAsync(p => p.SiteId == envoye.SiteId && p.PropositionCle == cle, cancellationToken).ConfigureAwait(false)
            ? throw new PropositionDejaPlanifieeException()
            : null;
    }

    private async Task<PlanPropose> PropositionAsync(Guid siteId, string? cle, DateOnly aujourdhui, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cle))
        {
            throw new SaisieInvalideException("cle", "Clé de la proposition obligatoire.");
        }

        var etablissement = await inventaire.EtablissementAsync(siteId, cancellationToken).ConfigureAwait(false);
        var composants = await inventaire.ComposantsAsync(siteId, ids: null, cancellationToken).ConfigureAwait(false);
        return Catalogue.Proposer(etablissement, composants, aujourdhui).SingleOrDefault(p => p.Cle == cle)
            ?? throw new SaisieInvalideException("cle", "Proposition inconnue sur ce site (classement ou composant changé ?).");
    }

    private async Task<DefinitionPlan> DefinitionAsync(Guid siteId, DemandePlan demande, BasePlan base_, CancellationToken cancellationToken)
    {
        var intervenant = await IntervenantAsync(siteId, demande.IntervenantCompteId, demande.Prestataire, demande.ReferentCompteId, cancellationToken)
            .ConfigureAwait(false);
        var periode = demande.Periode is { } p ? Periode.Valider(p.Valeur, SaisiePreventif.Facultatif<UnitePeriode>(p.Unite, "periode.unite")) : null;
        return new DefinitionPlan(
            demande.Libelle, base_, demande.Reference, intervenant, SaisiePreventif.Facultatif<DonneurOrdre>(demande.DonneurOrdre, "donneurOrdre"),
            CalendrierPlan.Valider(periode, SaisiePreventif.Facultatif<ModeEcheance>(demande.Mode, "mode"), demande.PremiereEcheance,
                JourCivil.Du(horloge.GetUtcNow())));
    }

    // Intervenant interne : un technicien du site ; référent d'un prestataire : technicien, responsable ou délégué du site.
    private async Task<IntervenantPlan> IntervenantAsync(Guid siteId, Guid? compteId, string? prestataire, Guid? referentId, CancellationToken cancellationToken)
    {
        var intervenant = IntervenantPlan.Valider(compteId, prestataire, referentId);
        var demandes = new[] { intervenant.CompteId, intervenant.ReferentCompteId }.Where(c => c is not null).Select(c => c!.Value).ToList();
        if (demandes.Count == 0)
        {
            return intervenant;
        }

        var terrain = (await annuaire.MembresAyantDroitAsync(siteId, Droit.ExecuterControle, demandes, cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.CompteId);
        if (intervenant.CompteId is { } interne && !(terrain.TryGetValue(interne, out var membre) && membre.Roles.Contains(Technicien)))
        {
            throw new SaisieInvalideException("intervenantCompteId", "Un technicien de ce site.");
        }

        return intervenant.ReferentCompteId is { } referent && !terrain.ContainsKey(referent)
            ? throw new SaisieInvalideException("referentCompteId", "Technicien, responsable ou délégué de ce site.")
            : intervenant;
    }

    // Cible désignée dans le corps : absente du site, c'est une saisie invalide (422), pas une ressource introuvable.
    private async Task<CibleDuPlan> CibleAsync(Guid siteId, DemandeCible cible, CancellationToken cancellationToken)
    {
        switch (SaisiePreventif.Valeur<CiblePlan>(cible.Type, "cible.type"))
        {
            case CiblePlan.Composant:
                var id = cible.ComposantId ?? throw new SaisieInvalideException("cible.composantId", "Composant obligatoire.");
                var composant = (await inventaire.ComposantsAsync(siteId, [id], cancellationToken).ConfigureAwait(false)).SingleOrDefault();
                return composant is { Retire: false, SoumisAVerification: true }
                    ? new CibleDuPlan(CiblePlan.Composant, composant.BatimentId, composant.ZoneId, composant.Id, composant.CorpsEtat)
                    : throw new SaisieInvalideException("cible.composantId", "Composant actif et soumis à vérification de ce site.");
            case CiblePlan.ZoneCorpsEtat:
                var zoneId = cible.ZoneId ?? throw new SaisieInvalideException("cible.zoneId", "Zone obligatoire.");
                var lieu = await inventaire.LieuAsync(siteId, zoneId, composantId: null, composantActif: true, cancellationToken).ConfigureAwait(false)
                    ?? throw new SaisieInvalideException("cible.zoneId", "Zone inconnue sur ce site.");
                var grille = await inventaire.GrilleAsync(siteId, cible.CorpsEtat ?? string.Empty, cancellationToken).ConfigureAwait(false)
                    ?? throw new SaisieInvalideException("cible.corpsEtat", "Corps d'état inconnu.");
                return new CibleDuPlan(CiblePlan.ZoneCorpsEtat, lieu.BatimentId!.Value, lieu.ZoneId, null, grille.CorpsEtat);
            default:
                var batimentId = cible.BatimentId ?? throw new SaisieInvalideException("cible.batimentId", "Bâtiment obligatoire.");
                var batiment = (await inventaire.EtablissementAsync(siteId, cancellationToken).ConfigureAwait(false)).Batiments
                    .SingleOrDefault(b => b.Id == batimentId && b.PartiesCommunesId is not null)
                    ?? throw new SaisieInvalideException("cible.batimentId", "Bâtiment inconnu sur ce site.");
                return new CibleDuPlan(CiblePlan.Batiment, batiment.Id, batiment.PartiesCommunesId!.Value, null, null);
        }
    }

    private async Task<PlanPreventif> PlanAVersionAsync(Guid siteId, Guid id, uint? version, CancellationToken cancellationToken)
    {
        var attendue = version ?? throw new SaisieInvalideException("version", "Version du plan lue obligatoire.");
        var plan = await context.Plans.SingleOrDefaultAsync(p => p.Id == id && p.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new PlanIntrouvableException();
        // Comparée explicitement (une demande sans changement est aussi refusée si elle est dépassée), puis gardée comme
        // valeur d'origine : une écriture concurrente entre-temps est refusée à l'enregistrement.
        var xmin = context.Entry(plan).Property<uint>("xmin");
        if (xmin.CurrentValue != attendue)
        {
            throw new PlanModifieException();
        }

        xmin.OriginalValue = attendue;
        return plan;
    }

    private void Tracer(PlanPreventif plan, NatureEvenementPlan nature, DateTimeOffset le, object? detail) =>
        context.JournalPreventif.Add(new EvenementPlan(Guid.CreateVersion7(le), plan, nature, compte.Id, le, detail is null ? null : JsonSerializer.Serialize(detail)));

    private async Task EnregistrerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PlanModifieException();
        }
    }

    // Lecture des plans : cible nommée depuis l'inventaire, suspension automatique d'un composant retiré ou plus soumis à
    // vérification (le retrait reste tracé au journal de l'inventaire), échéance courante calculée.
    private async Task<IReadOnlyList<PlanLu>> LireAsync(Guid siteId, IReadOnlyList<(PlanPreventif Plan, uint Version)> plans, CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        var composants = (await inventaire.ComposantsAsync(
                siteId, [.. plans.Where(p => p.Plan.ComposantId is not null).Select(p => p.Plan.ComposantId!.Value).Distinct()], cancellationToken)
            .ConfigureAwait(false)).ToDictionary(c => c.Id);
        var zones = await inventaire.ZonesAsync(siteId, [.. plans.Select(p => p.Plan.ZoneId).Distinct()], cancellationToken).ConfigureAwait(false);
        var batiments = (await inventaire.EtablissementAsync(siteId, cancellationToken).ConfigureAwait(false)).Batiments.ToDictionary(b => b.Id, b => b.Nom);
        var noms = await annuaire.NomsAsync(
            [.. plans.SelectMany(p => new[] { p.Plan.IntervenantCompteId, p.Plan.ReferentCompteId }).Where(c => c is not null).Select(c => c!.Value).Distinct()],
            cancellationToken).ConfigureAwait(false);
        var aujourdhui = JourCivil.Du(horloge.GetUtcNow());
        return [.. plans.Select(l =>
        {
            var p = l.Plan;
            var composant = p.ComposantId is { } cid ? composants.GetValueOrDefault(cid) : null;
            var automatique = composant switch
            {
                { Retire: true } => "Composant retiré de l'inventaire",
                { SoumisAVerification: false } => "Composant non soumis à vérification",
                _ => null,
            };
            var libelle = p.Cible switch
            {
                CiblePlan.Composant => composant is null ? "Composant" : $"{composant.Nom} ({composant.Zone})",
                CiblePlan.ZoneCorpsEtat => $"{zones.GetValueOrDefault(p.ZoneId, "Zone")} — {p.CorpsEtat}",
                _ => batiments.GetValueOrDefault(p.BatimentId, "Bâtiment"),
            };
            var suspendu = p.Suspendu || automatique is not null;
            return new PlanLu(
                p.Id, p.Libelle, p.Nature.ToString(), p.Base.ToString(), p.Reference, p.Modele, p.PropositionCle, p.TypeIntervenant?.ToString(),
                new CibleLue(p.Cible.ToString(), p.BatimentId, composant?.ZoneId ?? p.ZoneId, p.ComposantId, p.CorpsEtat, libelle),
                p.IntervenantCompteId is null && p.Prestataire is null
                    ? null
                    : new IntervenantLu(p.IntervenantCompteId, p.IntervenantCompteId is { } i ? noms.GetValueOrDefault(i) : null, p.Prestataire, p.ReferentCompteId,
                        p.ReferentCompteId is { } r ? noms.GetValueOrDefault(r) : null),
                p.DonneurOrdre?.ToString(), p.Periode is { } periode ? new PeriodeLue(periode.Valeur, periode.Unite.ToString()) : null, p.Mode.ToString(),
                p.PremiereEcheance, suspendu, p.MotifSuspension ?? automatique, automatique is not null && !p.Suspendu,
                suspendu ? null : Prochaine(p, [], aujourdhui), l.Version);
        })];
    }

    private static EcheanceLue? Prochaine(PlanPreventif plan, IReadOnlyCollection<OccurrenceConnue> occurrences, DateOnly aujourdhui)
    {
        if (Echeancier.Prochaine(plan.Calendrier, occurrences) is not { } echeance)
        {
            return null;
        }

        var fenetre = Echeancier.Fenetre(plan.Periode, echeance);
        var materialisee = occurrences.FirstOrDefault(o => o.Echeance == echeance);
        return new EcheanceLue(echeance, echeance.AddDays(-fenetre), Echeancier.Etat(echeance, fenetre, aujourdhui).ToString(), materialisee?.Statut.ToString());
    }

    private static JsonElement? Json(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>Valeurs d'énumération reçues en texte (nom exact) : obligatoires ou facultatives.</summary>
internal static class SaisiePreventif
{
    internal static T Valeur<T>(string? texte, string champ) where T : struct, Enum => Saisie.Valeur<T>(texte, champ);

    internal static T? Facultatif<T>(string? texte, string champ) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(texte) ? null : Saisie.Valeur<T>(texte, champ);
}

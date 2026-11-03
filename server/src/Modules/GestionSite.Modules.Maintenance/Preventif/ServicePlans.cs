using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.Preventif;

internal sealed record IntervenantLu(Guid? CompteId, string? Nom, string? Prestataire, Guid? ReferentCompteId, string? Referent);

/// <summary>
/// Échéance courante d'un plan : date, ouverture de la fenêtre « À faire », position ; matérialisée (affectée), son
/// occurrence, son intervenant et sa version (0 sinon, valeur attendue par l'affectation).
/// </summary>
internal sealed record EcheanceLue(
    DateOnly Echeance, DateOnly OuvertureFenetre, string Etat, string? Statut, Guid? OccurrenceId = null, IntervenantLu? Intervenant = null, uint Version = 0);

/// <summary>Occurrence matérialisée d'un plan (affectée, réalisée ou sautée), la plus récente d'abord.</summary>
internal sealed record OccurrenceLue(
    Guid Id, DateOnly Echeance, string Statut, IntervenantLu? Intervenant, DateTimeOffset? AffecteeLe, DateOnly? RealiseeLe, DateOnly? SauteeLe,
    string? MotifSaut, string? CommentaireSaut, Guid? ControleId, uint Version);

internal sealed record PlanLu(
    Guid Id, string Libelle, string Nature, string Base, string? Reference, string? Modele, string? PropositionCle, string? TypeIntervenant, CibleLue Cible,
    IntervenantLu? Intervenant, string? DonneurOrdre, PeriodeLue? Periode, string Mode, DateOnly PremiereEcheance, bool Suspendu, string? MotifSuspension,
    bool SuspensionAutomatique, EcheanceLue? Prochaine, uint Version);

internal sealed record EvenementPlanLu(DateTimeOffset Le, string Nature, Guid? AuteurCompteId, string? Auteur, JsonElement? Detail);

internal sealed record FichePlan(PlanLu Plan, IReadOnlyList<OccurrenceLue> Occurrences, IReadOnlyList<EvenementPlanLu> Historique);

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
        var occurrences = await OccurrencesAsync([id], cancellationToken).ConfigureAwait(false);
        var levees = await Constats.LeveesAsync(
            context, [.. occurrences.Where(o => o.Occurrence.Statut == StatutOccurrence.RealiseeAvecReserves).Select(o => o.Occurrence.Id)], cancellationToken)
            .ConfigureAwait(false);
        var noms = await NomsAsync(occurrences.Select(o => (o.Occurrence.IntervenantCompteId, o.Occurrence.ReferentCompteId)), cancellationToken).ConfigureAwait(false);
        return new FichePlan(
            plan,
            [.. occurrences.OrderByDescending(o => o.Occurrence.Echeance).Select(l =>
            {
                var o = l.Occurrence;
                return new OccurrenceLue(
                    o.Id, o.Echeance, levees.Contains(o.Id) ? "ReservesLevees" : o.Statut.ToString(),
                    Intervenant(o.IntervenantCompteId, o.Prestataire, o.ReferentCompteId, noms), o.AffecteeLe, o.RealiseeLe,
                    o.SauteeLe, o.MotifSaut?.ToString(), o.CommentaireSaut, o.ControleId, l.Version);
            })],
            [.. journal.Select(e => new EvenementPlanLu(
                e.Le, e.Nature.ToString(), e.AuteurCompteId, e.AuteurCompteId is { } a ? auteurs.GetValueOrDefault(a) : null, Json(e.Detail)))]);
    }

    /// <summary>Échéances affectées à l'appelant (technicien, ou référent d'un prestataire), par échéance.</summary>
    internal async Task<IReadOnlyList<PlanLu>> MesControlesAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ExecuterControle, cancellationToken).ConfigureAwait(false);
        var moi = compte.Id;
        // Seuls les plans dont une occurrence ouverte est confiée à l'appelant sont relus.
        var plans = await context.Plans.AsNoTracking()
            .Where(p => p.SiteId == siteId && context.Occurrences.Any(o => o.PlanId == p.Id && o.Statut == StatutOccurrence.Affectee
                && (o.IntervenantCompteId == moi || o.ReferentCompteId == moi)))
            .Select(p => new { Plan = p, Version = EF.Property<uint>(p, "xmin") }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. (await LireAsync(siteId, [.. plans.Select(p => (p.Plan, p.Version))], cancellationToken).ConfigureAwait(false))
            .Where(p => p.Prochaine?.Intervenant is { } i && (i.CompteId == moi || i.ReferentCompteId == moi))
            .OrderBy(p => p.Prochaine!.Echeance).ThenBy(p => p.Libelle, StringComparer.Ordinal)];
    }

    /// <summary>Plan saisi par R/D ; même commande rejouée : même plan.</summary>
    internal async Task<Guid> CreerAsync(Guid organisationId, Guid siteId, DemandePlan demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var commande = Commande(demande.CommandeId);
        var demandee = demande.Cible ?? throw new SaisieInvalideException("cible", "Cible obligatoire.");
        var nature = Saisie.Valeur<NaturePlan>(demande.Nature, "nature");
        var auteur = Auteur;
        bool MemeDemande(PlanPreventif recu) => recu.SiteId == siteId && recu.CreeParCompteId == auteur && recu.PropositionCle is null
            && recu.Nature == nature && recu.Cible.ToString() == demandee.Type && recu.ComposantId == demandee.ComposantId
            && (recu.Cible != CiblePlan.ZoneCorpsEtat || (recu.ZoneId == demandee.ZoneId && recu.CorpsEtat == demandee.CorpsEtat))
            && (recu.Cible != CiblePlan.Batiment || recu.BatimentId == demandee.BatimentId);

        // Rejeu reconnu avant toute revalidation : un inventaire changé depuis l'envoi ne le refuse pas.
        if (await DejaCreeAsync(commande, MemeDemande, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja;
        }

        var cible = await CibleAsync(siteId, demandee, cancellationToken).ConfigureAwait(false);
        var definition = await DefinitionAsync(siteId, demande, Saisie.Valeur<BasePlan>(demande.Base, "base"), actuelle: null, cancellationToken)
            .ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var plan = new PlanPreventif(
            Guid.CreateVersion7(maintenant), organisationId, siteId, commande, propositionCle: null, cible, nature, typeIntervenant: null, definition, auteur, maintenant);
        context.Plans.Add(plan);
        TracerCreation(plan, maintenant);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Même commande reçue en parallèle : l'autre requête l'a enregistrée ; même réponse que pour un rejeu.
            context.ChangeTracker.Clear();
            return await DejaCreeAsync(commande, MemeDemande, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Doublon signalé par la base mais introuvable.", doublon);
        }

        return plan.Id;
    }

    /// <summary>
    /// Confirmation d'une proposition du catalogue (décision du 30/10), écartée ou non : modèle, cible et référence « à
    /// confirmer » repris ; première échéance suggérée sauf choix de R/D. Une proposition ne donne qu'un plan.
    /// </summary>
    internal async Task<Guid> ConfirmerAsync(Guid organisationId, Guid siteId, DemandeConfirmation demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var commande = Commande(demande.CommandeId);
        var auteur = Auteur;
        bool MemeDemande(PlanPreventif recu) => recu.SiteId == siteId && recu.CreeParCompteId == auteur && recu.PropositionCle == demande.Cle;
        if (await DejaCreeAsync(commande, MemeDemande, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja; // rejeu reconnu avant toute revalidation
        }

        var aujourdhui = JourCivil.Du(horloge.GetUtcNow());
        var proposition = await PropositionAsync(siteId, demande.Cle, aujourdhui, cancellationToken).ConfigureAwait(false);
        var modele = proposition.Modele;
        var intervenant = await IntervenantAsync(siteId, demande.IntervenantCompteId, demande.Prestataire, demande.ReferentCompteId, cancellationToken)
            .ConfigureAwait(false);
        // Échéance suggérée très ancienne (commission jamais revenue) : ramenée à la borne, toujours en retard.
        var premiere = demande.PremiereEcheance ?? (proposition.Echeance < aujourdhui.AddYears(-5) ? aujourdhui.AddYears(-5) : proposition.Echeance);
        var definition = new DefinitionPlan(
            modele.Libelle, modele.Base, modele.Reference, intervenant, SaisiePreventif.Facultatif<DonneurOrdre>(demande.DonneurOrdre, "donneurOrdre"),
            CalendrierPlan.Valider(modele.Periode, modele.Mode, premiere, aujourdhui));
        var maintenant = horloge.GetUtcNow();
        var plan = new PlanPreventif(
            Guid.CreateVersion7(maintenant), organisationId, siteId, commande, proposition.Cle,
            new CibleDuPlan(proposition.Cible, proposition.BatimentId, proposition.ZoneId, proposition.ComposantId, proposition.CorpsEtat),
            modele.Nature, modele.Intervenant, definition, auteur, maintenant);

        // Proposition verrouillée le temps de la transaction : confirmation et écart simultanés ne se croisent pas.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await VerrouillerAsync(siteId, proposition.Cle, cancellationToken).ConfigureAwait(false);
        if (await DejaCreeAsync(commande, MemeDemande, cancellationToken).ConfigureAwait(false) is { } concurrent)
        {
            return concurrent; // même commande enregistrée pendant l'attente du verrou
        }

        if (await context.Plans.AnyAsync(p => p.SiteId == siteId && p.PropositionCle == proposition.Cle, cancellationToken).ConfigureAwait(false))
        {
            throw new PropositionDejaPlanifieeException();
        }

        context.Plans.Add(plan);
        TracerCreation(plan, maintenant);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return plan.Id;
    }

    /// <summary>Proposition écartée avec son motif ; elle reste visible. Déjà écartée : rien ne change ; déjà confirmée : 409.</summary>
    internal async Task EcarterAsync(Guid organisationId, Guid siteId, DemandeEcart demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var proposition = await PropositionAsync(siteId, demande.Cle, JourCivil.Du(maintenant), cancellationToken).ConfigureAwait(false);
        var ecart = new PropositionEcartee(
            Guid.CreateVersion7(maintenant), organisationId, siteId, proposition.Cle, Saisie.Valeur<MotifEcart>(demande.Motif, "motif"),
            demande.Commentaire, Auteur, maintenant);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await VerrouillerAsync(siteId, proposition.Cle, cancellationToken).ConfigureAwait(false);
        if (await context.Plans.AnyAsync(p => p.SiteId == siteId && p.PropositionCle == proposition.Cle, cancellationToken).ConfigureAwait(false))
        {
            throw new PropositionDejaPlanifieeException();
        }

        if (await context.PropositionsEcartees.AnyAsync(e => e.SiteId == siteId && e.Cle == proposition.Cle, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        context.PropositionsEcartees.Add(ecart);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Définition modifiée à la version lue ; seules les échéances non matérialisées en dépendent. Sans changement, rien n'est écrit.</summary>
    internal async Task ModifierAsync(Guid organisationId, Guid siteId, Guid id, DemandePlan demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var plan = await PlanAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        var definition = await DefinitionAsync(siteId, demande, Saisie.Valeur<BasePlan>(demande.Base, "base"), plan.PremiereEcheance, cancellationToken)
            .ConfigureAwait(false);
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

    private void TracerCreation(PlanPreventif plan, DateTimeOffset maintenant) => Tracer(plan, NatureEvenementPlan.Creation, maintenant, new
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

    // Même commande : le plan enregistré si c'est le même envoi (même auteur, même demande), sinon 409.
    private async Task<Guid?> DejaCreeAsync(Guid commande, Func<PlanPreventif, bool> memeDemande, CancellationToken cancellationToken)
    {
        var recu = await context.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.CommandeId == commande, cancellationToken).ConfigureAwait(false);
        return recu switch
        {
            null => null,
            _ when memeDemande(recu) => recu.Id,
            _ => throw new CommandePreventifReutiliseeException(),
        };
    }

    // Verrou transactionnel sur la proposition (site et clé), relâché à la fin de la transaction.
    private async Task VerrouillerAsync(Guid siteId, string cle, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"{siteId:N}:{cle}"}, 0))", cancellationToken)
            .ConfigureAwait(false);

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

    // Première échéance inchangée (modification d'un plan ancien) : jamais refusée par la fenêtre autour d'aujourd'hui.
    private async Task<DefinitionPlan> DefinitionAsync(Guid siteId, DemandePlan demande, BasePlan base_, DateOnly? actuelle, CancellationToken cancellationToken)
    {
        var intervenant = await IntervenantAsync(siteId, demande.IntervenantCompteId, demande.Prestataire, demande.ReferentCompteId, cancellationToken)
            .ConfigureAwait(false);
        var periode = demande.Periode is { } p ? Periode.Valider(p.Valeur, SaisiePreventif.Facultatif<UnitePeriode>(p.Unite, "periode.unite")) : null;
        return new DefinitionPlan(
            demande.Libelle, base_, demande.Reference, intervenant, SaisiePreventif.Facultatif<DonneurOrdre>(demande.DonneurOrdre, "donneurOrdre"),
            CalendrierPlan.Valider(periode, SaisiePreventif.Facultatif<ModeEcheance>(demande.Mode, "mode"), demande.PremiereEcheance,
                JourCivil.Du(horloge.GetUtcNow()), actuelle));
    }

    // Intervenant interne : un technicien du site ; référent d'un prestataire : technicien, responsable ou délégué du site.
    private async Task<IntervenantPlan> IntervenantAsync(Guid siteId, Guid? compteId, string? prestataire, Guid? referentId, CancellationToken cancellationToken)
    {
        var intervenant = IntervenantPlan.Valider(compteId, prestataire, referentId);
        await Intervenants.ExigerAsync(annuaire, siteId, Droit.ExecuterControle, intervenant.CompteId, intervenant.ReferentCompteId, cancellationToken)
            .ConfigureAwait(false);
        return intervenant;
    }

    // Cible désignée dans le corps : absente du site, c'est une saisie invalide (422), pas une ressource introuvable.
    private async Task<CibleDuPlan> CibleAsync(Guid siteId, DemandeCible cible, CancellationToken cancellationToken)
    {
        switch (Saisie.Valeur<CiblePlan>(cible.Type, "cible.type"))
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
        var occurrences = (await OccurrencesAsync([.. plans.Select(p => p.Plan.Id)], cancellationToken).ConfigureAwait(false)).ToLookup(o => o.Occurrence.PlanId);
        var noms = await NomsAsync(
            plans.Select(p => (p.Plan.IntervenantCompteId, p.Plan.ReferentCompteId))
                .Concat(occurrences.SelectMany(g => g).Select(o => (o.Occurrence.IntervenantCompteId, o.Occurrence.ReferentCompteId))),
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
                // Composant : son lieu et son corps d'état actuels (il a pu être déplacé ou corrigé depuis la création du plan).
                new CibleLue(
                    p.Cible.ToString(), composant?.BatimentId ?? p.BatimentId, composant?.ZoneId ?? p.ZoneId, p.ComposantId, composant?.CorpsEtat ?? p.CorpsEtat,
                    libelle),
                Intervenant(p.IntervenantCompteId, p.Prestataire, p.ReferentCompteId, noms),
                p.DonneurOrdre?.ToString(), p.Periode is { } periode ? new PeriodeLue(periode.Valeur, periode.Unite.ToString()) : null, p.Mode.ToString(),
                p.PremiereEcheance, suspendu, p.MotifSuspension ?? automatique, automatique is not null && !p.Suspendu,
                suspendu ? null : Prochaine(p, [.. occurrences[p.Id]], noms, aujourdhui), l.Version);
        })];
    }

    // Occurrences matérialisées des plans, avec leur version (xmin) : l'échéance courante en dépend.
    private async Task<List<(OccurrencePreventive Occurrence, uint Version)>> OccurrencesAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken) =>
        [.. (await context.Occurrences.AsNoTracking().Where(o => planIds.Contains(o.PlanId))
            .Select(o => new { Occurrence = o, Version = EF.Property<uint>(o, "xmin") }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(o => (o.Occurrence, o.Version))];

    private Task<IReadOnlyDictionary<Guid, string>> NomsAsync(IEnumerable<(Guid? Intervenant, Guid? Referent)> comptes, CancellationToken cancellationToken) =>
        annuaire.NomsAsync([.. comptes.SelectMany(c => new[] { c.Intervenant, c.Referent }).Where(c => c is not null).Select(c => c!.Value).Distinct()], cancellationToken);

    private static IntervenantLu? Intervenant(Guid? compteId, string? prestataire, Guid? referentId, IReadOnlyDictionary<Guid, string> noms) =>
        compteId is null && prestataire is null
            ? null
            : new IntervenantLu(compteId, compteId is { } i ? noms.GetValueOrDefault(i) : null, prestataire, referentId, referentId is { } r ? noms.GetValueOrDefault(r) : null);

    private static EcheanceLue? Prochaine(
        PlanPreventif plan, IReadOnlyList<(OccurrencePreventive Occurrence, uint Version)> occurrences, IReadOnlyDictionary<Guid, string> noms, DateOnly aujourdhui)
    {
        if (Echeancier.Prochaine(plan.Calendrier, [.. occurrences.Select(o => o.Occurrence.Connue)]) is not { } echeance)
        {
            return null;
        }

        var fenetre = Echeancier.Fenetre(plan.Periode, echeance);
        var etat = Echeancier.Etat(echeance, fenetre, aujourdhui).ToString();
        return occurrences.FirstOrDefault(o => o.Occurrence.Echeance == echeance) is { Occurrence: { } materialisee } lue
            ? new EcheanceLue(echeance, echeance.AddDays(-fenetre), etat, materialisee.Statut.ToString(), materialisee.Id,
                Intervenant(materialisee.IntervenantCompteId, materialisee.Prestataire, materialisee.ReferentCompteId, noms), lue.Version)
            : new EcheanceLue(echeance, echeance.AddDays(-fenetre), etat, null);
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

/// <summary>Valeur d'énumération facultative reçue en texte (nom exact) ; vide : absente.</summary>
internal static class SaisiePreventif
{
    internal static T? Facultatif<T>(string? texte, string champ) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(texte) ? null : Saisie.Valeur<T>(texte, champ);
}

/// <summary>Identifiant d'envoi déjà utilisé pour une autre saisie du préventif (409 « commande_reutilisee »).</summary>
internal sealed class CommandePreventifReutiliseeException() : Exception("Cet identifiant d'envoi a déjà servi pour une autre saisie du préventif.");

using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.Preventif;

internal sealed record ControleEnregistre(Guid Id, string Statut, Guid? EvaluationId, decimal? Note, string? Libelle);

internal sealed record DemandeAffectationControle(DateOnly? Echeance, Guid? IntervenantCompteId, string? Prestataire, Guid? ReferentCompteId, uint? Version);

internal sealed record DemandeSaut(DateOnly? Echeance, string? Motif, string? Commentaire);

internal sealed record DemandePoint(bool? Conforme, bool Danger);

internal sealed record DemandeReleve(string? Libelle, string? Unite, decimal? Valeur);

// Inspection : grilleVersion, points (un par point de la grille), mesures (une par mesure de la grille) ; inspection d'un
// composant : état (individuel) ou répartition (lot) de l'évaluation US-11. Entretien et vérification : relevés libres.
internal sealed record DemandeRealisation(
    Guid? CommandeId, DateOnly? Echeance, DateOnly? RealiseLe, string? Resultat, string? CompteRendu, int? GrilleVersion,
    IReadOnlyList<DemandePoint?>? Points, IReadOnlyList<decimal?>? Mesures, IReadOnlyList<DemandeReleve?>? Releves, string? Etat,
    RepartitionInventaire? Repartition, string? Prestataire, string? NumeroAgrement);

internal sealed record ControleResume(
    Guid Id, Guid PlanId, string Plan, string Nature, DateOnly Echeance, DateOnly RealiseLe, string Resultat, Guid? ComposantId, Guid BatimentId, int Defauts,
    decimal? Note);

internal sealed record ControleLu(
    Guid Id, Guid PlanId, string Plan, string Nature, DateOnly Echeance, DateOnly RealiseLe, DateTimeOffset SaisiLe, Guid AuteurCompteId, string? Auteur,
    Guid? IntervenantCompteId, string? Intervenant, string? Prestataire, string? NumeroAgrement, string Resultat, string? CompteRendu, string? CorpsEtat,
    Guid? ComposantId, Guid ZoneId, Guid BatimentId, int? GrilleVersion, IReadOnlyList<PointControle> Points, IReadOnlyList<MesureControle> Mesures,
    Guid? EvaluationId, decimal? Note, string? LibelleNote, IReadOnlyList<int> Photos);

/// <summary>Échéance qui n'est pas l'échéance courante du plan (422 « echeance »).</summary>
internal sealed class EcheanceInattendueException() : Exception("Échéance attendue : l'échéance courante du plan.");

/// <summary>Contrôle absent du site (404).</summary>
internal sealed class ControleIntrouvableException() : Exception("Contrôle introuvable sur ce site.");

/// <summary>
/// Exécution des occurrences (US-12, S12-03) : affectation par R/D (« Planifier le préventif »), saut motivé et
/// réalisation par la personne en charge (technicien affecté ou référent du prestataire) ou par R/D (« Exécuter un
/// contrôle », décision du 30/10). L'échéance visée est toujours l'échéance courante du plan ; l'occurrence n'est
/// enregistrée qu'à ce moment (clé plan et échéance). Une inspection de composant enregistre aussi son évaluation US-11.
/// </summary>
internal sealed class ServiceOccurrences(
    MaintenanceDbContext context, IAutorisations autorisations, IInventaireSite inventaire, IEvaluationsInventaire evaluations, IAnnuaireMembres annuaire,
    IStockageFichiers stockage, CompteCourant compte, TimeProvider horloge)
{
    /// <summary>Affectation de l'échéance courante, à la version lue de l'occurrence (0 tant qu'elle n'existe pas).</summary>
    internal async Task AffecterAsync(Guid organisationId, Guid siteId, Guid planId, DemandeAffectationControle demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        var (plan, occurrences, _) = await PlanActifAsync(siteId, planId, cancellationToken).ConfigureAwait(false);
        var echeance = Courante(plan, occurrences, demande.Echeance);
        var demandee = IntervenantPlan.Valider(demande.IntervenantCompteId, demande.Prestataire, demande.ReferentCompteId);
        var intervenant = demandee.CompteId is null && demandee.Prestataire is null
            ? new IntervenantPlan(plan.IntervenantCompteId, plan.Prestataire, plan.ReferentCompteId)
            : demandee;
        await Intervenants.ExigerAsync(annuaire, siteId, Droit.ExecuterControle, intervenant.CompteId, intervenant.ReferentCompteId, cancellationToken)
            .ConfigureAwait(false);
        var version = demande.Version ?? throw new SaisieInvalideException("version", "Version de l'occurrence lue obligatoire (0 si elle n'existe pas).");
        var maintenant = horloge.GetUtcNow();
        var occurrence = occurrences.SingleOrDefault(o => o.Echeance == echeance);
        if (occurrence is null)
        {
            if (version != 0)
            {
                throw new PlanModifieException();
            }

            occurrence = new OccurrencePreventive(Guid.CreateVersion7(maintenant), plan, echeance, maintenant);
            context.Occurrences.Add(occurrence);
        }
        else
        {
            ExigerVersion(occurrence, version);
        }

        if (!occurrence.Affecter(intervenant, maintenant))
        {
            return;
        }

        Tracer(plan, NatureEvenementPlan.Affectation, maintenant, new
        {
            echeance,
            intervenantCompteId = intervenant.CompteId,
            prestataire = intervenant.Prestataire,
            referentCompteId = intervenant.ReferentCompteId,
        });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saut motivé de l'échéance courante ; en mode glissant, la suivante repart de la date du saut. Rejoué : sans effet.</summary>
    internal async Task SauterAsync(Guid organisationId, Guid siteId, Guid planId, DemandeSaut demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ExecuterControle, cancellationToken).ConfigureAwait(false);
        var motif = Saisie.Valeur<MotifSaut>(demande.Motif, "motif");
        var (plan, occurrences, _) = await PlanActifAsync(siteId, planId, cancellationToken).ConfigureAwait(false);
        if (occurrences.SingleOrDefault(o => o.Echeance == demande.Echeance) is { Statut: StatutOccurrence.Sautee } deja && deja.MotifSaut == motif)
        {
            return; // même saut rejoué
        }

        var echeance = Courante(plan, occurrences, demande.Echeance);
        var occurrence = occurrences.SingleOrDefault(o => o.Echeance == echeance);
        await ExigerEnChargeAsync(organisationId, siteId, occurrence, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        if (occurrence is null)
        {
            occurrence = new OccurrencePreventive(Guid.CreateVersion7(maintenant), plan, echeance, maintenant);
            context.Occurrences.Add(occurrence);
        }

        occurrence.Sauter(motif, demande.Commentaire, JourCivil.Du(maintenant));
        Tracer(plan, NatureEvenementPlan.Saut, maintenant, new { echeance, motif = motif.ToString(), commentaire = occurrence.CommentaireSaut });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Réalisation de l'échéance courante, saisie sur place ou après coup avec sa date réelle. Le formulaire n'est lu
    /// qu'après le contrôle d'accès (403/404 d'abord). Inspection d'un composant : l'évaluation US-11 est enregistrée
    /// d'abord (commande dérivée), puis le contrôle ; un rejeu après un échec partiel retrouve l'évaluation sans la doubler.
    /// </summary>
    internal async Task<ControleEnregistre> RealiserAsync(
        Guid organisationId, Guid siteId, Guid planId, Func<Task<(DemandeRealisation Demande, IReadOnlyList<PhotoRecue> Photos)>> lire,
        CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ExecuterControle, cancellationToken).ConfigureAwait(false);
        var plan = await context.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId && p.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new PlanIntrouvableException();
        var (demande, photos) = await lire().ConfigureAwait(false);
        var commande = demande.CommandeId is { } c && c != Guid.Empty ? c : throw new SaisieInvalideException("commandeId", "Identifiant d'envoi obligatoire.");
        var auteur = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        var saisie = SaisieDe(commande, demande);
        if (await DejaRealiseAsync(auteur, plan, demande.Echeance, saisie, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja; // rejeu reconnu avant les contrôles
        }

        var (_, occurrences, cible) = await PlanActifAsync(siteId, planId, cancellationToken).ConfigureAwait(false);
        var echeance = Courante(plan, occurrences, demande.Echeance);
        var occurrence = occurrences.SingleOrDefault(o => o.Echeance == echeance);
        await ExigerEnChargeAsync(organisationId, siteId, occurrence, cancellationToken).ConfigureAwait(false);

        // 1. Contrôles complets (grille en vigueur du corps d'état actuel, saisie) avant toute écriture.
        var grille = plan.Nature == NaturePlan.InspectionInterne
            ? await inventaire.GrilleAsync(siteId, cible.CorpsEtat!, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Grille introuvable pour le corps d'état du plan.")
            : null;
        var maintenant = horloge.GetUtcNow();
        var nouvelle = occurrence ?? new OccurrencePreventive(Guid.CreateVersion7(maintenant), plan, echeance, maintenant);
        var controle = ControlePreventif.Creer(
            Guid.CreateVersion7(maintenant), plan, cible, nouvelle,
            grille is null ? null : new GrilleControle(grille.Version, grille.Criteres, [.. grille.Mesures.Select(m => (m.Libelle, m.Unite))]),
            saisie, auteur, maintenant);
        var jpegs = photos.Select(p => TraitementPhotos.Preparer(p.Contenu)).ToList();

        // 2. Inspection d'un composant : évaluation US-11 dans la même saisie (décision du 30/10), sans proposition de dossier.
        if (plan.Nature == NaturePlan.InspectionInterne && plan.ComposantId is { } composantId)
        {
            var evaluation = await evaluations.EvaluerAsync(siteId, composantId, new SaisieEvaluationInventaire(
                Commandes.Derivee(commande, "evaluation"), auteur, controle.GrilleVersion!.Value, [.. controle.PointsLus.Select(p => p.Conforme)],
                [.. controle.MesuresLues.Select(m => m.Valeur)], demande.Etat, demande.Repartition, controle.CompteRendu, [.. photos.Select(p => p.Contenu)]),
                cancellationToken).ConfigureAwait(false);
            controle.Evaluer(evaluation.Id, evaluation.Note, evaluation.Libelle);
        }

        // 3. Photos écrites sous des clés propres au contrôle, retirées si l'enregistrement n'aboutit pas.
        await using var envoi = await PhotosStockees.EcrireAsync(
            stockage, jpegs, (rang, jpeg) => new PhotoControle(Guid.CreateVersion7(maintenant), controle, rang, jpeg.Length, maintenant), p => p.Cle,
            enregistrees => { var id = enregistrees[0].Id; return context.PhotosControles.AsNoTracking().AnyAsync(p => p.Id == id, CancellationToken.None); },
            cancellationToken).ConfigureAwait(false);
        if (occurrence is null)
        {
            context.Occurrences.Add(nouvelle); // sinon déjà suivie : réalisée à sa version lue (xmin)
        }

        nouvelle.Realiser(controle, reserves: controle.Defauts > 0);
        context.Controles.Add(controle);
        context.PhotosControles.AddRange(envoi.Photos);
        Tracer(plan, NatureEvenementPlan.Realisation, maintenant, new
        {
            echeance,
            controleId = controle.Id,
            realiseLe = controle.RealiseLe,
            resultat = controle.Resultat.ToString(),
            defauts = controle.Defauts,
            evaluationId = controle.EvaluationId,
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            envoi.Confirmer();
        }
        catch (DbUpdateException concurrent) when (concurrent is DbUpdateConcurrencyException
            || concurrent.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Même envoi reçu en parallèle : la même réponse ; sinon l'occurrence vient d'être résolue par une autre saisie.
            context.ChangeTracker.Clear();
            return await DejaRealiseAsync(auteur, plan, demande.Echeance, saisie, cancellationToken).ConfigureAwait(false) ?? throw new PlanModifieException();
        }

        return Resultat(controle, nouvelle.Statut);
    }

    /// <summary>Historique des contrôles du site, filtrable par plan, composant ou bâtiment, le plus récent d'abord (500 au plus).</summary>
    internal async Task<IReadOnlyList<ControleResume>> ListerAsync(
        Guid organisationId, Guid siteId, Guid? planId, Guid? composantId, Guid? batimentId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var controles = context.Controles.AsNoTracking().Where(c => c.SiteId == siteId);
        if (planId is { } plan)
        {
            controles = controles.Where(c => c.PlanId == plan);
        }

        if (composantId is { } composant)
        {
            controles = controles.Where(c => c.ComposantId == composant);
        }

        if (batimentId is { } batiment)
        {
            controles = controles.Where(c => c.BatimentId == batiment);
        }

        var lus = await (
                from c in controles
                join p in context.Plans.AsNoTracking() on c.PlanId equals p.Id
                orderby c.RealiseLe descending, c.Id descending
                select new { Controle = c, Plan = p.Libelle })
            .Take(500).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. lus.Select(l => new ControleResume(
            l.Controle.Id, l.Controle.PlanId, l.Plan, l.Controle.Nature.ToString(), l.Controle.Echeance, l.Controle.RealiseLe, l.Controle.Resultat.ToString(),
            l.Controle.ComposantId, l.Controle.BatimentId, l.Controle.Defauts, l.Controle.Note))];
    }

    internal async Task<ControleLu> LireAsync(Guid organisationId, Guid siteId, Guid id, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var lu = await (
                from k in context.Controles.AsNoTracking()
                join p in context.Plans.AsNoTracking() on k.PlanId equals p.Id
                where k.Id == id && k.SiteId == siteId
                select new { Controle = k, Plan = p.Libelle })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ControleIntrouvableException();
        var c = lu.Controle;
        var photos = await context.PhotosControles.AsNoTracking().Where(p => p.ControleId == id).OrderBy(p => p.Rang).Select(p => p.Rang)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var noms = await annuaire.NomsAsync([.. new[] { (Guid?)c.AuteurCompteId, c.IntervenantCompteId }.OfType<Guid>().Distinct()], cancellationToken).ConfigureAwait(false);
        return new ControleLu(
            c.Id, c.PlanId, lu.Plan, c.Nature.ToString(), c.Echeance, c.RealiseLe, c.SaisiLe, c.AuteurCompteId, noms.GetValueOrDefault(c.AuteurCompteId),
            c.IntervenantCompteId, c.IntervenantCompteId is { } i ? noms.GetValueOrDefault(i) : null, c.Prestataire, c.NumeroAgrement, c.Resultat.ToString(),
            c.CompteRendu, c.CorpsEtat, c.ComposantId, c.ZoneId, c.BatimentId, c.GrilleVersion, c.PointsLus, c.MesuresLues, c.EvaluationId, c.Note, c.LibelleNote,
            photos);
    }

    /// <summary>Photo d'un contrôle (JPEG), ou null si elle n'existe pas pour ce contrôle du site.</summary>
    internal async Task<byte[]?> PhotoAsync(Guid organisationId, Guid siteId, Guid id, int rang, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var cle = await (
                from p in context.PhotosControles.AsNoTracking()
                join c in context.Controles.AsNoTracking() on p.ControleId equals c.Id
                where c.SiteId == siteId && c.Id == id && p.Rang == rang
                select p.Cle)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return cle is null ? null : (await stockage.LireAsync(cle, cancellationToken).ConfigureAwait(false))?.Contenu;
    }

    private static SaisieControle SaisieDe(Guid commande, DemandeRealisation demande) => new(
        commande, demande.RealiseLe, SaisiePreventif.Facultatif<ResultatControle>(demande.Resultat, "resultat"), demande.CompteRendu, demande.GrilleVersion,
        demande.Points?.Select(p => (p?.Conforme, p?.Danger ?? false)).ToList(), demande.Mesures,
        demande.Releves?.Select(r => (r?.Libelle, r?.Unite, r?.Valeur)).ToList(), demande.Prestataire, demande.NumeroAgrement);

    // Rejeu : même auteur, même plan et échéance, même résultat et date → même contrôle ; sinon la commande est réutilisée (409).
    private async Task<ControleEnregistre?> DejaRealiseAsync(Guid auteur, PlanPreventif plan, DateOnly? echeance, SaisieControle saisie, CancellationToken cancellationToken)
    {
        var recu = await context.Controles.AsNoTracking().SingleOrDefaultAsync(c => c.CommandeId == saisie.CommandeId, cancellationToken).ConfigureAwait(false);
        if (recu is null)
        {
            return null;
        }

        if (echeance is not { } date || !recu.MemeEnvoi(auteur, plan.Id, date, saisie))
        {
            throw new CommandePreventifReutiliseeException();
        }

        var statut = await context.Occurrences.AsNoTracking().Where(o => o.Id == recu.OccurrenceId).Select(o => o.Statut).SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return Resultat(recu, statut);
    }

    private static ControleEnregistre Resultat(ControlePreventif controle, StatutOccurrence statut) =>
        new(controle.Id, statut.ToString(), controle.EvaluationId, controle.Note, controle.LibelleNote);

    // Plan du site, ses occurrences et sa cible actuelle (un composant a pu changer de zone ou de corps d'état) ; suspendu (à
    // la main, ou par le retrait de son composant ou la fin de sa vérification) : 409.
    private async Task<(PlanPreventif Plan, List<OccurrencePreventive> Occurrences, CibleDuPlan Cible)> PlanActifAsync(
        Guid siteId, Guid planId, CancellationToken cancellationToken)
    {
        var plan = await context.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId && p.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new PlanIntrouvableException();
        var cible = new CibleDuPlan(plan.Cible, plan.BatimentId, plan.ZoneId, plan.ComposantId, plan.CorpsEtat);
        if (plan.ComposantId is { } composantId)
        {
            var composant = (await inventaire.ComposantsAsync(siteId, [composantId], cancellationToken).ConfigureAwait(false)).SingleOrDefault();
            cible = composant is { Retire: false, SoumisAVerification: true }
                ? cible with { BatimentId = composant.BatimentId, ZoneId = composant.ZoneId, CorpsEtat = composant.CorpsEtat }
                : throw new PlanIncompatibleException("Le composant de ce plan est retiré ou n'est plus soumis à vérification : plan suspendu.");
        }

        if (plan.Suspendu)
        {
            throw new PlanIncompatibleException("Ce plan est suspendu : aucune échéance à traiter.");
        }

        return (plan, await context.Occurrences.Where(o => o.PlanId == planId).ToListAsync(cancellationToken).ConfigureAwait(false), cible);
    }

    // L'échéance demandée doit être l'échéance courante du plan.
    private static DateOnly Courante(PlanPreventif plan, IReadOnlyCollection<OccurrencePreventive> occurrences, DateOnly? demandee)
    {
        var echeance = demandee ?? throw new SaisieInvalideException("echeance", "Échéance obligatoire.");
        return Echeancier.Prochaine(plan.Calendrier, [.. occurrences.Select(o => o.Connue)]) == echeance ? echeance : throw new EcheanceInattendueException();
    }

    // Le technicien affecté (ou le référent du prestataire) exécute ; sinon R/D seulement (« Planifier le préventif »).
    private async Task ExigerEnChargeAsync(Guid organisationId, Guid siteId, OccurrencePreventive? occurrence, CancellationToken cancellationToken)
    {
        if (occurrence?.EnCharge(compte.Id) != true)
        {
            await autorisations.ExigerAsync(organisationId, siteId, Droit.PlanifierPreventif, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ExigerVersion(OccurrencePreventive occurrence, uint version)
    {
        var xmin = context.Entry(occurrence).Property<uint>("xmin");
        if (xmin.CurrentValue != version)
        {
            throw new PlanModifieException();
        }

        xmin.OriginalValue = version;
    }

    private void Tracer(PlanPreventif plan, NatureEvenementPlan nature, DateTimeOffset le, object detail) =>
        context.JournalPreventif.Add(new EvenementPlan(Guid.CreateVersion7(le), plan, nature, compte.Id, le, JsonSerializer.Serialize(detail)));

    private async Task EnregistrerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException conflit) when (conflit is DbUpdateConcurrencyException
            || conflit.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PlanModifieException(); // occurrence affectée, sautée ou réalisée en parallèle
        }
    }
}

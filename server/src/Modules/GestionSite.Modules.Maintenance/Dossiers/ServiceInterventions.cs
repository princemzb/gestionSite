using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Dossiers;

internal sealed record IntervenantPossible(Guid CompteId, string Nom, IReadOnlyCollection<string> Roles);

internal sealed record MonIntervention(
    Guid DossierId, int Numero, string Titre, string Statut, string Dossier, string? Priorite, string Emplacement, bool Danger,
    DateTimeOffset? EcheancePriseEnCharge, DateTimeOffset? EcheanceResolution, bool Prestataire);

/// <summary>Intervention absente du dossier (404).</summary>
internal sealed class InterventionIntrouvableException() : Exception("Intervention introuvable.");

/// <summary>
/// Affectation et démarrage des interventions (US-09, S09-02) : R/D affecte à un technicien interne ou à un prestataire
/// suivi par un référent interne (décision du 29/10) ; la personne en charge, ou R/D, déclare le démarrage.
/// </summary>
internal sealed class ServiceInterventions(
    MaintenanceDbContext context, IAutorisations autorisations, IAnnuaireMembres annuaire, IInventaireSite inventaire, IStockageFichiers stockage,
    CompteCourant compte, TimeProvider horloge)
{
    // Intervenant interne : un technicien du site (décision du 29/10) ; référent d'un prestataire : tout membre de terrain.
    private const string Technicien = "Technicien";

    /// <summary>Membres du site qui peuvent recevoir ou suivre une intervention (droit de documenter une intervention).</summary>
    internal async Task<IReadOnlyList<IntervenantPossible>> IntervenantsAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        return [.. (await annuaire.MembresAyantDroitAsync(siteId, Droit.DocumenterIntervention, compteIds: null, cancellationToken).ConfigureAwait(false))
            .Select(m => new IntervenantPossible(m.CompteId, m.Nom, m.Roles))];
    }

    internal async Task<int> AffecterAsync(Guid organisationId, Guid siteId, Guid dossierId, DemandeAffectation demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        var demandes = new[] { demande.IntervenantCompteId, demande.ReferentCompteId }.Where(c => c is not null).Select(c => c!.Value).ToList();
        var terrain = (await annuaire.MembresAyantDroitAsync(siteId, Droit.DocumenterIntervention, demandes, cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.CompteId);
        if (demande.IntervenantCompteId is { } intervenant && !(terrain.TryGetValue(intervenant, out var membre) && membre.Roles.Contains(Technicien)))
        {
            throw new SaisieInvalideException("intervenantCompteId", "Un technicien de ce site.");
        }

        if (demande.ReferentCompteId is { } referent && !terrain.ContainsKey(referent))
        {
            throw new SaisieInvalideException("referentCompteId", "Technicien, responsable ou délégué de ce site.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var dossier = await context.DossierAVersionAsync(siteId, dossierId, demande.Version, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var numero = await context.Interventions.CountAsync(i => i.DossierId == dossierId, cancellationToken).ConfigureAwait(false) + 1;
        var intervention = new Intervention(
            Guid.CreateVersion7(maintenant), dossier, numero, demande.Titre, demande.IntervenantCompteId, demande.Prestataire, demande.ReferentCompteId, maintenant);
        dossier.Affecter(maintenant);
        context.Interventions.Add(intervention);
        await SynchroniserAsync(dossier, maintenant, ajoutee: intervention, cancellationToken).ConfigureAwait(false);
        // Comptes par identifiant (noms résolus à la lecture) : le journal en ajout seul ne fige aucun nom de personne.
        context.Tracer(dossier, NatureEvenementDossier.Affectation, compte.Id, maintenant, new
        {
            numero,
            titre = intervention.Titre,
            intervenantCompteId = intervention.IntervenantCompteId,
            prestataire = intervention.Prestataire,
            referentCompteId = intervention.ReferentCompteId,
        });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return numero;
    }

    /// <summary>Démarrage par la personne en charge (droit de documenter) ou par R/D ; le dossier passe « En cours ».</summary>
    internal async Task DemarrerAsync(Guid organisationId, Guid siteId, Guid dossierId, int numero, DemandeVersion demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.DocumenterIntervention, cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (dossier, intervention) = await InterventionEnChargeAsync(organisationId, siteId, dossierId, numero, demande.Version, cancellationToken)
            .ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        intervention.Demarrer(maintenant);
        await SynchroniserAsync(dossier, maintenant, ajoutee: null, cancellationToken).ConfigureAwait(false);
        context.Tracer(dossier, NatureEvenementDossier.Demarrage, compte.Id, maintenant, new { numero, titre = intervention.Titre });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Mise en attente par la personne en charge ou R/D (DEC-03) : motif, date de réexamen ; le dossier attend quand plus
    /// aucune de ses interventions n'est en cours, et son délai de résolution est suspendu (RG-07).
    /// </summary>
    internal async Task MettreEnAttenteAsync(
        Guid organisationId, Guid siteId, Guid dossierId, int numero, DemandeAttente demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.DocumenterIntervention, cancellationToken).ConfigureAwait(false);
        var motif = Saisie.Valeur<MotifAttente>(demande.Motif, "motif");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (dossier, intervention) = await InterventionEnChargeAsync(organisationId, siteId, dossierId, numero, demande.Version, cancellationToken)
            .ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        intervention.MettreEnAttente(motif, demande.Commentaire, demande.ReexamenLe, maintenant);
        await SynchroniserAsync(dossier, maintenant, ajoutee: null, cancellationToken).ConfigureAwait(false);

        context.Tracer(dossier, NatureEvenementDossier.MiseEnAttente, compte.Id, maintenant, new
        {
            numero,
            motif = motif.ToString(),
            commentaire = intervention.CommentaireAttente,
            reexamenLe = intervention.ReexamenLe,
        });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reprise datée (DEC-03) : le temps d'attente du dossier s'ajoute à son délai de résolution.</summary>
    internal async Task ReprendreAsync(Guid organisationId, Guid siteId, Guid dossierId, int numero, DemandeVersion demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.DocumenterIntervention, cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (dossier, intervention) = await InterventionEnChargeAsync(organisationId, siteId, dossierId, numero, demande.Version, cancellationToken)
            .ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        intervention.Reprendre();
        await SynchroniserAsync(dossier, maintenant, ajoutee: null, cancellationToken).ConfigureAwait(false);

        context.Tracer(dossier, NatureEvenementDossier.Reprise, compte.Id, maintenant, new { numero, titre = intervention.Titre });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Clôture par la personne en charge ou R/D (RG-06) : actions réalisées, état final, photos (3 au plus), au moins une
    /// exigée quand le composant du dossier relève d'un corps d'état à risque (décision du 29/10). La dernière intervention
    /// ouverte clôturée clôt le dossier (DEC-03).
    /// </summary>
    internal async Task CloturerAsync(
        Guid organisationId, Guid siteId, Guid dossierId, int numero, DemandeCloture demande, IReadOnlyList<PhotoRecue> photos,
        CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.CloturerIntervention, cancellationToken).ConfigureAwait(false);
        if (photos.Count > TraitementPhotos.NombreMax)
        {
            throw new SaisieInvalideException("photos", "Trois photos au plus.");
        }

        var etatFinal = Saisie.Valeur<EtatFinal>(demande.EtatFinal, "etatFinal");
        // Photos préparées avant toute écriture : une photo refusée n'écrit rien.
        var jpegs = photos.Select(p => TraitementPhotos.Preparer(p.Contenu)).ToList();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (dossier, intervention) = await InterventionEnChargeAsync(organisationId, siteId, dossierId, numero, demande.Version, cancellationToken)
            .ConfigureAwait(false);
        var lieu = dossier.ZoneId is { } zone
            ? await inventaire.LieuAsync(siteId, zone, dossier.ComposantId, composantActif: false, cancellationToken).ConfigureAwait(false)
            : null;
        var maintenant = horloge.GetUtcNow();
        intervention.Cloturer(demande.Actions, etatFinal, jpegs.Count, PhotoExigee.Pour(lieu?.CorpsEtat), maintenant);
        var enregistrees = jpegs.Select((jpeg, i) => new PhotoIntervention(Guid.CreateVersion7(maintenant), intervention, i + 1, jpeg.Length, maintenant)).ToList();
        await Task.WhenAll(enregistrees.Select((photo, i) =>
            stockage.EcrireAsync(photo.Cle, jpegs[i], TraitementPhotos.TypeContenu, cancellationToken))).ConfigureAwait(false);
        context.PhotosIntervention.AddRange(enregistrees);
        await SynchroniserAsync(dossier, maintenant, ajoutee: null, cancellationToken).ConfigureAwait(false);
        context.Tracer(dossier, NatureEvenementDossier.ClotureIntervention, compte.Id, maintenant,
            new { numero, titre = intervention.Titre, etatFinal = etatFinal.ToString(), photos = enregistrees.Count });
        if (dossier.Statut == StatutDossier.Cloture)
        {
            context.Tracer(dossier, NatureEvenementDossier.Cloture, compte.Id, maintenant, detail: null);
        }

        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Photo après intervention (JPEG), ou null si elle n'existe pas pour cette intervention de ce dossier du site.</summary>
    internal async Task<byte[]?> PhotoAsync(Guid organisationId, Guid siteId, Guid dossierId, int numero, int rang, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var cle = await (
                from p in context.PhotosIntervention.AsNoTracking()
                join i in context.Interventions.AsNoTracking() on p.InterventionId equals i.Id
                where i.SiteId == siteId && i.DossierId == dossierId && i.Numero == numero && p.Rang == rang
                select p.Cle)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return cle is null ? null : (await stockage.LireAsync(cle, cancellationToken).ConfigureAwait(false))?.Contenu;
    }

    // Statut du dossier recalculé depuis toutes ses interventions (suivies : l'état modifié dans cette requête compte).
    private async Task SynchroniserAsync(Signalement dossier, DateTimeOffset maintenant, Intervention? ajoutee, CancellationToken cancellationToken)
    {
        var interventions = await context.Interventions.Where(i => i.DossierId == dossier.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ajoutee is not null && !interventions.Contains(ajoutee))
        {
            interventions.Add(ajoutee);
        }

        dossier.Synchroniser(interventions, maintenant);
    }

    // Dossier verrouillé à la version lue et intervention dont l'appelant est en charge, sinon R/D (RG-04, RG-06).
    private async Task<(Signalement Dossier, Intervention Intervention)> InterventionEnChargeAsync(
        Guid organisationId, Guid siteId, Guid dossierId, int numero, uint? version, CancellationToken cancellationToken)
    {
        var dossier = await context.DossierAVersionAsync(siteId, dossierId, version, cancellationToken).ConfigureAwait(false);
        var intervention = await context.Interventions.SingleOrDefaultAsync(i => i.DossierId == dossierId && i.Numero == numero, cancellationToken)
            .ConfigureAwait(false) ?? throw new InterventionIntrouvableException();
        if (intervention.EnCharge != compte.Id)
        {
            await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        }

        return (dossier, intervention);
    }

    /// <summary>
    /// Interventions non clôturées dont l'appelant est en charge sur ce site : par priorité du dossier, puis par échéance
    /// (prise en charge si l'intervention n'a pas démarré et que le dossier l'attend encore, sinon résolution).
    /// </summary>
    internal async Task<IReadOnlyList<MonIntervention>> MesInterventionsAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.DocumenterIntervention, cancellationToken).ConfigureAwait(false);
        var moi = compte.Id;
        var lignes = await (
                from i in context.Interventions.AsNoTracking()
                join d in context.Signalements.AsNoTracking() on i.DossierId equals d.Id
                where i.SiteId == siteId && i.Statut != StatutIntervention.Cloturee && (i.IntervenantCompteId == moi || i.ReferentCompteId == moi)
                select new { Intervention = i, Dossier = d })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. lignes
            .OrderBy(l => l.Dossier.Priorite ?? PrioriteDossier.Basse)
            .ThenBy(l => (l.Intervention.Statut == StatutIntervention.Affectee ? l.Dossier.EcheancePriseEnCharge : null)
                ?? l.Dossier.EcheanceResolution ?? DateTimeOffset.MaxValue)
            .Select(l => new MonIntervention(
                l.Dossier.Id, l.Intervention.Numero, l.Intervention.Titre, l.Intervention.Statut.ToString(), ServiceDossiers.Titre(l.Dossier),
                l.Dossier.Priorite?.ToString(), l.Dossier.Emplacement, l.Dossier.Danger, l.Dossier.EcheancePriseEnCharge, l.Dossier.EcheanceResolution,
                l.Intervention.Prestataire is not null))];
    }
}

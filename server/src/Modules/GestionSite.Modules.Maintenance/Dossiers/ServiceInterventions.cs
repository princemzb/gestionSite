using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
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
    MaintenanceDbContext context, IAutorisations autorisations, IAnnuaireMembres annuaire, CompteCourant compte, TimeProvider horloge)
{
    private static readonly string[] RolesTerrain = ["Technicien", "Responsable", "Delegue"];

    /// <summary>Membres du site qui peuvent recevoir ou suivre une intervention (technicien, responsable, délégué).</summary>
    internal async Task<IReadOnlyList<IntervenantPossible>> IntervenantsAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        return [.. (await annuaire.MembresDuSiteAsync(siteId, cancellationToken).ConfigureAwait(false))
            .Where(m => m.Roles.Any(RolesTerrain.Contains))
            .Select(m => new IntervenantPossible(m.CompteId, m.Nom, m.Roles))];
    }

    internal async Task<int> AffecterAsync(Guid organisationId, Guid siteId, Guid dossierId, DemandeAffectation demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        var dossier = await DossierAVersionAsync(siteId, dossierId, demande.Version, cancellationToken).ConfigureAwait(false);
        var terrain = (await annuaire.MembresDuSiteAsync(siteId, cancellationToken).ConfigureAwait(false))
            .Where(m => m.Roles.Any(RolesTerrain.Contains)).ToDictionary(m => m.CompteId, m => m.Nom);
        foreach (var (champ, id) in new[] { ("intervenantCompteId", demande.IntervenantCompteId), ("referentCompteId", demande.ReferentCompteId) })
        {
            if (id is { } compteId && !terrain.ContainsKey(compteId))
            {
                throw new SaisieInvalideException(champ, "Technicien, responsable ou délégué de ce site.");
            }
        }

        var maintenant = horloge.GetUtcNow();
        var numero = await context.Interventions.CountAsync(i => i.DossierId == dossierId, cancellationToken).ConfigureAwait(false) + 1;
        var intervention = new Intervention(
            Guid.CreateVersion7(maintenant), dossier, numero, demande.Titre, demande.IntervenantCompteId, demande.Prestataire, demande.ReferentCompteId, maintenant);
        dossier.Affecter(maintenant);
        context.Interventions.Add(intervention);
        Tracer(dossier, NatureEvenementDossier.Affectation, maintenant, new
        {
            numero,
            titre = intervention.Titre,
            intervenant = intervention.IntervenantCompteId is { } i ? terrain[i] : null,
            prestataire = intervention.Prestataire,
            referent = intervention.ReferentCompteId is { } r ? terrain[r] : null,
        });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return numero;
    }

    /// <summary>Démarrage par la personne en charge (droit de documenter) ou par R/D ; le dossier passe « En cours ».</summary>
    internal async Task DemarrerAsync(Guid organisationId, Guid siteId, Guid dossierId, int numero, DemandeVersion demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.DocumenterIntervention, cancellationToken).ConfigureAwait(false);
        var dossier = await DossierAVersionAsync(siteId, dossierId, demande.Version, cancellationToken).ConfigureAwait(false);
        var intervention = await context.Interventions.SingleOrDefaultAsync(i => i.DossierId == dossierId && i.Numero == numero, cancellationToken)
            .ConfigureAwait(false) ?? throw new InterventionIntrouvableException();
        if (intervention.EnCharge != compte.Id)
        {
            // Pas en charge : seul R/D agit à la place de l'intervenant (RG-04, RG-06).
            await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        }

        var maintenant = horloge.GetUtcNow();
        intervention.Demarrer(maintenant);
        dossier.Demarrer(maintenant);
        Tracer(dossier, NatureEvenementDossier.Demarrage, maintenant, new { numero, titre = intervention.Titre });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Interventions non clôturées dont l'appelant est en charge sur ce site, la plus urgente d'abord.</summary>
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
            .Select(l => new MonIntervention(
                l.Dossier.Id, l.Intervention.Numero, l.Intervention.Titre, l.Intervention.Statut.ToString(), ServiceDossiers.Titre(l.Dossier),
                l.Dossier.Priorite?.ToString(), l.Dossier.Emplacement, l.Dossier.Danger, Echeances.PriseEnCharge(l.Dossier), Echeances.Resolution(l.Dossier),
                l.Intervention.Prestataire is not null))
            .OrderBy(m => m.EcheancePriseEnCharge ?? m.EcheanceResolution ?? DateTimeOffset.MaxValue)];
    }

    private async Task<Signalement> DossierAVersionAsync(Guid siteId, Guid id, uint? version, CancellationToken cancellationToken)
    {
        var dossier = await context.Signalements.SingleOrDefaultAsync(s => s.Id == id && s.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new DossierIntrouvableException();
        context.Entry(dossier).Property<uint>("xmin").OriginalValue = version ?? throw new SaisieInvalideException("version", "Version du dossier lue obligatoire.");
        return dossier;
    }

    private void Tracer(Signalement dossier, NatureEvenementDossier nature, DateTimeOffset le, object detail) =>
        context.Journal.Add(new EvenementDossier(Guid.CreateVersion7(le), dossier, nature, compte.Id, le, JsonSerializer.Serialize(detail)));

    private async Task EnregistrerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException conflit) when (conflit is DbUpdateConcurrencyException
            || conflit.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Version dépassée, ou deux affectations simultanées (même numéro) : relire puis refaire.
            throw new DossierModifieException();
        }
    }
}

/// <summary>Échéances cibles du dossier (DEC-05) : prise en charge tant qu'aucune intervention n'a démarré, résolution tant qu'il est ouvert.</summary>
internal static class Echeances
{
    internal static DateTimeOffset? PriseEnCharge(Signalement dossier) =>
        dossier is { Priorite: { } priorite, AffecteLe: { } affecte, PrisEnChargeLe: null } && dossier.Ouvert ? affecte + Delais.PriseEnCharge(priorite) : null;

    // Les périodes « En attente » suspendront ce délai (S09-03).
    internal static DateTimeOffset? Resolution(Signalement dossier) =>
        dossier is { Priorite: { } priorite, QualifieLe: { } qualifie } && dossier.Ouvert ? qualifie + Delais.Resolution(priorite) : null;
}

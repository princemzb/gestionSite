using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.DepotPublic;

/// <summary>Envoi du formulaire public ; la commande (identifiant choisi par le formulaire) rend le rejeu sans effet.</summary>
internal sealed record DemandeDepot(
    Guid CommandeId, string? Nature, string? Description, string? Emplacement, bool Danger, bool ActiviteBloquee, string? Nom, string? Contact);

/// <summary>
/// Accusé sans aucun moyen de lire le signalement (F-01) ; en cas de danger, le numéro du site à appeler s'il est
/// renseigné (décision du 28/10).
/// </summary>
internal sealed record AccuseDepot(string Site, bool Danger, string? NumeroUrgence);

/// <summary>Dépôt public sans compte (US-08, RG-02) : site atteint par son code, signalement créé une seule fois.</summary>
internal sealed class ServiceDepotPublic(MaintenanceDbContext context, ISitesPublics sites, TimeProvider horloge)
{
    internal Task<SitePublic?> SiteAsync(string code, CancellationToken cancellationToken) => sites.OuvrirAsync(code, cancellationToken);

    /// <summary>L'accusé, ou null si le code de site est inconnu (404).</summary>
    internal async Task<AccuseDepot?> DeposerAsync(string code, DemandeDepot demande, CancellationToken cancellationToken)
    {
        // Avant toute transaction : la recherche fixe l'organisation courante (filtres et RLS).
        if (await sites.OuvrirAsync(code, cancellationToken).ConfigureAwait(false) is not { } site)
        {
            return null;
        }

        var dejaRecu = await context.Signalements.AsNoTracking().Where(s => s.CommandeId == demande.CommandeId)
            .Select(s => (bool?)s.Danger).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (dejaRecu is { } danger)
        {
            return Accuse(site, danger);
        }

        var maintenant = horloge.GetUtcNow();
        var signalement = new Signalement(
            Guid.CreateVersion7(maintenant), site.OrganisationId, site.SiteId, demande.CommandeId, Nature(demande.Nature), demande.Description,
            demande.Emplacement, demande.Danger, demande.ActiviteBloquee, demande.Nom, demande.Contact, maintenant);
        context.Signalements.Add(signalement);
        if (signalement.Danger)
        {
            context.AlertesDanger.Add(new AlerteDanger(Guid.CreateVersion7(maintenant), signalement));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Même envoi reçu en parallèle : l'autre requête l'a enregistré, celle-ci rend le même accusé.
            return Accuse(site, signalement.Danger);
        }

        return Accuse(site, signalement.Danger);
    }

    private static AccuseDepot Accuse(SitePublic site, bool danger) => new(site.NomSite, danger, danger ? site.NumeroUrgence : null);

    private static NatureSignalement Nature(string? nature) =>
        Enum.TryParse<NatureSignalement>(nature, ignoreCase: true, out var valeur) && Enum.IsDefined(valeur) && !int.TryParse(nature, out _)
            ? valeur
            : throw new ArgumentException("Nature attendue : Incident ou Demande.", nameof(nature));
}

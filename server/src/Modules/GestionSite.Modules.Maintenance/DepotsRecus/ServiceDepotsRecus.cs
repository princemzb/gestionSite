using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.DepotsRecus;

/// <summary>Dépôt reçu, tel que lu par les comptes internes du site ; contact déclaratif, non vérifié.</summary>
internal sealed record DepotRecu(
    Guid Id, DateTimeOffset RecuLe, string Nature, bool Danger, bool ActiviteBloquee, string Emplacement, string Description,
    string NomDeclarant, string? ContactDeclarant, IReadOnlyList<int> Photos);

/// <summary>
/// Liste en lecture des dépôts reçus par site (US-08, S08-05) : comptes internes habilités sur le site (T, R/D, P/C ;
/// DEC-04 pour le contact du déclarant). Le traitement (qualification, affectation) relève d'US-09.
/// </summary>
internal sealed class ServiceDepotsRecus(MaintenanceDbContext context, IAutorisations autorisations, IStockageFichiers stockage)
{
    internal const int LimiteMax = 100;

    /// <summary>Du plus récent au plus ancien ; <paramref name="avant"/> : identifiant du dernier dépôt déjà reçu (identifiants ordonnés dans le temps).</summary>
    internal async Task<IReadOnlyList<DepotRecu>> ListerAsync(
        Guid organisationId, Guid siteId, Guid? avant, int limite, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        await autorisations.ExigerAsync(organisationId, siteId, Droit.LireContactsDeclarant, cancellationToken).ConfigureAwait(false);
        // Dépôts publics seulement : les déclarations des comptes internes se suivent dans les dossiers (US-09).
        var signalements = context.Signalements.AsNoTracking().Where(s => s.SiteId == siteId && s.Origine == OrigineDossier.Public);
        if (avant is { } curseur)
        {
            signalements = signalements.Where(s => s.Id.CompareTo(curseur) < 0);
        }

        var page = await signalements.OrderByDescending(s => s.Id).Take(Math.Clamp(limite, 1, LimiteMax))
            .Select(s => new
            {
                s.Id,
                s.RecuLe,
                s.Nature,
                s.Danger,
                s.ActiviteBloquee,
                s.Emplacement,
                s.Description,
                s.NomDeclarant,
                s.ContactDeclarant,
                Photos = context.Photos.Where(p => p.SignalementId == s.Id).OrderBy(p => p.Rang).Select(p => p.Rang).ToList(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. page.Select(s => new DepotRecu(
            s.Id, s.RecuLe, s.Nature.ToString(), s.Danger, s.ActiviteBloquee, s.Emplacement, s.Description, s.NomDeclarant, s.ContactDeclarant, s.Photos))];
    }

    /// <summary>Photo réencodée (JPEG), ou null si elle n'existe pas pour ce dépôt de ce site.</summary>
    internal async Task<byte[]?> PhotoAsync(Guid organisationId, Guid siteId, Guid depotId, int rang, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var cle = await context.Photos.AsNoTracking()
            .Where(p => p.SignalementId == depotId && p.Rang == rang
                && context.Signalements.Any(s => s.Id == depotId && s.SiteId == siteId))
            .Select(p => p.Cle).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return cle is null ? null : (await stockage.LireAsync(cle, cancellationToken).ConfigureAwait(false))?.Contenu;
    }
}

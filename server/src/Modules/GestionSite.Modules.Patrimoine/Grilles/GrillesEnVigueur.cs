using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Grilles;

/// <summary>
/// Grille en vigueur d'un site (US-11) : la dernière version adaptée, sinon la grille de l'application (version 1).
/// Partagée par le service des grilles et la lecture de l'inventaire par les autres modules (US-12).
/// </summary>
internal static class GrillesEnVigueur
{
    /// <summary>
    /// Grille en vigueur sur un site pour un corps d'état. L'appelant a vérifié l'accès au site, ce qui fixe
    /// l'organisation courante ; sans elle, l'appel échoue au lieu de rendre la grille par défaut.
    /// </summary>
    internal static async Task<GrilleCourante> CouranteAsync(PatrimoineDbContext context, Guid siteId, CorpsEtat corps, CancellationToken cancellationToken) =>
        Courante(corps, await AdapteeAsync(context, siteId, corps, cancellationToken).ConfigureAwait(false));

    // Dernière version adaptée sur le site pour un corps d'état, ou null (grille par défaut) ; organisation exigée.
    internal static async Task<VersionGrille?> AdapteeAsync(PatrimoineDbContext context, Guid siteId, CorpsEtat corps, CancellationToken cancellationToken)
    {
        _ = context.OrganisationRequise;
        return await DernieresVersions(context, siteId).FirstOrDefaultAsync(g => g.CorpsEtat == corps, cancellationToken).ConfigureAwait(false);
    }

    // Dernière version de chaque corps d'état adapté sur le site, calculée en base (index ix_grilles_version).
    internal static IQueryable<VersionGrille> DernieresVersions(PatrimoineDbContext context, Guid siteId) =>
        context.Grilles.AsNoTracking().Where(g => g.SiteId == siteId
            && !context.Grilles.Any(suivante => suivante.SiteId == siteId && suivante.CorpsEtat == g.CorpsEtat && suivante.Version > g.Version));

    internal static GrilleCourante Courante(CorpsEtat corps, VersionGrille? adaptee) =>
        adaptee is null ? new GrilleCourante(corps, 1, GrillesParDefaut.Pour(corps)) : new GrilleCourante(corps, adaptee.Version, adaptee.Contenu);
}

using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.SitesPublics;

internal sealed class RechercheSitesPublics(IdentiteDbContext context, OrganisationCourante organisationCourante) : ISitesPublics
{
    public async Task<SitePublic?> OuvrirAsync(string code, CancellationToken cancellationToken)
    {
        if (CodeSitePublic.Normaliser(code) is not { } normalise)
        {
            return null;
        }

        var entree = await context.CodesSitesPublics.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == normalise, cancellationToken).ConfigureAwait(false);
        if (entree is null)
        {
            return null;
        }

        // Le code est la seule clé : la suite de la requête (lectures et dépôt) se fait dans l'organisation du site.
        organisationCourante.Definir(entree.OrganisationId);
        var site = await context.Sites.AsNoTracking().Where(s => s.Id == entree.SiteId)
            .Select(s => new { s.Nom, s.NumeroUrgence }).SingleAsync(cancellationToken).ConfigureAwait(false);
        var organisation = await context.Organisations.AsNoTracking().Select(o => o.Nom)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        return new SitePublic(entree.OrganisationId, entree.SiteId, site.Nom, organisation, site.NumeroUrgence);
    }
}

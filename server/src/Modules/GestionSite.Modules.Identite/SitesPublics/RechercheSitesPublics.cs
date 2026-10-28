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

        var entree = await context.CodesSitesPublics.AsNoTracking().IgnoreQueryFilters() // seule lecture hors organisation
            .SingleOrDefaultAsync(c => c.Code == normalise, cancellationToken).ConfigureAwait(false);
        if (entree is null)
        {
            return null;
        }

        // Le code est la seule clé : la suite de la requête (lectures et dépôt) se fait dans l'organisation du site.
        organisationCourante.Definir(entree.OrganisationId);
        var site = await (
                from s in context.Sites.AsNoTracking()
                join o in context.Organisations.AsNoTracking() on s.OrganisationId equals o.Id
                where s.Id == entree.SiteId
                select new SitePublic(s.OrganisationId, s.Id, s.Nom, o.Nom, s.NumeroUrgence))
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        return site;
    }
}

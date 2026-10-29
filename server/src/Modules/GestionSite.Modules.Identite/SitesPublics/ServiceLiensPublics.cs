using GestionSite.Modules.Identite.Autorisation;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Membres;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.SitesPublics;

internal sealed record LienPublic(Guid SiteId, string Nom, string Code, string? NumeroUrgence);

/// <summary>
/// Lien public, QR code et numéro d'urgence des sites (US-08, S08-04) : affichés par l'administrateur pour tous les
/// sites et par responsable et délégués pour les leurs ; numéro d'urgence modifié par l'administrateur seul.
/// </summary>
internal sealed class ServiceLiensPublics(IdentiteDbContext context, AccesOrganisation acces, Autorisations autorisations)
{
    internal async Task<IReadOnlyList<LienPublic>> ListerAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        await acces.EntrerAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var administrateur = await autorisations.VerifierAsync(organisationId, Droit.GererComptes, siteId: null, cancellationToken)
            .ConfigureAwait(false) == DecisionAutorisation.Autorise;
        var autorises = administrateur
            ? null
            : await autorisations.SitesAutorisesAsync(organisationId, Droit.AfficherLienPublic, cancellationToken).ConfigureAwait(false);
        if (autorises is { Count: 0 })
        {
            throw RefusException.Interdit();
        }

        var sites = context.Sites.AsNoTracking();
        if (autorises is not null)
        {
            var identifiants = autorises.ToList();
            sites = sites.Where(s => identifiants.Contains(s.Id));
        }

        return await (from s in sites
                      join c in context.CodesSitesPublics.AsNoTracking() on s.Id equals c.SiteId
                      orderby s.Nom
                      select new LienPublic(s.Id, s.Nom, c.Code, s.NumeroUrgence))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task DefinirNumeroUrgenceAsync(Guid organisationId, Guid siteId, string? numero, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var site = await context.Sites.SingleOrDefaultAsync(s => s.Id == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw RefusException.Introuvable();
        site.DefinirNumeroUrgence(numero);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Autorisation;

/// <summary>
/// Décision d'autorisation sur l'adhésion acceptée de l'appelant (lue par la politique RLS « par compte »),
/// puis organisation courante définie pour la suite de la requête. Une adhésion lue par portée.
/// </summary>
internal sealed class Autorisations(IdentiteDbContext context, CompteCourant compte, OrganisationCourante organisation) : IAutorisations
{
    private Membre? _appelant;

    public async Task<DecisionAutorisation> VerifierAsync(
        Guid organisationId, Droit droit, Guid? siteId, CancellationToken cancellationToken)
    {
        var appelant = await AppelantAsync(organisationId, cancellationToken).ConfigureAwait(false);
        if (appelant is null)
        {
            return DecisionAutorisation.Introuvable;
        }

        var autorises = MatriceDroits.RolesPour(droit);
        if (MatriceDroits.EstOrganisationnelle(droit))
        {
            return appelant.Roles.Any(r => autorises.Contains(r.Role)) ? DecisionAutorisation.Autorise : DecisionAutorisation.Refuse;
        }

        if (siteId is not { } site)
        {
            throw new ArgumentException($"Le droit {droit} porte sur un site.", nameof(siteId));
        }

        // Site sans aucun rôle de l'appelant (ou inexistant) : introuvable. L'administrateur voit les sites
        // de son organisation (il les gère) mais n'y a pas de droit métier sans rôle.
        var visible = appelant.EstAdministrateur ? await SiteExisteAsync(site, cancellationToken).ConfigureAwait(false) : RolesSur(appelant, site).Any();
        if (!visible)
        {
            return DecisionAutorisation.Introuvable;
        }

        return RolesSur(appelant, site).Any(autorises.Contains) ? DecisionAutorisation.Autorise : DecisionAutorisation.Refuse;
    }

    public async Task<IReadOnlySet<Guid>> SitesAutorisesAsync(Guid organisationId, Droit droit, CancellationToken cancellationToken)
    {
        if (MatriceDroits.EstOrganisationnelle(droit))
        {
            throw new ArgumentException($"Le droit {droit} porte sur l'organisation.", nameof(droit));
        }

        var appelant = await AppelantAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var autorises = MatriceDroits.RolesPour(droit);
        return appelant is null
            ? new HashSet<Guid>()
            : appelant.Roles.Where(r => r.SiteId is not null && autorises.Contains(r.Role)).Select(r => r.SiteId!.Value).ToHashSet();
    }

    /// <summary>Adhésion acceptée et active de l'appelant, ou null ; l'organisation devient courante si trouvée.</summary>
    internal async Task<Membre?> AppelantAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        if (_appelant is not null)
        {
            return _appelant.OrganisationId == organisationId ? _appelant : null;
        }

        if (compte.Id is not { } compteId)
        {
            return null;
        }

        _appelant = await context.Membres.IgnoreQueryFilters().Include(m => m.Roles)
            .SingleOrDefaultAsync(
                m => m.OrganisationId == organisationId && m.CompteId == compteId && m.RevoqueLe == null && m.AccepteLe != null, cancellationToken)
            .ConfigureAwait(false);
        if (_appelant is not null)
        {
            organisation.Definir(organisationId);
        }

        return _appelant;
    }

    private static IEnumerable<Role> RolesSur(Membre membre, Guid site) =>
        membre.Roles.Where(r => r.SiteId == site).Select(r => r.Role);

    private Task<bool> SiteExisteAsync(Guid site, CancellationToken cancellationToken) =>
        context.Sites.AnyAsync(s => s.Id == site, cancellationToken); // filtre d'organisation : la sienne seulement
}

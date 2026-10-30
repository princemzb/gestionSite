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
        // Arguments contrôlés avant toute lecture : une erreur d'usage échoue de la même façon pour tous.
        var autorises = MatriceDroits.RolesPour(droit);
        var organisationnel = MatriceDroits.EstOrganisationnelle(droit);
        if (organisationnel != (siteId is null))
        {
            throw new ArgumentException(organisationnel ? $"Le droit {droit} porte sur l'organisation." : $"Le droit {droit} porte sur un site.", nameof(siteId));
        }

        var appelant = await AppelantAsync(organisationId, cancellationToken).ConfigureAwait(false);
        if (appelant is null)
        {
            return DecisionAutorisation.Introuvable;
        }

        if (organisationnel)
        {
            return appelant.Roles.Any(r => autorises.Contains(r.Role)) ? DecisionAutorisation.Autorise : DecisionAutorisation.Refuse;
        }

        var site = siteId!.Value;
        var rolesSurSite = RolesSur(appelant, site).ToList();
        if (rolesSurSite.Count == 0 && !await EstVisibleAsync(appelant, site, cancellationToken).ConfigureAwait(false))
        {
            return DecisionAutorisation.Introuvable;
        }

        if (rolesSurSite.Exists(autorises.Contains))
        {
            return DecisionAutorisation.Autorise;
        }

        // Union des rôles et des droits nominatifs (US-21) : un droit de la liste blanche accordé nommément sur ce site,
        // jamais sans rôle sur le site. Relu à chaque décision : un retrait prend effet aussitôt.
        return rolesSurSite.Count > 0 && MatriceDroits.EstNominatif(droit)
            && await context.DroitsNominatifs.AsNoTracking()
                .AnyAsync(d => d.MembreId == appelant.Id && d.SiteId == site && d.Droit == droit, cancellationToken).ConfigureAwait(false)
            ? DecisionAutorisation.Autorise
            : DecisionAutorisation.Refuse;
    }

    public async Task<IReadOnlySet<Guid>> SitesAutorisesAsync(Guid organisationId, Droit droit, CancellationToken cancellationToken)
    {
        if (MatriceDroits.EstOrganisationnelle(droit))
        {
            throw new ArgumentException($"Le droit {droit} porte sur l'organisation.", nameof(droit));
        }

        var appelant = await AppelantAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var autorises = MatriceDroits.RolesPour(droit);
        if (appelant is null)
        {
            return new HashSet<Guid>();
        }

        var sites = appelant.Roles.Where(r => r.SiteId is not null && autorises.Contains(r.Role)).Select(r => r.SiteId!.Value).ToHashSet();
        if (MatriceDroits.EstNominatif(droit))
        {
            // Droit nominatif : seulement sur un site où l'appelant a un rôle.
            var internes = appelant.Roles.Where(r => r.SiteId is not null).Select(r => r.SiteId!.Value).ToList();
            sites.UnionWith(await context.DroitsNominatifs.AsNoTracking()
                .Where(d => d.MembreId == appelant.Id && d.Droit == droit && internes.Contains(d.SiteId)).Select(d => d.SiteId)
                .ToListAsync(cancellationToken).ConfigureAwait(false));
        }

        return sites;
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

        // Lecture sans suivi : instantané de l'appelant, indépendant des modifications en cours dans la portée.
        _appelant = await context.Membres.IgnoreQueryFilters().AsNoTracking().Include(m => m.Roles)
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

    /// <summary>
    /// Site visible : un rôle de l'appelant y est accordé, ou l'appelant administre l'organisation (il gère
    /// ses sites, sans droit métier pour autant). Sinon introuvable, qu'il existe ou non.
    /// </summary>
    internal Task<bool> EstVisibleAsync(Membre appelant, Guid site, CancellationToken cancellationToken) =>
        RolesSur(appelant, site).Any()
            ? Task.FromResult(true)
            : appelant.EstAdministrateur
                ? context.Sites.AnyAsync(s => s.Id == site, cancellationToken) // filtre d'organisation : la sienne seulement
                : Task.FromResult(false);
}

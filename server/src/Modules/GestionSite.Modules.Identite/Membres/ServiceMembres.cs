using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Membres;

internal sealed record RoleDemande(string Role, Guid? SiteId);

internal sealed record ActivationEmise(string Jeton, DateTimeOffset ExpireLe);

internal sealed record MembreCree(Guid MembreId, Guid CompteId, int Version, ActivationEmise? Activation);

internal sealed record MembreRevise(Guid MembreId, int Version);

/// <summary>Gestion des membres et des sites par l'administrateur d'organisation (CONTRATS, S06-04).</summary>
internal sealed class ServiceMembres(
    IdentiteDbContext context, UserManager<Compte> comptes, AccesOrganisation acces, TimeProvider horloge)
{
    internal async Task<MembreCree> CreerAsync(
        Guid organisationId, string nom, string email, IReadOnlyCollection<RoleDemande> roles, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var habilitations = await ResoudreAsync(roles, cancellationToken).ConfigureAwait(false);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var compte = await TrouverOuCreerCompteAsync(email, nom).ConfigureAwait(false);
        if (await context.Membres.AnyAsync(m => m.CompteId == compte.Id && m.RevoqueLe == null, cancellationToken).ConfigureAwait(false))
        {
            throw RefusException.Conflit("deja_membre", "Ce compte est déjà membre de l'organisation.");
        }

        var membre = new Membre(Guid.NewGuid(), organisationId, compte.Id, horloge.GetUtcNow());
        membre.Reviser(habilitations);
        context.Membres.Add(membre);
        var activation = compte.PasswordHash is null ? EmettreActivation(compte.Id) : null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MembreCree(membre.Id, compte.Id, membre.Version, activation);
    }

    internal async Task<MembreRevise> ReviserAsync(
        Guid organisationId, Guid membreId, IReadOnlyCollection<RoleDemande> roles, int versionAttendue, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var membre = await ChargerAsync(membreId, cancellationToken).ConfigureAwait(false);
        if (membre.Version != versionAttendue)
        {
            throw VersionDepassee();
        }

        membre.Reviser(await ResoudreAsync(roles, cancellationToken).ConfigureAwait(false));
        await ExigerUnAdministrateurAsync(membre, cancellationToken).ConfigureAwait(false);
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return new MembreRevise(membre.Id, membre.Version);
    }

    /// <summary>Accès révoqué, historique conservé, sessions du compte invalidées (jetons de renouvellement).</summary>
    internal async Task RevoquerAsync(Guid organisationId, Guid membreId, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var membre = await ChargerAsync(membreId, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        membre.Revoquer(maintenant);
        await ExigerUnAdministrateurAsync(membre, cancellationToken).ConfigureAwait(false);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await context.JetonsRenouvellement.Where(j => j.CompteId == membre.CompteId && j.RevoqueLe == null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.RevoqueLe, maintenant), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<Site>> ListerSitesAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        var appelant = await acces.EntrerAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var sites = await context.Sites.OrderBy(s => s.Nom).ToListAsync(cancellationToken).ConfigureAwait(false);
        return appelant.EstAdministrateur ? sites : [.. sites.Where(s => appelant.Roles.Any(r => r.SiteId == s.Id))];
    }

    internal async Task<Site> CreerSiteAsync(Guid organisationId, string nom, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var site = new Site(Guid.NewGuid(), organisationId, nom, horloge.GetUtcNow());
        context.Sites.Add(site);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return site;
    }

    /// <summary>Nouveau lien : les liens précédents encore valides de ce compte sont annulés.</summary>
    internal ActivationEmise EmettreActivation(Guid compteId)
    {
        var maintenant = horloge.GetUtcNow();
        foreach (var ancienne in context.Activations.Where(a => a.CompteId == compteId && a.UtiliseLe == null).ToList())
        {
            context.Activations.Remove(ancienne);
        }

        var (secret, empreinte) = SecretsAleatoires.Nouveau();
        var activation = new Activation(Guid.NewGuid(), compteId, empreinte, maintenant);
        context.Activations.Add(activation);
        return new ActivationEmise(secret, activation.ExpireLe);
    }

    internal async Task<Compte> TrouverOuCreerCompteAsync(string email, string nom)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw RefusException.Invalide("email_invalide", "Adresse e-mail obligatoire.");
        }

        var existant = await comptes.FindByEmailAsync(email).ConfigureAwait(false);
        if (existant is not null)
        {
            return existant;
        }

        var compte = Compte.Nouveau(email.Trim(), nom);
        var creation = await comptes.CreateAsync(compte).ConfigureAwait(false);
        return creation.Succeeded
            ? compte
            : throw RefusException.Invalide("email_invalide", "Adresse e-mail invalide ou déjà utilisée.");
    }

    private async Task<IReadOnlyCollection<(Role Role, Site? Site)>> ResoudreAsync(
        IReadOnlyCollection<RoleDemande> roles, CancellationToken cancellationToken)
    {
        if (roles is null || roles.Count == 0)
        {
            throw RefusException.Invalide("roles_requis", "Au moins un rôle est requis.");
        }

        var siteIds = roles.Where(r => r.SiteId is not null).Select(r => r.SiteId!.Value).Distinct().ToList();
        // Filtre d'organisation : un site d'une autre organisation est simplement inconnu.
        var sites = await context.Sites.Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken).ConfigureAwait(false);
        return [.. roles.Select(r =>
        {
            if (!Enum.TryParse<Role>(r.Role, ignoreCase: false, out var role) || !Enum.IsDefined(role))
            {
                throw RefusException.Invalide("role_inconnu", $"Rôle inconnu : {r.Role}.");
            }

            if ((role == Role.Administrateur) != (r.SiteId is null))
            {
                throw RefusException.Invalide("site_selon_role", "L'administrateur couvre toute l'organisation ; les autres rôles exigent un site.");
            }

            if (r.SiteId is { } id && !sites.ContainsKey(id))
            {
                throw RefusException.Invalide("site_inconnu", "Site inconnu dans cette organisation.");
            }

            return (role, r.SiteId is { } siteId ? sites[siteId] : null);
        })];
    }

    private async Task<Membre> ChargerAsync(Guid membreId, CancellationToken cancellationToken) =>
        await context.Membres.Include(m => m.Roles).SingleOrDefaultAsync(m => m.Id == membreId && m.RevoqueLe == null, cancellationToken)
            .ConfigureAwait(false) ?? throw RefusException.Introuvable();

    // L'organisation garde toujours au moins un administrateur actif : le membre modifié est jugé sur son
    // état en mémoire (non enregistré), les autres sur la base.
    private async Task ExigerUnAdministrateurAsync(Membre modifie, CancellationToken cancellationToken)
    {
        var autre = await context.Membres.AnyAsync(
            m => m.Id != modifie.Id && m.RevoqueLe == null && m.Roles.Any(r => r.Role == Role.Administrateur), cancellationToken).ConfigureAwait(false);
        if (!autre && !modifie.EstAdministrateur)
        {
            throw RefusException.Conflit("dernier_administrateur", "L'organisation doit garder au moins un administrateur.");
        }
    }

    private async Task EnregistrerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw VersionDepassee();
        }
    }

    private static RefusException VersionDepassee() =>
        RefusException.Conflit("version_depassee", "Le membre a été modifié entre-temps. Recharger puis réessayer.");
}

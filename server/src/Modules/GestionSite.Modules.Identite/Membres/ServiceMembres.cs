using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Identite.Membres;

internal sealed record RoleDemande(string Role, Guid? SiteId);

internal sealed record ActivationEmise(string Jeton, DateTimeOffset ExpireLe);

internal sealed record MembreCree(Guid MembreId, Guid CompteId, int Version, ActivationEmise? Activation);

internal sealed record MembreRevise(Guid MembreId, int Version);

/// <summary>Membre vu par l'administrateur : invitation en attente tant qu'elle n'est pas acceptée.</summary>
internal sealed record MembreListe(
    Guid MembreId, Guid CompteId, string Nom, string? Email, bool InvitationEnAttente, int Version, IReadOnlyList<RoleDemande> Roles);

/// <summary>Gestion des membres et des sites par l'administrateur d'organisation (CONTRATS, S06-04).</summary>
internal sealed class ServiceMembres(
    IdentiteDbContext context, UserManager<Compte> comptes, AccesOrganisation acces, CompteCourant auteur, TimeProvider horloge)
{
    internal async Task<MembreCree> CreerAsync(
        Guid organisationId, string nom, string email, IReadOnlyCollection<RoleDemande> roles, string? motif, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var habilitations = await ResoudreAsync(roles, cancellationToken).ConfigureAwait(false);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (compte, cree) = await TrouverOuCreerCompteAsync(email, nom).ConfigureAwait(false);
        if (!cree && compte.PasswordHash is null)
        {
            // Compte créé par une autre organisation et pas encore activé : lui donner un lien permettrait
            // de choisir le mot de passe d'un compte d'autrui. Il sera ajouté une fois activé.
            throw RefusException.Conflit("compte_en_attente", "Ce compte n'est pas encore activé. Réessayer après son activation.");
        }

        if (await context.Membres.AnyAsync(m => m.CompteId == compte.Id && m.RevoqueLe == null, cancellationToken).ConfigureAwait(false))
        {
            throw DejaMembre();
        }

        var membre = new Membre(Guid.NewGuid(), organisationId, compte.Id, horloge.GetUtcNow());
        membre.Reviser(habilitations);
        context.Membres.Add(membre);
        Journaliser(membre, NatureHabilitation.Creation, motif, avant: null, membre.CreeLe);
        var activation = cree ? await EmettreActivationAsync(compte.Id, organisationId, cancellationToken).ConfigureAwait(false) : null;
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException erreur) when (erreur.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw DejaMembre(); // création simultanée du même membre
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MembreCree(membre.Id, compte.Id, membre.Version, activation);
    }

    internal async Task<MembreRevise> ReviserAsync(
        Guid organisationId, Guid membreId, IReadOnlyCollection<RoleDemande> roles, int versionAttendue, string? motif,
        CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var habilitations = await ResoudreAsync(roles, cancellationToken).ConfigureAwait(false);
        await using var transaction = await VerrouillerOrganisationAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var membre = await ChargerAsync(membreId, cancellationToken).ConfigureAwait(false);
        if (membre.Version != versionAttendue)
        {
            throw VersionDepassee();
        }

        var avant = EvenementHabilitation.Instantane(membre);
        membre.Reviser(habilitations);
        if (membre.Version == versionAttendue)
        {
            return new MembreRevise(membre.Id, membre.Version); // rien n'a changé : ni écriture ni entrée de journal
        }

        await ExigerUnAdministrateurAsync(membre, cancellationToken).ConfigureAwait(false);
        Journaliser(membre, NatureHabilitation.Revision, motif, avant, horloge.GetUtcNow());
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MembreRevise(membre.Id, membre.Version);
    }

    /// <summary>
    /// Accès révoqué, historique conservé. L'accès à l'organisation cesse aussitôt, jetons en cours compris :
    /// l'adhésion est vérifiée à chaque requête. Les sessions du compte ne sont pas coupées : elles servent
    /// peut-être d'autres organisations, que la RLS rend invisibles d'ici (et qu'un administrateur tiers ne
    /// doit pas pouvoir interrompre).
    /// </summary>
    internal async Task RevoquerAsync(Guid organisationId, Guid membreId, string? motif, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        await using var transaction = await VerrouillerOrganisationAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var membre = await ChargerAsync(membreId, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        membre.Revoquer(maintenant);
        await ExigerUnAdministrateurAsync(membre, cancellationToken).ConfigureAwait(false);
        Journaliser(membre, NatureHabilitation.Revocation, motif, EvenementHabilitation.Instantane(membre), maintenant);
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Nouveau lien pour un membre jamais activé, seulement si cette organisation a créé son compte.</summary>
    internal async Task<ActivationEmise> RenouvelerActivationAsync(Guid organisationId, Guid membreId, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var membre = await ChargerAsync(membreId, cancellationToken).ConfigureAwait(false);
        var compte = await comptes.FindByIdAsync(membre.CompteId.ToString()).ConfigureAwait(false);
        var creePar = await context.Activations.Where(a => a.CompteId == membre.CompteId)
            .OrderByDescending(a => a.CreeLe).Select(a => (Guid?)a.OrganisationId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (compte?.PasswordHash is not null || creePar != organisationId)
        {
            throw RefusException.Conflit("activation_impossible", "Ce compte est déjà activé ou n'a pas été créé par cette organisation.");
        }

        var activation = await EmettreActivationAsync(compte!.Id, organisationId, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return activation;
    }

    /// <summary>Membres non révoqués de l'organisation (GET /members, administrateur), triés par nom.</summary>
    internal async Task<IReadOnlyList<MembreListe>> ListerAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var membres = await context.Membres.AsNoTracking().Include(m => m.Roles).Where(m => m.RevoqueLe == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = membres.Select(m => m.CompteId).ToList();
        var comptesListes = await context.Users.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        return [.. membres
            .Select(m => (Membre: m, Compte: comptesListes.GetValueOrDefault(m.CompteId)))
            .Select(x => new MembreListe(
                x.Membre.Id, x.Membre.CompteId, x.Compte?.NomAffiche ?? string.Empty, x.Compte?.Email, x.Membre.AccepteLe is null,
                x.Membre.Version, [.. x.Membre.Roles.OrderBy(r => r.Role).Select(r => new RoleDemande(r.Role.ToString(), r.SiteId))]))
            .OrderBy(m => m.Nom, StringComparer.CurrentCulture)];
    }

    internal async Task<IReadOnlyList<Site>> ListerSitesAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        var appelant = await acces.EntrerAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var sites = await context.Sites.OrderBy(s => s.Nom).ToListAsync(cancellationToken).ConfigureAwait(false);
        // Même règle de visibilité que les décisions d'autorisation (Autorisations.EstVisibleAsync).
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

    /// <summary>
    /// Journal des habilitations (RG-31), du plus récent au plus ancien, par pages : `avant` = identifiant de la
    /// dernière entrée reçue (identifiants ordonnés dans le temps).
    /// </summary>
    internal async Task<IReadOnlyList<EvenementHabilitation>> JournalAsync(
        Guid organisationId, int limite, Guid? avant, CancellationToken cancellationToken)
    {
        await acces.EntrerCommeAdministrateurAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var entrees = context.JournalHabilitations.AsNoTracking();
        if (avant is { } curseur)
        {
            entrees = entrees.Where(e => e.Id.CompareTo(curseur) < 0);
        }

        return await entrees.OrderByDescending(e => e.Id).Take(Math.Clamp(limite, 1, 200))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Entrée ajoutée au contexte, enregistrée dans la même transaction que le changement.</summary>
    private void Journaliser(Membre membre, NatureHabilitation nature, string? motif, IReadOnlyCollection<RoleJournalise>? avant, DateTimeOffset le) =>
        context.JournalHabilitations.Add(EvenementHabilitation.Pour(membre, nature, auteur.Id, le, motif, avant));

    /// <summary>Nouveau lien : les liens précédents non utilisés de ce compte sont annulés.</summary>
    internal async Task<ActivationEmise> EmettreActivationAsync(Guid compteId, Guid organisationId, CancellationToken cancellationToken)
    {
        var maintenant = horloge.GetUtcNow();
        await context.Activations.Where(a => a.CompteId == compteId && a.UtiliseLe == null)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var (secret, empreinte) = SecretsAleatoires.Nouveau();
        var activation = new Activation(Guid.NewGuid(), compteId, organisationId, empreinte, maintenant);
        context.Activations.Add(activation);
        return new ActivationEmise(secret, activation.ExpireLe);
    }

    /// <summary>Compte existant (Cree = false) ou créé sans mot de passe par cet appel (Cree = true).</summary>
    internal async Task<(Compte Compte, bool Cree)> TrouverOuCreerCompteAsync(string email, string nom)
    {
        var adresse = email?.Trim();
        if (string.IsNullOrEmpty(adresse))
        {
            throw RefusException.Invalide("email_invalide", "Adresse e-mail obligatoire.");
        }

        var existant = await comptes.FindByEmailAsync(adresse).ConfigureAwait(false);
        if (existant is not null)
        {
            return (existant, false);
        }

        var compte = Compte.Nouveau(adresse, nom);
        var creation = await comptes.CreateAsync(compte).ConfigureAwait(false);
        return creation.Succeeded
            ? (compte, true)
            : throw RefusException.Invalide("email_invalide", "Adresse e-mail invalide.");
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
            // Nom exact seulement : Enum.TryParse accepterait « 0 » ou « Responsable, Delegue ».
            if (r.Role is null || !Enum.GetNames<Role>().Contains(r.Role, StringComparer.Ordinal) || !Enum.TryParse<Role>(r.Role, out var role))
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

    // Verrou sur la ligne de l'organisation pour la durée de la transaction : deux révisions simultanées
    // d'administrateurs différents ne peuvent pas laisser l'organisation sans administrateur.
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> VerrouillerOrganisationAsync(
        Guid organisationId, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM identite.organisations WHERE id = {organisationId} FOR UPDATE", cancellationToken).ConfigureAwait(false);
        return transaction;
    }

    private static RefusException DejaMembre() =>
        RefusException.Conflit("deja_membre", "Ce compte est déjà membre de l'organisation.");

    private async Task<Membre> ChargerAsync(Guid membreId, CancellationToken cancellationToken) =>
        await context.Membres.Include(m => m.Roles).SingleOrDefaultAsync(m => m.Id == membreId && m.RevoqueLe == null, cancellationToken)
            .ConfigureAwait(false) ?? throw RefusException.Introuvable();

    // L'organisation garde toujours au moins un administrateur actif : le membre modifié est jugé sur son
    // état en mémoire (non enregistré), les autres sur la base.
    private async Task ExigerUnAdministrateurAsync(Membre modifie, CancellationToken cancellationToken)
    {
        var autre = await context.Membres.AnyAsync(
            m => m.Id != modifie.Id && m.RevoqueLe == null && m.AccepteLe != null && m.Roles.Any(r => r.Role == Role.Administrateur),
            cancellationToken).ConfigureAwait(false);
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

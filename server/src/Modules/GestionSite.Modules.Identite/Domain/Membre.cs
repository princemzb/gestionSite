namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Adhésion d'un compte à une organisation, avec ses rôles. Une révocation conserve l'historique
/// (CONTRATS : DELETE /members) ; la version protège les révisions concurrentes (409).
/// </summary>
internal sealed class Membre : IAppartientAOrganisation
{
    private readonly List<RoleAttribue> _roles = [];

    private Membre()
    {
    }

    internal Membre(Guid id, Guid organisationId, Guid compteId, DateTimeOffset creeLe)
    {
        Identifiants.ExigerNonVide(id, nameof(id));
        Identifiants.ExigerNonVide(organisationId, nameof(organisationId));
        Identifiants.ExigerNonVide(compteId, nameof(compteId));
        Id = id;
        OrganisationId = organisationId;
        CompteId = compteId;
        CreeLe = Horodatage.Valider(creeLe, nameof(creeLe));
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    /// <summary>Compte de connexion (US-06 S06-03) ; un seul membre par compte et par organisation.</summary>
    public Guid CompteId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset? RevoqueLe { get; private set; }

    /// <summary>
    /// Acceptation de l'invitation par le titulaire (ADR-10, complément du 28/10) : aucun droit avant. Pour un
    /// compte créé par l'invitation, l'activation par le lien vaut acceptation.
    /// </summary>
    public DateTimeOffset? AccepteLe { get; private set; }

    /// <summary>Adhésion effective : acceptée et non révoquée.</summary>
    internal bool EstActif => RevoqueLe is null && AccepteLe is not null;

    /// <summary>
    /// Version de l'adhésion et de ses rôles, augmentée par chaque modification (rôle accordé,
    /// révocation) : jeton de concurrence, une révision partie d'une version dépassée échoue (409).
    /// </summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<RoleAttribue> Roles => _roles;

    /// <summary>
    /// Accorde un rôle ; sans effet s'il est déjà accordé sur ce site. Le membre doit être chargé
    /// avec ses rôles (Include) : sinon le doublon est refusé par l'index unique de la base.
    /// </summary>
    internal void Accorder(Role role, Site? site)
    {
        if (RevoqueLe is not null)
        {
            throw new InvalidOperationException("Adhésion révoquée : aucun rôle ne peut plus être accordé.");
        }

        if (site is not null && site.OrganisationId != OrganisationId)
        {
            throw new ArgumentException("Le site appartient à une autre organisation.", nameof(site));
        }

        if (_roles.Exists(r => r.Role == role && r.SiteId == site?.Id))
        {
            return;
        }

        _roles.Add(new RoleAttribue(Guid.NewGuid(), OrganisationId, Id, role, site?.Id));
        Version++;
    }

    /// <summary>
    /// Révision complète des habilitations (PUT /members) : les rôles absents sont retirés, les nouveaux
    /// accordés ; une seule nouvelle version, même si plusieurs rôles changent.
    /// </summary>
    internal void Reviser(IReadOnlyCollection<(Role Role, Site? Site)> roles)
    {
        if (RevoqueLe is not null)
        {
            throw new InvalidOperationException("Adhésion révoquée : aucun rôle ne peut plus être accordé.");
        }

        var voulus = roles.Select(r => (r.Role, SiteId: r.Site?.Id)).ToHashSet();
        var avant = Version;
        var retires = _roles.RemoveAll(r => !voulus.Contains((r.Role, r.SiteId)));
        foreach (var (role, site) in roles)
        {
            Accorder(role, site);
        }

        Version = avant + (retires > 0 || Version != avant ? 1 : 0);
    }

    internal bool EstAdministrateur => RevoqueLe is null && _roles.Exists(r => r.Role == Role.Administrateur);

    internal void Accepter(DateTimeOffset le)
    {
        if (RevoqueLe is not null)
        {
            throw new InvalidOperationException("Invitation révoquée ou refusée.");
        }

        if (AccepteLe is null)
        {
            AccepteLe = Horodatage.Valider(le, nameof(le));
            Version++;
        }
    }

    internal void Revoquer(DateTimeOffset le)
    {
        if (RevoqueLe is not null)
        {
            return;
        }

        RevoqueLe = Horodatage.Valider(le, nameof(le));
        Version++;
    }
}

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

    /// <summary>Jeton de concurrence PostgreSQL (xmin), changé à chaque écriture de la ligne.</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<RoleAttribue> Roles => _roles;

    /// <summary>Accorde un rôle ; sans effet s'il est déjà accordé sur ce site.</summary>
    internal void Accorder(Role role, Site? site)
    {
        if (site is not null && site.OrganisationId != OrganisationId)
        {
            throw new ArgumentException("Le site appartient à une autre organisation.", nameof(site));
        }

        if (_roles.Exists(r => r.Role == role && r.SiteId == site?.Id))
        {
            return;
        }

        _roles.Add(new RoleAttribue(Guid.NewGuid(), OrganisationId, Id, role, site?.Id));
    }

    internal void Revoquer(DateTimeOffset le)
    {
        RevoqueLe ??= Horodatage.Valider(le, nameof(le));
    }
}

using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Un rôle d'un membre : sur toute l'organisation pour A, sur un site précis pour les autres.</summary>
internal sealed class RoleAttribue : IAppartientAOrganisation
{
    private RoleAttribue()
    {
    }

    internal RoleAttribue(Guid id, Guid organisationId, Guid membreId, Role role, Guid? siteId)
    {
        Identifiants.ExigerNonVide(id, nameof(id));
        Identifiants.ExigerNonVide(organisationId, nameof(organisationId));
        Identifiants.ExigerNonVide(membreId, nameof(membreId));
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Rôle inconnu.");
        }

        if ((role == Role.Administrateur) != (siteId is null))
        {
            throw new ArgumentException(
                "Le rôle administrateur couvre toute l'organisation ; les autres rôles exigent un site.", nameof(siteId));
        }

        if (siteId is { } site)
        {
            Identifiants.ExigerNonVide(site, nameof(siteId));
        }

        Id = id;
        OrganisationId = organisationId;
        MembreId = membreId;
        Role = role;
        SiteId = siteId;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid MembreId { get; private set; }

    public Role Role { get; private set; }

    public Guid? SiteId { get; private set; }
}

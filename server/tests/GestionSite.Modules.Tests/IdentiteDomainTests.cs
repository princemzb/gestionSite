using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

public sealed class IdentiteDomainTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organisation = Guid.NewGuid();

    [Fact]
    public void AdministratorCoversTheWholeOrganisation()
    {
        Assert.Throws<ArgumentException>(() => new RoleAttribue(Guid.NewGuid(), Organisation, Guid.NewGuid(), Role.Administrateur, Guid.NewGuid()));
        Assert.Null(new RoleAttribue(Guid.NewGuid(), Organisation, Guid.NewGuid(), Role.Administrateur, null).SiteId);
    }

    [Fact]
    public void OtherRolesRequireASite()
    {
        Assert.All(Enum.GetValues<Role>().Where(r => r != Role.Administrateur), role =>
        {
            Assert.Throws<ArgumentException>(() => new RoleAttribue(Guid.NewGuid(), Organisation, Guid.NewGuid(), role, null));
            Assert.Throws<ArgumentException>(() => new RoleAttribue(Guid.NewGuid(), Organisation, Guid.NewGuid(), role, Guid.Empty));
        });
    }

    [Fact]
    public void UnknownRoleIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoleAttribue(Guid.NewGuid(), Organisation, Guid.NewGuid(), (Role)42, Guid.NewGuid()));
    }

    [Fact]
    public void RolesAreCumulativeAndGrantedOncePerSite()
    {
        var membre = new Membre(Guid.NewGuid(), Organisation, Guid.NewGuid(), Instant);
        var nord = new Site(Guid.NewGuid(), Organisation, "Nord", Instant);
        var sud = new Site(Guid.NewGuid(), Organisation, "Sud", Instant);

        membre.Accorder(Role.Technicien, nord);
        membre.Accorder(Role.Technicien, nord);
        membre.Accorder(Role.Technicien, sud);
        membre.Accorder(Role.Responsable, nord);

        Assert.Equal(3, membre.Roles.Count);
        Assert.All(membre.Roles, r => Assert.Equal(Organisation, r.OrganisationId));
    }

    [Fact]
    public void RoleCannotTargetASiteOfAnotherOrganisation()
    {
        var membre = new Membre(Guid.NewGuid(), Organisation, Guid.NewGuid(), Instant);
        var ailleurs = new Site(Guid.NewGuid(), Guid.NewGuid(), "Ailleurs", Instant);

        Assert.Throws<ArgumentException>(() => membre.Accorder(Role.Technicien, ailleurs));
        Assert.Empty(membre.Roles);
    }

    [Fact]
    public void RevocationKeepsTheFirstDate()
    {
        var membre = new Membre(Guid.NewGuid(), Organisation, Guid.NewGuid(), Instant);

        membre.Revoquer(Instant.AddDays(1));
        membre.Revoquer(Instant.AddDays(2));

        Assert.Equal(Instant.AddDays(1), membre.RevoqueLe);
    }

    [Fact]
    public void IdentifiersAreRequired()
    {
        Assert.Throws<ArgumentException>(() => new Site(Guid.Empty, Organisation, "Nord", Instant));
        Assert.Throws<ArgumentException>(() => new Site(Guid.NewGuid(), Guid.Empty, "Nord", Instant));
        Assert.Throws<ArgumentException>(() => new Membre(Guid.NewGuid(), Organisation, Guid.Empty, Instant));
        Assert.Throws<ArgumentException>(() => new RoleAttribue(Guid.Empty, Organisation, Guid.NewGuid(), Role.Administrateur, null));
    }

    [Fact]
    public void SiteNamesFollowTheDisplayNameRules()
    {
        Assert.Equal("Église Nord", new Site(Guid.NewGuid(), Organisation, "  Église Nord​ ", Instant).Nom);
        Assert.Throws<ArgumentException>(() => new Site(Guid.NewGuid(), Organisation, "Nord\nSud", Instant));
    }

    [Fact]
    public void CurrentOrganisationIsSetOncePerRequest()
    {
        var courante = new OrganisationCourante();
        var id = Guid.NewGuid();

        courante.Definir(id);
        courante.Definir(id);

        Assert.Equal(id, courante.Id);
        Assert.Throws<InvalidOperationException>(() => courante.Definir(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new OrganisationCourante().Definir(Guid.Empty));
    }
}

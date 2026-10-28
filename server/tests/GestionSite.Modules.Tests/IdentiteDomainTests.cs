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
    public void ARevokedMemberGetsNoNewRole()
    {
        var membre = new Membre(Guid.NewGuid(), Organisation, Guid.NewGuid(), Instant);
        membre.Revoquer(Instant);

        Assert.Throws<InvalidOperationException>(() => membre.Accorder(Role.Administrateur, site: null));
        Assert.Empty(membre.Roles);
    }

    [Fact]
    public void IdentifiersAreRequired()
    {
        Assert.Throws<ArgumentException>(() => new Site(Guid.Empty, Organisation, "Nord", Instant));
        Assert.Throws<ArgumentException>(() => new Site(Guid.NewGuid(), Guid.Empty, "Nord", Instant));
        Assert.Throws<ArgumentException>(() => new Membre(Guid.NewGuid(), Organisation, Guid.Empty, Instant));
        Assert.Throws<ArgumentException>(() => new RoleAttribue(Guid.Empty, Organisation, Guid.NewGuid(), Role.Administrateur, null));
        Assert.Throws<ArgumentException>(() => new RoleAttribue(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Role.Administrateur, null));
        Assert.Throws<ArgumentException>(() => new RoleAttribue(Guid.NewGuid(), Organisation, Guid.Empty, Role.Administrateur, null));
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

    [Fact]
    public void CurrentOrganisationCannotBeSetInsideAnOpenTransaction()
    {
        var courante = new OrganisationCourante();
        courante.SignalerTransaction(ouverte: true);

        Assert.Throws<InvalidOperationException>(() => courante.Definir(Guid.NewGuid()));
        courante.SignalerTransaction(ouverte: false);
        courante.Definir(Guid.NewGuid());
    }

    [Fact]
    public void AMemberIsActiveOnlyOnceAcceptedAndUntilRevoked()
    {
        var membre = new Membre(Guid.NewGuid(), Organisation, Guid.NewGuid(), Instant);
        Assert.False(membre.EstActif);

        membre.Accepter(Instant.AddHours(1));
        membre.Accepter(Instant.AddHours(2));
        Assert.True(membre.EstActif);
        Assert.Equal(Instant.AddHours(1), membre.AccepteLe);

        membre.Revoquer(Instant.AddDays(1));
        Assert.False(membre.EstActif);
        Assert.Throws<InvalidOperationException>(() => new Membre(Guid.NewGuid(), Organisation, Guid.NewGuid(), Instant).Also(m => m.Revoquer(Instant)).Accepter(Instant));
    }

    [Fact]
    public void EveryChangeOfAMemberIncreasesItsVersion()
    {
        var membre = new Membre(Guid.NewGuid(), Organisation, Guid.NewGuid(), Instant);
        var site = new Site(Guid.NewGuid(), Organisation, "Nord", Instant);

        membre.Accorder(Role.Technicien, site);
        membre.Accorder(Role.Technicien, site); // sans effet
        membre.Revoquer(Instant);
        membre.Revoquer(Instant); // sans effet

        Assert.Equal(2, membre.Version);
        Assert.Throws<InvalidOperationException>(() => membre.Accepter(Instant)); // révoqué
    }
}

internal static class EssaiExtensions
{
    internal static T Also<T>(this T valeur, Action<T> action)
    {
        action(valeur);
        return valeur;
    }
}

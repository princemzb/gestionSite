using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>
/// Autorisation par rôle et par site, refus interorganisation (S06-05, T-01) sur PostgreSQL réel.
/// Les cas T-01.10 à T-01.18 portant sur dossiers, budgets, grilles et plans seront automatisés avec
/// leurs modules ; ici, les décisions qu'ils utiliseront.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class AutorisationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string MotDePasse = "un-mot-de-passe-solide";
    private static readonly DateTimeOffset Instant = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();
    private readonly Guid _s1 = Guid.NewGuid();
    private readonly Guid _s2 = Guid.NewGuid();
    private readonly Guid _sB = Guid.NewGuid();
    private readonly Dictionary<string, Guid> _comptes = [];
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        using (_api.CreateClient())
        {
        }

        await OrganisationAsync(_orgA, [(_s1, "S1"), (_s2, "S2")], new()
        {
            ["technicien"] = [(Role.Technicien, _s1)],
            ["responsable"] = [(Role.Responsable, _s1)],
            ["pasteur"] = [(Role.Pasteur, _s1)],
            ["administrateur"] = [(Role.Administrateur, null)],
            ["cumul"] = [(Role.Technicien, _s1), (Role.Responsable, _s2)],
        });
        await OrganisationAsync(_orgB, [(_sB, "SB")], new() { ["adminB"] = [(Role.Administrateur, null), (Role.Responsable, _sB)] });
        await InviterSansAcceptationAsync("invite", _orgA, Role.Responsable, _s1);
    }

    public async Task DisposeAsync()
    {
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Theory]
    [InlineData("technicien", Droit.ConsulterSite, "S1", DecisionAutorisation.Autorise)]
    [InlineData("technicien", Droit.ConsulterSite, "S2", DecisionAutorisation.Introuvable)] // T-01.11 : aucun rôle sur S2
    [InlineData("technicien", Droit.QualifierAffecter, "S1", DecisionAutorisation.Refuse)]
    [InlineData("technicien", Droit.ConsommerStock, "S1", DecisionAutorisation.Autorise)]
    [InlineData("responsable", Droit.QualifierAffecter, "S1", DecisionAutorisation.Autorise)]
    [InlineData("pasteur", Droit.LireMontants, "S1", DecisionAutorisation.Autorise)] // T-01.12
    [InlineData("pasteur", Droit.EngagerBudget, "S1", DecisionAutorisation.Refuse)]
    [InlineData("pasteur", Droit.LireContactsDeclarant, "S1", DecisionAutorisation.Autorise)] // T-01.10
    [InlineData("technicien", Droit.LireContactsDeclarant, "S1", DecisionAutorisation.Autorise)]
    [InlineData("technicien", Droit.ModifierGrilles, "S1", DecisionAutorisation.Refuse)] // T-01.16
    [InlineData("responsable", Droit.ModifierGrilles, "S1", DecisionAutorisation.Autorise)] // T-01.17
    [InlineData("technicien", Droit.ModifierInventaire, "S1", DecisionAutorisation.Autorise)] // T-01.15
    [InlineData("administrateur", Droit.ConsulterSite, "S1", DecisionAutorisation.Refuse)] // aucun accès implicite
    [InlineData("administrateur", Droit.QualifierAffecter, "S2", DecisionAutorisation.Refuse)]
    [InlineData("cumul", Droit.QualifierAffecter, "S2", DecisionAutorisation.Autorise)]
    [InlineData("cumul", Droit.QualifierAffecter, "S1", DecisionAutorisation.Refuse)] // rôles par site, non transférés
    [InlineData("cumul", Droit.ConsommerStock, "S1", DecisionAutorisation.Autorise)]
    [InlineData("technicien", Droit.ConsulterSite, "SB", DecisionAutorisation.Introuvable)] // site d'une autre organisation
    [InlineData("administrateur", Droit.ConsulterSite, "SB", DecisionAutorisation.Introuvable)]
    [InlineData("invite", Droit.ConsulterSite, "S1", DecisionAutorisation.Introuvable)] // invitation non acceptée
    public async Task SiteRightsFollowRolesPerSite(string compte, Droit droit, string site, DecisionAutorisation attendue)
    {
        Assert.Equal(attendue, await VerifierAsync(compte, _orgA, droit, Site(site)));
    }

    [Theory]
    [InlineData("administrateur", DecisionAutorisation.Autorise)]
    [InlineData("responsable", DecisionAutorisation.Refuse)]
    [InlineData("adminB", DecisionAutorisation.Introuvable)] // administrateur d'une autre organisation
    [InlineData("invite", DecisionAutorisation.Introuvable)]
    public async Task AccountManagementIsForTheOrganisationAdministrator(string compte, DecisionAutorisation attendue)
    {
        Assert.Equal(attendue, await VerifierAsync(compte, _orgA, Droit.GererComptes, null));
    }

    [Fact]
    public async Task UnknownOrganisationAndUnknownSiteAreBothIntrouvable()
    {
        Assert.Equal(DecisionAutorisation.Introuvable, await VerifierAsync("technicien", Guid.NewGuid(), Droit.ConsulterSite, _s1));
        Assert.Equal(DecisionAutorisation.Introuvable, await VerifierAsync("administrateur", _orgA, Droit.ConsulterSite, Guid.NewGuid()));
    }

    [Fact]
    public void DefaultDecisionNeverAuthorises()
    {
        Assert.NotEqual(DecisionAutorisation.Autorise, default);
    }

    [Fact]
    public async Task MisusedArgumentsFailTheSameWayForEveryone()
    {
        await using var membre = Scope("technicien");
        await using var etranger = Scope("adminB");

        await Assert.ThrowsAsync<ArgumentException>(() => membre.ServiceProvider.GetRequiredService<IAutorisations>()
            .VerifierAsync(_orgA, Droit.ConsulterSite, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => etranger.ServiceProvider.GetRequiredService<IAutorisations>()
            .VerifierAsync(_orgA, Droit.GererComptes, _s1, CancellationToken.None));
    }

    [Fact]
    public async Task AuthorisedSitesListOnlySitesWhereTheRoleGrantsTheRight()
    {
        await using var scope = Scope("cumul");
        var autorisations = scope.ServiceProvider.GetRequiredService<IAutorisations>();

        Assert.Equal(new[] { _s1, _s2 }.Order(), (await autorisations.SitesAutorisesAsync(_orgA, Droit.ConsulterSite, CancellationToken.None)).Order());
        Assert.Equal([_s2], await autorisations.SitesAutorisesAsync(_orgA, Droit.QualifierAffecter, CancellationToken.None));
    }

    [Fact]
    public async Task EveryOrganisationRouteRequiresAuthentication()
    {
        var routes = _api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/organizations", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(routes);
        Assert.All(routes, e => Assert.NotEmpty(e.Metadata.GetOrderedMetadata<IAuthorizeData>()));
        Assert.All(routes, e => Assert.Empty(e.Metadata.GetOrderedMetadata<IAllowAnonymous>()));
    }

    [Theory]
    [InlineData("GET", "/sites")]
    [InlineData("POST", "/sites")]
    [InlineData("POST", "/members")]
    [InlineData("PUT", "/members/{membre}")]
    [InlineData("DELETE", "/members/{membre}")]
    [InlineData("POST", "/members/{membre}/activation")]
    public async Task AnotherOrganisationAnswersExactlyLikeANonexistentOne(string methode, string chemin)
    {
        var jeton = await ConnecterAsync("adminB");
        var membre = _comptes["technicien"]; // identifiant quelconque : jamais atteint
        using var client = _api.CreateClient();

        using var etrangere = await EnvoyerAsync(client, methode, $"/organizations/{_orgA}{chemin.Replace("{membre}", membre.ToString(), StringComparison.Ordinal)}", jeton);
        using var inexistante = await EnvoyerAsync(client, methode, $"/organizations/{Guid.NewGuid()}{chemin.Replace("{membre}", membre.ToString(), StringComparison.Ordinal)}", jeton);

        Assert.Equal(HttpStatusCode.NotFound, etrangere.StatusCode);
        Assert.Equal(inexistante.StatusCode, etrangere.StatusCode);
        Assert.Equal(await inexistante.Content.ReadAsStringAsync(), await etrangere.Content.ReadAsStringAsync());
    }

    private Guid? Site(string nom) => nom switch
    {
        "S1" => _s1,
        "S2" => _s2,
        "SB" => _sB,
        _ => null,
    };

    private async Task<DecisionAutorisation> VerifierAsync(string compte, Guid organisation, Droit droit, Guid? site)
    {
        await using var scope = Scope(compte);
        return await scope.ServiceProvider.GetRequiredService<IAutorisations>().VerifierAsync(organisation, droit, site, CancellationToken.None);
    }

    private AsyncServiceScope Scope(string compte)
    {
        var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CompteCourant>().Definir(_comptes[compte]);
        return scope;
    }

    private async Task OrganisationAsync(Guid organisationId, (Guid Id, string Nom)[] sites, Dictionary<string, (Role Role, Guid? Site)[]> membres)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(organisationId);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var entites = sites.Select(s => new Site(s.Id, organisationId, s.Nom, Instant)).ToDictionary(s => s.Id);
        context.Add(new Organisation(organisationId, $"Organisation {organisationId:N}", Instant));
        context.AddRange(entites.Values);
        foreach (var (nom, roles) in membres)
        {
            var compte = await CompteAsync(scope, nom);
            var membre = new Membre(Guid.NewGuid(), organisationId, compte, Instant);
            membre.Reviser([.. roles.Select(r => (r.Role, r.Site is { } id ? entites[id] : null))]);
            membre.Accepter(Instant);
            context.Add(membre);
        }

        await context.SaveChangesAsync();
    }

    private async Task InviterSansAcceptationAsync(string nom, Guid organisationId, Role role, Guid site)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(organisationId);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var membre = new Membre(Guid.NewGuid(), organisationId, await CompteAsync(scope, nom), Instant);
        membre.Accorder(role, context.Sites.Single(s => s.Id == site));
        context.Add(membre);
        await context.SaveChangesAsync();
    }

    private async Task<Guid> CompteAsync(AsyncServiceScope scope, string nom)
    {
        var compte = new Compte { Id = Guid.NewGuid(), UserName = $"{nom}@exemple.test", Email = $"{nom}@exemple.test" };
        var creation = await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse);
        Assert.True(creation.Succeeded);
        _comptes[nom] = compte.Id;
        return compte.Id;
    }

    private async Task<string> ConnecterAsync(string nom)
    {
        using var client = _api.CreateClient();
        using var reponse = await client.PostAsJsonAsync("/auth/login", new { email = $"{nom}@exemple.test", motDePasse = MotDePasse });
        return (await reponse.Content.ReadFromJsonAsync<JetonsLus>())!.JetonAcces;
    }

    private static async Task<HttpResponseMessage> EnvoyerAsync(HttpClient client, string methode, string chemin, string jeton)
    {
        using var demande = new HttpRequestMessage(new HttpMethod(methode), new Uri(chemin, UriKind.Relative));
        demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        if (methode is "POST" or "PUT")
        {
            demande.Content = JsonContent.Create(new { nom = "x", email = "x@exemple.test", roles = new[] { new { role = "Administrateur" } }, version = 1 });
        }

        return await client.SendAsync(demande);
    }

    private sealed record JetonsLus(string JetonAcces);
}

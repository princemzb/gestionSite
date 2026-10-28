using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>Connexion, jetons, verrouillage et GET /me (S06-03) sur l'API et un PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class AuthentificationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string Email = "marc.technicien@exemple.test";
    private const string MotDePasse = "un-mot-de-passe-solide";
    private static readonly DateTimeOffset Instant = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);
    private readonly Guid _organisationA = Guid.NewGuid();
    private readonly Guid _organisationB = Guid.NewGuid();
    private readonly Guid _siteNord = Guid.NewGuid();
    private readonly Guid _siteSud = Guid.NewGuid();
    private readonly Guid _siteB = Guid.NewGuid();
    private Guid _compte;
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        _client = _api.CreateClient();

        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var compte = new Compte { Id = Guid.NewGuid(), UserName = Email, Email = Email };
            var creation = await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse);
            Assert.True(creation.Succeeded, string.Join(", ", creation.Errors.Select(e => e.Code)));
            _compte = compte.Id;
        }

        // Technicien à Nord seulement dans A ; administrateur de B.
        await SeedAsync(_organisationA, [(_siteNord, "Nord"), (_siteSud, "Sud")], (Role.Technicien, _siteNord));
        await SeedAsync(_organisationB, [(_siteB, "Centre")], (Role.Administrateur, null));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task LoginThenMeListsOnlyAuthorisedOrganisationsAndSites()
    {
        var jetons = await ConnecterAsync();

        using var me = await AvecJeton(HttpMethod.Get, "/me", jetons.JetonAcces);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var corps = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(_compte, corps.GetProperty("compteId").GetGuid());
        var organisations = corps.GetProperty("organisations").EnumerateArray().ToDictionary(o => o.GetProperty("id").GetGuid());
        Assert.Equal(2, organisations.Count);
        Assert.Equal([_siteNord], SitesDe(organisations[_organisationA]));
        Assert.Equal([_siteB], SitesDe(organisations[_organisationB]));
    }

    [Fact]
    public async Task UnknownAccountAndWrongPasswordGetTheSameAnswer()
    {
        using var inconnu = await _client.PostAsJsonAsync("/auth/login", new { email = "personne@exemple.test", motDePasse = MotDePasse });
        using var faux = await _client.PostAsJsonAsync("/auth/login", new { email = Email, motDePasse = "mauvais-mot-de-passe" });

        Assert.Equal(HttpStatusCode.Unauthorized, inconnu.StatusCode);
        Assert.Equal(inconnu.StatusCode, faux.StatusCode);
        Assert.Equal(await inconnu.Content.ReadAsStringAsync(), await faux.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FiveFailuresLockTheAccountEvenForTheRightPassword()
    {
        for (var essai = 1; essai <= 4; essai++)
        {
            using var echec = await _client.PostAsJsonAsync("/auth/login", new { email = Email, motDePasse = "mauvais" });
            Assert.Equal(HttpStatusCode.Unauthorized, echec.StatusCode);
        }

        using var cinquieme = await _client.PostAsJsonAsync("/auth/login", new { email = Email, motDePasse = "mauvais" });
        using var bon = await _client.PostAsJsonAsync("/auth/login", new { email = Email, motDePasse = MotDePasse });

        Assert.Equal((HttpStatusCode)423, cinquieme.StatusCode);
        Assert.Equal((HttpStatusCode)423, bon.StatusCode);
    }

    [Fact]
    public async Task RefreshRotatesAndAReusedTokenRevokesTheWholeFamily()
    {
        var premier = await ConnecterAsync();
        var second = await RenouvelerAsync(premier.JetonRenouvellement);
        Assert.NotEqual(premier.JetonRenouvellement, second.JetonRenouvellement);

        using var reutilise = await _client.PostAsJsonAsync("/auth/refresh", new { jetonRenouvellement = premier.JetonRenouvellement });
        using var successeur = await _client.PostAsJsonAsync("/auth/refresh", new { jetonRenouvellement = second.JetonRenouvellement });

        Assert.Equal(HttpStatusCode.Unauthorized, reutilise.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, successeur.StatusCode); // lignée révoquée : vol présumé
    }

    [Fact]
    public async Task LogoutRevokesTheDeviceRefreshToken()
    {
        var jetons = await ConnecterAsync();

        using var deconnexion = await AvecJeton(HttpMethod.Post, "/auth/logout", jetons.JetonAcces, new { jetonRenouvellement = jetons.JetonRenouvellement });
        using var apres = await _client.PostAsJsonAsync("/auth/refresh", new { jetonRenouvellement = jetons.JetonRenouvellement });

        Assert.Equal(HttpStatusCode.NoContent, deconnexion.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apres.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pas.un.jeton")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiIwMDAwIn0.")] // alg=none
    public async Task MeRequiresAValidAccessToken(string? jeton)
    {
        using var reponse = await AvecJeton(HttpMethod.Get, "/me", jeton);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas-du-base64-!!")]
    [InlineData("AAAA")]
    public async Task MalformedRefreshTokensAreRefused(string jeton)
    {
        using var reponse = await _client.PostAsJsonAsync("/auth/refresh", new { jetonRenouvellement = jeton });

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task SecretsAreStoredOnlyAsHashes()
    {
        var jetons = await ConnecterAsync();

        Assert.Equal(0L, await _database.CountAsync("SELECT count(*) FROM identite.comptes WHERE password_hash LIKE '%' || @mdp || '%'", ("mdp", MotDePasse)));
        Assert.Equal(1L, await _database.CountAsync("SELECT count(*) FROM identite.jetons_renouvellement WHERE compte_id = @compte", ("compte", _compte)));
        Assert.Equal(0L, await _database.CountAsync(
            "SELECT count(*) FROM identite.jetons_renouvellement WHERE encode(empreinte, 'escape') LIKE '%' || @jeton || '%'",
            ("jeton", jetons.JetonRenouvellement)));
    }

    [Fact]
    public async Task AccountPoliciesShowOnlyOwnMembershipsAndAllowNoWrite()
    {
        await using var connection = new NpgsqlConnection(_database.ApplicationConnectionString);
        await connection.OpenAsync();
        await ExecuterAsync(connection, $"SET {CompteCourant.VariableSession} = '{Guid.NewGuid()}'"); // un autre compte

        Assert.Equal(0L, await ExecuterAsync(connection, "SELECT count(*) FROM identite.membres"));
        Assert.Equal(0L, await ExecuterAsync(connection, "SELECT count(*) FROM identite.sites"));

        await ExecuterAsync(connection, $"SET {CompteCourant.VariableSession} = '{_compte}'");
        Assert.Equal(2L, await ExecuterAsync(connection, "SELECT count(*) FROM identite.membres"));
        Assert.Equal(2L, await ExecuterAsync(connection, "SELECT count(*) FROM identite.sites")); // Nord (A) et Centre (B), pas Sud
        var ecriture = await Assert.ThrowsAsync<PostgresException>(() => ExecuterAsync(connection,
            $"INSERT INTO identite.sites (id, organisation_id, nom, cree_le) VALUES ('{Guid.NewGuid()}', '{_organisationA}', 'Intrus', now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ecriture.SqlState);
    }

    [Fact]
    public async Task RevokedMembershipDisappearsFromMe()
    {
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_organisationA);
            var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
            var membre = context.Membres.Single(m => m.CompteId == _compte);
            membre.Revoquer(Instant);
            await context.SaveChangesAsync();
        }

        var jetons = await ConnecterAsync();
        using var me = await AvecJeton(HttpMethod.Get, "/me", jetons.JetonAcces);
        var organisations = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement.GetProperty("organisations");

        Assert.Equal([_organisationB], organisations.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
    }

    private async Task<Jetons> ConnecterAsync()
    {
        using var reponse = await _client.PostAsJsonAsync("/auth/login", new { email = Email, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<Jetons>())!;
    }

    private async Task<Jetons> RenouvelerAsync(string jeton)
    {
        using var reponse = await _client.PostAsJsonAsync("/auth/refresh", new { jetonRenouvellement = jeton });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<Jetons>())!;
    }

    private async Task<HttpResponseMessage> AvecJeton(HttpMethod methode, string chemin, string? jeton, object? corps = null)
    {
        using var demande = new HttpRequestMessage(methode, new Uri(chemin, UriKind.Relative));
        if (jeton is not null)
        {
            demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        }

        if (corps is not null)
        {
            demande.Content = JsonContent.Create(corps);
        }

        return await _client.SendAsync(demande);
    }

    private static Guid[] SitesDe(JsonElement organisation) =>
        [.. organisation.GetProperty("sites").EnumerateArray().Select(s => s.GetProperty("id").GetGuid())];

    private async Task SeedAsync(Guid organisationId, (Guid Id, string Nom)[] sites, (Role Role, Guid? Site) role)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(organisationId);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var entites = sites.Select(s => new Site(s.Id, organisationId, s.Nom, Instant)).ToList();
        var membre = new Membre(Guid.NewGuid(), organisationId, _compte, Instant);
        membre.Accorder(role.Role, entites.SingleOrDefault(s => s.Id == role.Site));
        context.Add(new Organisation(organisationId, $"Organisation {organisationId:N}", Instant));
        context.AddRange(entites);
        context.Add(membre);
        await context.SaveChangesAsync();
    }

    private static async Task<object?> ExecuterAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private sealed record Jetons(string JetonAcces, DateTimeOffset AccesExpireLe, string JetonRenouvellement, DateTimeOffset RenouvellementExpireLe);
}

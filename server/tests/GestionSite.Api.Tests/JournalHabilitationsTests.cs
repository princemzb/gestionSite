using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>Journal des habilitations (S06-06, RG-31) sur l'API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class JournalHabilitationsTests(InfrastructureFixture infrastructure) : IAsyncLifetime, IDisposable
{
    private const string AdminEmail = "admin@journal.test";
    private const string MotDePasse = "un-mot-de-passe-solide";
    private readonly RecordingLoggerProvider _journaux = new();
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private Guid _organisation;
    private string _admin = null!;
    private string _jetonActivationAdmin = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database, logs: _journaux);
        _client = _api.CreateClient();
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, "Saint-Paul", AdminEmail, "Admin", CancellationToken.None);
        _organisation = ouverte.OrganisationId;
        _jetonActivationAdmin = ouverte.JetonActivation!;
        await ActiverAsync(_jetonActivationAdmin);
        _admin = await ConnecterAsync(AdminEmail);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    public void Dispose() => _journaux.Dispose();

    [Fact]
    public async Task EveryChangeKeepsAuthorDateReasonAndPreviousValue()
    {
        var nord = await CreerSiteAsync("Nord");
        using var creation = await Envoyer(HttpMethod.Post, $"/organizations/{_organisation}/members", _admin,
            new { nom = "Marc", email = "marc@journal.test", roles = new[] { new { role = "Technicien", siteId = (Guid?)nord } }, motif = "Arrivée de Marc" });
        var cree = await Json(creation);
        var membre = cree.GetProperty("membreId").GetGuid();
        using var revision = await Envoyer(HttpMethod.Put, $"/organizations/{_organisation}/members/{membre}", _admin,
            new { roles = new[] { new { role = "Responsable", siteId = (Guid?)nord } }, version = cree.GetProperty("version").GetInt32(), motif = "Promotion" });
        using var revocation = await Envoyer(HttpMethod.Delete, $"/organizations/{_organisation}/members/{membre}", _admin, new { motif = "Départ" });

        var entrees = (await JournalAsync(_admin)).Where(e => e.GetProperty("membreId").GetGuid() == membre).ToList();
        var auteur = (await Json(await Envoyer(HttpMethod.Get, "/me", _admin))).GetProperty("compteId").GetGuid();

        Assert.Equal(["Revocation", "Revision", "Creation"], entrees.Select(e => e.GetProperty("nature").GetString()));
        Assert.All(entrees, e => Assert.Equal(auteur, e.GetProperty("auteurCompteId").GetGuid()));
        Assert.Equal(["Départ", "Promotion", "Arrivée de Marc"], entrees.Select(e => e.GetProperty("motif").GetString()));
        Assert.Equal("Technicien", entrees[1].GetProperty("rolesAvant")[0].GetProperty("role").GetString());
        Assert.Equal("Responsable", entrees[1].GetProperty("rolesApres")[0].GetProperty("role").GetString());
        Assert.Equal("Responsable", entrees[0].GetProperty("rolesAvant")[0].GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, entrees[0].GetProperty("rolesApres").ValueKind);
        Assert.Equal(JsonValueKind.Null, entrees[2].GetProperty("rolesAvant").ValueKind);
    }

    [Fact]
    public async Task RevisionAndRevocationRequireAReasonAndNoOpIsNotJournaled()
    {
        var nord = await CreerSiteAsync("Nord");
        using var creation = await Envoyer(HttpMethod.Post, $"/organizations/{_organisation}/members", _admin,
            new { nom = "Marc", email = "marc@journal.test", roles = new[] { new { role = "Technicien", siteId = (Guid?)nord } } });
        var cree = await Json(creation);
        var membre = cree.GetProperty("membreId").GetGuid();
        var version = cree.GetProperty("version").GetInt32();

        using var sansMotif = await Envoyer(HttpMethod.Put, $"/organizations/{_organisation}/members/{membre}", _admin,
            new { roles = new[] { new { role = "Responsable", siteId = (Guid?)nord } }, version });
        using var revocationSansMotif = await Envoyer(HttpMethod.Delete, $"/organizations/{_organisation}/members/{membre}", _admin);
        using var identique = await Envoyer(HttpMethod.Put, $"/organizations/{_organisation}/members/{membre}", _admin,
            new { roles = new[] { new { role = "Technicien", siteId = (Guid?)nord } }, version, motif = "Rien" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansMotif.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, revocationSansMotif.StatusCode);
        Assert.Equal(HttpStatusCode.OK, identique.StatusCode);
        Assert.Equal(["Creation"], (await JournalAsync(_admin)).Where(e => e.GetProperty("membreId").GetGuid() == membre)
            .Select(e => e.GetProperty("nature").GetString()));
    }

    [Fact]
    public async Task TheJournalIsReadPageByPage()
    {
        var page1 = await JournalAsync(_admin, "?limite=1");
        var page2 = await JournalAsync(_admin, $"?limite=1&avant={page1[0].GetProperty("id").GetGuid()}");
        var fin = await JournalAsync(_admin, $"?limite=1&avant={page2[0].GetProperty("id").GetGuid()}");

        Assert.Equal("Acceptation", page1.Single().GetProperty("nature").GetString());
        Assert.Equal("Ouverture", page2.Single().GetProperty("nature").GetString());
        Assert.Empty(fin);
    }

    [Fact]
    public async Task OpeningAndActivationAreJournaled()
    {
        var natures = (await JournalAsync(_admin)).Select(e => e.GetProperty("nature").GetString()).ToList();

        Assert.Equal(["Acceptation", "Ouverture"], natures);
        Assert.Equal(JsonValueKind.Null, (await JournalAsync(_admin))[1].GetProperty("auteurCompteId").ValueKind); // plateforme
    }

    [Fact]
    public async Task OnlyTheAdministratorReadsTheJournal()
    {
        var nord = await CreerSiteAsync("Nord");
        using var creation = await Envoyer(HttpMethod.Post, $"/organizations/{_organisation}/members", _admin,
            new { nom = "Marc", email = "marc@journal.test", roles = new[] { new { role = "Technicien", siteId = (Guid?)nord } } });
        await ActiverAsync((await Json(creation)).GetProperty("activation").GetProperty("jeton").GetString()!);
        var technicien = await ConnecterAsync("marc@journal.test");

        using var parTechnicien = await Envoyer(HttpMethod.Get, $"/organizations/{_organisation}/members/journal", technicien);
        using var ailleurs = await Envoyer(HttpMethod.Get, $"/organizations/{Guid.NewGuid()}/members/journal", _admin);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ailleurs.StatusCode);
    }

    [Theory]
    [InlineData("UPDATE identite.journal_habilitations SET motif = 'falsifié'")]
    [InlineData("DELETE FROM identite.journal_habilitations")]
    public async Task TheJournalCannotBeAlteredByTheApplicationRole(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ApplicationConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SET app.organisation_id = '{_organisation}'; {sql}", connection);

        var refus = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState);
    }

    [Fact]
    public async Task TechnicalLogsContainNoSecretOrEmail()
    {
        var nord = await CreerSiteAsync("Nord");
        using var creation = await Envoyer(HttpMethod.Post, $"/organizations/{_organisation}/members", _admin,
            new { nom = "Marc", email = "marc.secret@journal.test", roles = new[] { new { role = "Technicien", siteId = (Guid?)nord } } });
        var jeton = (await Json(creation)).GetProperty("activation").GetProperty("jeton").GetString()!;
        await ActiverAsync(jeton);
        using var connexion = await Envoyer(HttpMethod.Post, "/auth/login", null, new { email = "marc.secret@journal.test", motDePasse = MotDePasse });
        var renouvellement = (await Json(connexion)).GetProperty("jetonRenouvellement").GetString()!;

        var texte = string.Join('\n', _journaux.Entries.Select(e => e.Message));
        Assert.NotEmpty(_journaux.Entries);
        Assert.DoesNotContain("marc.secret", texte, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AdminEmail, texte, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MotDePasse, texte, StringComparison.Ordinal);
        Assert.DoesNotContain(jeton, texte, StringComparison.Ordinal);
        Assert.DoesNotContain(_jetonActivationAdmin, texte, StringComparison.Ordinal);
        Assert.DoesNotContain(renouvellement, texte, StringComparison.Ordinal);
    }

    private async Task<List<JsonElement>> JournalAsync(string jeton, string requete = "")
    {
        using var reponse = await Envoyer(HttpMethod.Get, $"/organizations/{_organisation}/members/journal{requete}", jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return [.. (await Json(reponse)).EnumerateArray()];
    }

    private async Task<Guid> CreerSiteAsync(string nom)
    {
        using var reponse = await Envoyer(HttpMethod.Post, $"/organizations/{_organisation}/sites", _admin, new { nom });
        return (await Json(reponse)).GetProperty("id").GetGuid();
    }

    private async Task ActiverAsync(string jeton)
    {
        using var reponse = await Envoyer(HttpMethod.Post, "/auth/activate", null, new { jeton, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    private async Task<string> ConnecterAsync(string email)
    {
        using var reponse = await Envoyer(HttpMethod.Post, "/auth/login", null, new { email, motDePasse = MotDePasse });
        return (await Json(reponse)).GetProperty("jetonAcces").GetString()!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage reponse) =>
        JsonDocument.Parse(await reponse.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpResponseMessage> Envoyer(HttpMethod methode, string chemin, string? jeton, object? corps = null)
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
}

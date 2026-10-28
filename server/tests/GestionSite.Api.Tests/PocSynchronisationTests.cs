using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestionSite.Api.Tests;

/// <summary>
/// POC PREP-06 (expérimental), côté serveur, sur l'API et PostgreSQL réels : coupure après validation,
/// doublons, deux auteurs, conflit. Le côté tablette est testé dans mobile/test/poc.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class PocSynchronisationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private Guid _organisation;
    private Guid _site;
    private string _admin = null!;
    private (Guid Id, string Jeton) _marc;
    private (Guid Id, string Jeton) _lea;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database, pocSynchronisation: true);
        _client = _api.CreateClient();
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, "Saint-Paul", "admin@poc.test", "Admin", CancellationToken.None);
        _organisation = ouverte.OrganisationId;
        await Activer(ouverte.JetonActivation!);
        _admin = (await Connecter("admin@poc.test")).Jeton;
        using var site = await Envoyer(HttpMethod.Post, $"/organizations/{_organisation}/sites", _admin, new { nom = "Nord" });
        _site = (await Json(site)).GetProperty("id").GetGuid();
        _marc = await Technicien("marc@poc.test");
        _lea = await Technicien("lea@poc.test");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task AResponseLostAfterCommitIsRecoveredByReplayingTheSameOperation()
    {
        var commande = Commande(_marc.Id, Guid.NewGuid(), baseVersion: 0, "Fuite constatée");

        var premier = await Appliquer(commande, _marc.Jeton); // validé côté serveur ; réponse supposée perdue
        var rejeu = await Appliquer(commande, _marc.Jeton);
        var troisieme = await Appliquer(commande, _marc.Jeton);

        Assert.Equal(("applied", 1), (Resultat(premier), Version(premier)));
        Assert.Equal(("alreadyApplied", 1), (Resultat(rejeu), Version(rejeu)));
        Assert.Equal(("alreadyApplied", 1), (Resultat(troisieme), Version(troisieme)));
        Assert.Equal(1L, await _database.CountAsync("SELECT count(*) FROM poc_sync.notes"));
        Assert.Equal(1L, await _database.CountAsync("SELECT count(*) FROM poc_sync.commandes"));
    }

    [Fact]
    public async Task SimultaneousDuplicatesApplyExactlyOnce()
    {
        var commande = Commande(_marc.Id, Guid.NewGuid(), baseVersion: 0, "Doublon");

        var resultats = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var client = _api.CreateClient();
            return Resultat(await Appliquer(commande, _marc.Jeton, client));
        }));

        Assert.Single(resultats, r => r == "applied");
        Assert.Equal(9, resultats.Count(r => r == "alreadyApplied"));
        Assert.Equal(1L, await _database.CountAsync("SELECT count(*) FROM poc_sync.notes WHERE version = 1"));
    }

    [Fact]
    public async Task ConcurrentEditsFromTwoDevicesProduceAnExplicitConflict()
    {
        var note = Guid.NewGuid();
        await Appliquer(Commande(_marc.Id, note, 0, "Version initiale"), _marc.Jeton);

        var tablette1 = await Appliquer(Commande(_marc.Id, note, 1, "Bâche posée", appareil: "tablette-1"), _marc.Jeton);
        var tablette2 = await Appliquer(Commande(_lea.Id, note, 1, "Seau installé", appareil: "tablette-2"), _lea.Jeton);
        var rejeuConflit = await Appliquer(Commande(_lea.Id, note, 1, "Seau installé", appareil: "tablette-2", operation: tablette2.GetProperty("operationId").GetGuid()), _lea.Jeton);

        Assert.Equal("applied", Resultat(tablette1));
        Assert.Equal("conflict", Resultat(tablette2));
        Assert.Equal(2, Version(tablette2));
        Assert.Equal("Bâche posée", tablette2.GetProperty("texteCourant").GetString()); // rien d'écrasé
        Assert.Equal("conflict", Resultat(rejeuConflit)); // même décision au rejeu
        Assert.Equal(1L, await _database.CountAsync("SELECT count(*) FROM poc_sync.notes WHERE texte = 'Bâche posée' AND version = 2"));
    }

    [Fact]
    public async Task AnOperationIdentifierCannotCarryDifferentContent()
    {
        var operation = Guid.NewGuid();
        var note = Guid.NewGuid();
        await Appliquer(Commande(_marc.Id, note, 0, "Texte A", operation: operation), _marc.Jeton);

        using var reponse = await Envoyer(HttpMethod.Post, Route(), _marc.Jeton, Commande(_marc.Id, note, 0, "Texte B", operation: operation));

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal("operation_reutilisee", (await Json(reponse)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ACommandIsNeverSentUnderAnotherAuthorsSession()
    {
        using var reponse = await Envoyer(HttpMethod.Post, Route(), _marc.Jeton, Commande(_lea.Id, Guid.NewGuid(), 0, "Saisie de Léa"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        Assert.Equal("acteur_different", (await Json(reponse)).GetProperty("code").GetString());
        Assert.Equal(0L, await _database.CountAsync("SELECT count(*) FROM poc_sync.commandes"));
    }

    [Fact]
    public async Task RightsAndIsolationStillApply()
    {
        var admin = (await Json(await Envoyer(HttpMethod.Get, "/me", _admin))).GetProperty("compteId").GetGuid();

        using var sansRole = await Envoyer(HttpMethod.Post, Route(), _admin, Commande(admin, Guid.NewGuid(), 0, "Admin"));
        using var autreOrganisation = await Envoyer(HttpMethod.Post, Route(Guid.NewGuid()), _marc.Jeton, Commande(_marc.Id, Guid.NewGuid(), 0, "Ailleurs"));
        using var sansJeton = await Envoyer(HttpMethod.Post, Route(), null, Commande(_marc.Id, Guid.NewGuid(), 0, "Anonyme"));

        Assert.Equal(HttpStatusCode.Forbidden, sansRole.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreOrganisation.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sansJeton.StatusCode);
    }

    [Fact]
    public async Task ThePocIsAbsentUnlessEnabled()
    {
        await using var api = infrastructure.CreateApi(_database);
        using var client = api.CreateClient();

        using var reponse = await Envoyer(HttpMethod.Post, Route(), _marc.Jeton, Commande(_marc.Id, Guid.NewGuid(), 0, "x"), client);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    private object Commande(Guid acteur, Guid note, int baseVersion, string texte, string appareil = "tablette-1", Guid? operation = null) => new
    {
        operationId = operation ?? Guid.NewGuid(),
        deviceId = appareil,
        acteurId = acteur,
        siteId = _site,
        noteId = note,
        baseVersion,
        texte,
        observeLe = DateTimeOffset.UtcNow,
    };

    private string Route(Guid? organisation = null) => $"/poc/sync/organizations/{organisation ?? _organisation}/commands";

    private async Task<JsonElement> Appliquer(object commande, string jeton, HttpClient? client = null)
    {
        using var reponse = await Envoyer(HttpMethod.Post, Route(), jeton, commande, client);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await Json(reponse);
    }

    private static string Resultat(JsonElement reponse) => reponse.GetProperty("resultat").GetString()!;

    private static int Version(JsonElement reponse) => reponse.GetProperty("version").GetInt32();

    private async Task<(Guid Id, string Jeton)> Technicien(string email)
    {
        using var creation = await Envoyer(HttpMethod.Post, $"/organizations/{_organisation}/members", _admin,
            new { nom = email, email, roles = new[] { new { role = "Technicien", siteId = (Guid?)_site } } });
        var cree = await Json(creation);
        await Activer(cree.GetProperty("activation").GetProperty("jeton").GetString()!);
        return (cree.GetProperty("compteId").GetGuid(), (await Connecter(email)).Jeton);
    }

    private async Task Activer(string jeton)
    {
        using var reponse = await Envoyer(HttpMethod.Post, "/auth/activate", null, new { jeton, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<(Guid Id, string Jeton)> Connecter(string email)
    {
        using var reponse = await Envoyer(HttpMethod.Post, "/auth/login", null, new { email, motDePasse = MotDePasse });
        return (Guid.Empty, (await Json(reponse)).GetProperty("jetonAcces").GetString()!);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage reponse) =>
        JsonDocument.Parse(await reponse.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpResponseMessage> Envoyer(HttpMethod methode, string chemin, string? jeton, object? corps = null, HttpClient? client = null)
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

        return await (client ?? _client).SendAsync(demande);
    }
}

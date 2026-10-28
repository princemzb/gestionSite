using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>Dépôt public sans compte (US-08, S08-01, T-02) sur l'API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class DepotPublicTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private OrganisationOuverte _ouverte = null!;
    private string _code = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        _client = _api.CreateClient();
        (_ouverte, _code) = await OuvrirAsync("Saint-Paul", "admin@depot.test");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task AReportWithoutPhotoIsRecordedOnTheSiteOfTheCode()
    {
        using var reponse = await DeposerAsync(_code, Demande());
        var accuse = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("Site centre", accuse.GetProperty("site").GetString());
        // Aucun identifiant ni secret permettant de relire le signalement (F-01).
        Assert.Equal(["site", "danger", "numeroUrgence"], accuse.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, await _database.CountAsync(
            "SELECT count(*) FROM maintenance.signalements s JOIN identite.sites si ON si.id = s.site_id " +
            "WHERE si.nom = 'Site centre' AND s.organisation_id = @org AND s.nature = 'Incident' AND NOT s.danger",
            ("org", _ouverte.OrganisationId)));
    }

    [Fact]
    public async Task TheCodeIsReadCaseInsensitivelyAndTheSiteIsNamed()
    {
        using var reponse = await _client.GetAsync(new Uri($"/public/sites/{_code.ToLowerInvariant()}", UriKind.Relative));
        var site = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("Site centre", site.GetProperty("site").GetString());
        Assert.Equal("Saint-Paul", site.GetProperty("organisation").GetString());
    }

    [Fact]
    public async Task AReplayGetsTheSameAcknowledgementButADifferentContentIsRefused()
    {
        var demande = Demande() with { Danger = true };

        using var premier = await DeposerAsync(_code, demande);
        using var rejeu = await DeposerAsync(_code, demande);
        using var modifie = await DeposerAsync(_code, demande with { Description = "Autre chose" });
        var probleme = await modifie.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(await premier.Content.ReadAsStringAsync(), await rejeu.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, modifie.StatusCode);
        Assert.Equal("commande_reutilisee", probleme.GetProperty("code").GetString());
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements"));
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.alertes_danger"));
    }

    [Theory]
    [InlineData("{\"commandeId\": \"abc\"}")]
    [InlineData("pas du JSON")]
    [InlineData("null")]
    public async Task AnUnreadableBodyIsAnInputError(string corps)
    {
        using var contenu = new StringContent(corps, System.Text.Encoding.UTF8, "application/json");
        using var reponse = await _client.PostAsync(new Uri($"/public/sites/{_code}/reports", UriKind.Relative), contenu);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        Assert.Equal("corps", probleme.GetProperty("champ").GetString());
    }

    [Theory]
    [InlineData("description", "Fuite\u0000d'eau")] // refusé par PostgreSQL : jamais une erreur 500
    [InlineData("emplacement", "Salle\u202Eetuoc")] // texte affiché à l'envers
    [InlineData("nom", "Jeanne\nP.")] // une ligne
    [InlineData("contact", "06\u000700")] // caractère de contrôle (une demi-paire arrive déjà remplacée par le lecteur JSON)
    public async Task ForbiddenCharactersAreInputErrors(string champ, string valeur)
    {
        var demande = champ switch
        {
            "description" => Demande() with { Description = valeur },
            "emplacement" => Demande() with { Emplacement = valeur },
            "nom" => Demande() with { Nom = valeur },
            _ => Demande() with { Contact = valeur },
        };

        using var reponse = await DeposerAsync(_code, demande);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        Assert.Equal(champ, probleme.GetProperty("champ").GetString());
    }

    [Fact]
    public async Task AMultiLineDescriptionIsKept()
    {
        using var reponse = await DeposerAsync(_code, Demande() with { Description = "Fuite au plafond.\nSeau posé dessous." });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements WHERE description LIKE '%\n%'"));
    }

    [Fact]
    public async Task SitesCreatedBeforeTheMigrationReceiveACode()
    {
        await using var ancienne = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await MigrerIdentiteAsync(ancienne, "20261028102034_JournalHabilitations");
        await ExecuterAsync(ancienne,
            "INSERT INTO identite.organisations (id, nom, cree_le) VALUES ('11111111-1111-1111-1111-111111111111', 'Ancienne', now()); " +
            "INSERT INTO identite.sites (id, organisation_id, nom, cree_le) VALUES " +
            "('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'Nef', now()), " +
            "('33333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', 'Crypte', now());");

        await MigrerIdentiteAsync(ancienne, cible: null);

        Assert.Equal(2, await ancienne.CountAsync(
            "SELECT count(DISTINCT code) FROM identite.codes_sites_publics WHERE code ~ '^[2-9A-HJKMNP-TV-Z]{10}$'"));
        Assert.Equal(1, await ancienne.CountAsync(
            "SELECT count(*) FROM pg_class WHERE relname = 'sites' AND relforcerowsecurity"));
    }

    [Fact]
    public async Task ParallelSubmissionsOfTheSameCommandCreateASingleReport()
    {
        var demande = Demande();

        var reponses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => DeposerAsync(_code, demande)));

        Assert.All(reponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements"));
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    [Theory]
    [InlineData("ZZZZZZZZZZ")] // bien formé, inconnu
    [InlineData("abc")] // mal formé
    public async Task AnUnknownSiteCodeIsNotFound(string code)
    {
        using var depot = await DeposerAsync(code, Demande());
        using var site = await _client.GetAsync(new Uri($"/public/sites/{code}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, depot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, site.StatusCode);
        Assert.Equal(0, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements"));
    }

    [Theory]
    [InlineData("description")]
    [InlineData("emplacement")]
    [InlineData("nom")]
    [InlineData("contact")]
    [InlineData("nature")]
    [InlineData("commandeId")]
    public async Task EachRequiredFieldIsChecked(string champ)
    {
        var demande = champ switch
        {
            "description" => Demande() with { Description = "  " },
            "emplacement" => Demande() with { Emplacement = null },
            "nom" => Demande() with { Nom = "" },
            "contact" => Demande() with { Contact = new string('x', 201) },
            "nature" => Demande() with { Nature = "1" },
            _ => Demande() with { CommandeId = Guid.Empty },
        };

        using var reponse = await DeposerAsync(_code, demande);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        Assert.Equal("donnee_invalide", probleme.GetProperty("code").GetString());
        Assert.Equal(champ, probleme.GetProperty("champ").GetString());
        Assert.Equal(0, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements"));
    }

    [Fact]
    public async Task ADangerRecordsAnAlertAndGivesTheSiteEmergencyNumber()
    {
        await DefinirNumeroUrgenceAsync("01 23 45 67 89");

        using var danger = await DeposerAsync(_code, Demande() with { Danger = true, ActiviteBloquee = true });
        using var ordinaire = await DeposerAsync(_code, Demande());
        var accuseDanger = await danger.Content.ReadFromJsonAsync<JsonElement>();
        var accuseOrdinaire = await ordinaire.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(accuseDanger.GetProperty("danger").GetBoolean());
        Assert.Equal("01 23 45 67 89", accuseDanger.GetProperty("numeroUrgence").GetString());
        Assert.Equal(JsonValueKind.Null, accuseOrdinaire.GetProperty("numeroUrgence").ValueKind);
        Assert.Equal(1, await _database.CountAsync(
            "SELECT count(*) FROM maintenance.alertes_danger a JOIN maintenance.signalements s ON s.id = a.signalement_id " +
            "WHERE s.danger AND s.activite_bloquee AND a.site_id = s.site_id"));
    }

    [Fact]
    public async Task ReportsStayInTheOrganisationOfTheSite()
    {
        var (autre, codeAutre) = await OuvrirAsync("Autre église", "admin@autre.test");

        using var ici = await DeposerAsync(_code, Demande());
        using var la = await DeposerAsync(codeAutre, Demande());

        Assert.Equal(1, await _database.CountAsync(
            "SELECT count(*) FROM maintenance.signalements WHERE organisation_id = @org", ("org", autre.OrganisationId)));
        // Seconde barrière : le rôle de l'API ne voit rien sans organisation, seulement la sienne avec.
        Assert.Equal(0, await CompterCommeApplicationAsync(organisation: null));
        Assert.Equal(1, await CompterCommeApplicationAsync(_ouverte.OrganisationId));
    }

    private static async Task MigrerIdentiteAsync(TestDatabase base_, string? cible)
    {
        await using var proprietaire = NpgsqlDataSource.Create(base_.MigrationsConnectionString);
        var options = new DbContextOptionsBuilder<IdentiteDbContext>();
        IdentitePersistence.Configure(options, proprietaire, requete: null);
        await using var context = new IdentiteDbContext(options.Options, new OrganisationCourante());
        await context.GetService<IMigrator>().MigrateAsync(cible);
    }

    private static async Task ExecuterAsync(TestDatabase base_, string sql)
    {
        await using var connexion = new NpgsqlConnection(base_.ConnectionString);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand(sql, connexion);
        await commande.ExecuteNonQueryAsync();
    }

    private static DemandeTest Demande() => new(
        Guid.NewGuid(), "Incident", "La porte de secours ne ferme plus.", "Grande salle", false, false, "Jeanne P.", "06 00 00 00 00");

    private Task<HttpResponseMessage> DeposerAsync(string code, DemandeTest demande) =>
        _client.PostAsJsonAsync(new Uri($"/public/sites/{code}/reports", UriKind.Relative), demande);

    private async Task<(OrganisationOuverte Ouverte, string Code)> OuvrirAsync(string nom, string email)
    {
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, nom, email, "Admin", CancellationToken.None, premierSite: "Site centre");
        await using var connexion = new NpgsqlConnection(_database.ConnectionString);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand("SELECT code FROM identite.codes_sites_publics WHERE organisation_id = @org", connexion);
        commande.Parameters.AddWithValue("org", ouverte.OrganisationId);
        return (ouverte, (string)(await commande.ExecuteScalarAsync())!);
    }

    private async Task DefinirNumeroUrgenceAsync(string numero)
    {
        await using var connexion = new NpgsqlConnection(_database.ConnectionString);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand("UPDATE identite.sites SET numero_urgence = @numero WHERE organisation_id = @org", connexion);
        commande.Parameters.AddWithValue("numero", numero);
        commande.Parameters.AddWithValue("org", _ouverte.OrganisationId);
        await commande.ExecuteNonQueryAsync();
    }

    private async Task<long> CompterCommeApplicationAsync(Guid? organisation)
    {
        await using var connexion = new NpgsqlConnection(_database.ApplicationConnectionString);
        await connexion.OpenAsync();
        await using var transaction = await connexion.BeginTransactionAsync();
        if (organisation is { } id)
        {
            await using var poser = new NpgsqlCommand($"SET LOCAL app.organisation_id = '{id:D}'", connexion, transaction);
            await poser.ExecuteNonQueryAsync();
        }

        await using var compter = new NpgsqlCommand("SELECT count(*) FROM maintenance.signalements", connexion, transaction);
        return (long)(await compter.ExecuteScalarAsync())!;
    }

    private sealed record DemandeTest(
        Guid CommandeId, string? Nature, string? Description, string? Emplacement, bool Danger, bool ActiviteBloquee, string? Nom, string? Contact);
}

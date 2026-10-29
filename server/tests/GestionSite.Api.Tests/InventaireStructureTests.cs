using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>Structure de l'inventaire d'un site (US-07, S07-01) sur l'API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class InventaireStructureTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private Guid _organisation;
    private Guid _site;
    private string _base = null!;
    private string? _activationAdministrateur;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        _client = _api.CreateClient();
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, "Saint-Paul", "admin@inventaire.test", "Admin", CancellationToken.None, premierSite: "Site centre");
        _organisation = ouverte.OrganisationId;
        _activationAdministrateur = ouverte.JetonActivation;
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_organisation);
        _site = await scope.ServiceProvider.GetRequiredService<IdentiteDbContext>().Sites.Select(s => s.Id).SingleAsync();
        _base = $"/organizations/{_organisation}/sites/{_site}/inventory";
        await MembreAsync("technicien@inventaire.test", Role.Technicien);
        await MembreAsync("pasteur@inventaire.test", Role.Pasteur);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ABuildingComesWithItsCommonAreasAndEveryChangeIsJournaled()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");

        var batiment = await CreerAsync($"{_base}/buildings", new { nom = "Église" }, technicien);
        var rdc = await CreerAsync($"{_base}/buildings/{batiment}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        var salle = await CreerAsync($"{_base}/zones", new { nom = "Grande salle", batimentId = batiment, niveauId = rdc }, technicien);
        using var renomme = await EnvoyerAsync(HttpMethod.Put, $"{_base}/zones/{salle}", technicien, new { nom = "Grande salle paroissiale" });
        using var structure = await EnvoyerAsync(HttpMethod.Get, $"{_base}/structure", await JetonAsync("pasteur@inventaire.test"));
        var lue = (await structure.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();

        Assert.Equal(HttpStatusCode.NoContent, renomme.StatusCode);
        Assert.Equal("Église", lue.GetProperty("nom").GetString());
        Assert.Equal(["Rez-de-chaussée"], lue.GetProperty("niveaux").EnumerateArray().Select(n => n.GetProperty("nom").GetString()));
        Assert.Equal(["Grande salle paroissiale", "Parties communes"], lue.GetProperty("zones").EnumerateArray().Select(z => z.GetProperty("nom").GetString()));
        Assert.True(lue.GetProperty("zones")[1].GetProperty("partiesCommunes").GetBoolean());
        Assert.Equal(5, await _database.CountAsync("SELECT count(*) FROM patrimoine.journal_inventaire WHERE auteur_compte_id IS NOT NULL"));
        Assert.Equal(1, await _database.CountAsync(
            "SELECT count(*) FROM patrimoine.journal_inventaire WHERE nature = 'Modification' AND avant->>'nom' = 'Grande salle' AND apres->>'nom' = 'Grande salle paroissiale'"));
    }

    [Theory]
    [InlineData("pasteur@inventaire.test", HttpStatusCode.Forbidden)] // lit, ne modifie pas
    [InlineData("admin@inventaire.test", HttpStatusCode.Forbidden)] // administrateur seul : aucun droit métier
    public async Task OnlyFieldRolesModifyTheInventory(string compte, HttpStatusCode attendu)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{_base}/buildings", await JetonAsync(compte), new { nom = "Presbytère" });

        Assert.Equal(attendu, reponse.StatusCode);
        Assert.Equal(0, await _database.CountAsync("SELECT count(*) FROM patrimoine.batiments"));
    }

    [Fact]
    public async Task AnotherSiteOrOrganisationIsNotFound()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");

        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{_organisation}/sites/{Guid.NewGuid()}/inventory/structure", technicien);
        using var autreOrganisation = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Guid.NewGuid()}/sites/{_site}/inventory/structure", technicien);

        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreOrganisation.StatusCode);
    }

    [Fact]
    public async Task NamesAreUniqueWhereTheyLiveAndCommonAreasKeepTheirName()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var eglise = await CreerAsync($"{_base}/buildings", new { nom = "Église" }, technicien);
        var salles = await CreerAsync($"{_base}/buildings", new { nom = "Salles annexes" }, technicien);
        var etage = await CreerAsync($"{_base}/buildings/{salles}/levels", new { nom = "1er étage", ordre = 1 }, technicien);
        var structure = await LireAsync(technicien);
        var communes = structure.EnumerateArray().First(b => b.GetProperty("nom").GetString() == "Église").GetProperty("zones")[0].GetProperty("id").GetGuid();

        using var doublon = await EnvoyerAsync(HttpMethod.Post, $"{_base}/buildings", technicien, new { nom = "Église" });
        using var niveauAilleurs = await EnvoyerAsync(HttpMethod.Post, $"{_base}/zones", technicien, new { nom = "Sacristie", batimentId = eglise, niveauId = etage });
        using var renommerCommunes = await EnvoyerAsync(HttpMethod.Put, $"{_base}/zones/{communes}", technicien, new { nom = "Hall" });
        using var nomVide = await EnvoyerAsync(HttpMethod.Post, $"{_base}/buildings", technicien, new { nom = " " });
        using var inconnu = await EnvoyerAsync(HttpMethod.Put, $"{_base}/buildings/{Guid.NewGuid()}", technicien, new { nom = "X" });

        Assert.Equal(HttpStatusCode.Conflict, doublon.StatusCode);
        Assert.Equal("niveauId", await ChampAsync(niveauAilleurs));
        Assert.Equal("nom", await ChampAsync(renommerCommunes));
        Assert.Equal("nom", await ChampAsync(nomVide));
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
    }

    private async Task<JsonElement> LireAsync(string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{_base}/structure", jeton);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> ChampAsync(HttpResponseMessage reponse)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("champ").GetString();
    }

    private async Task<Guid> CreerAsync(string chemin, object corps, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, chemin, jeton, corps);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> EnvoyerAsync(HttpMethod methode, string chemin, string jeton, object? corps = null)
    {
        using var demande = new HttpRequestMessage(methode, new Uri(chemin, UriKind.Relative));
        demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        if (corps is not null)
        {
            demande.Content = JsonContent.Create(corps);
        }

        return await _client.SendAsync(demande);
    }

    private async Task<string> JetonAsync(string email)
    {
        if (email.StartsWith("admin@", StringComparison.Ordinal) && _activationAdministrateur is { } jeton)
        {
            // Activation réelle : elle accepte aussi l'adhésion de l'administrateur.
            using var activation = await _client.PostAsJsonAsync("/auth/activate", new { jeton, motDePasse = MotDePasse });
            _activationAdministrateur = null;
        }

        using var reponse = await _client.PostAsJsonAsync("/auth/login", new { email, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jetonAcces").GetString()!;
    }

    private async Task MembreAsync(string email, Role role)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_organisation);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var compte = new Compte { Id = Guid.NewGuid(), UserName = email, Email = email };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse)).Succeeded);
        var membre = new Membre(Guid.NewGuid(), _organisation, compte.Id, DateTimeOffset.UtcNow);
        membre.Reviser([(role, await context.Sites.SingleAsync(s => s.Id == _site))]);
        membre.Accepter(DateTimeOffset.UtcNow);
        context.Add(membre);
        await context.SaveChangesAsync();
    }
}

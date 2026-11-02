using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.Modules.Patrimoine.PlansBatiment;
using GestionSite.SharedKernel;
using GestionSite.Web;
using GestionSite.Web.Inventaire;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>
/// Éditeur de plans de bâtiment dans l'application web (S21-07) : page Plans (parcours guidé, atelier, plan absent), relais
/// JSON de lecture, création et enregistrement (jeton antifalsification, conflit 409 transmis avec la version gagnante,
/// 401 sans session), effacement du stockage local à la déconnexion. Application web, API et PostgreSQL réels.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed partial class WebEditeurPlanTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private WebApplicationFactory<WebOptions> _web = null!;
    private Guid _organisation;
    private Guid _site;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        using (_api.CreateClient())
        {
        }

        _web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .ConfigureServices(services =>
            {
                services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => _api.Server.CreateHandler());
                services.AddHttpClient(Programme.ClientApiLong).ConfigurePrimaryHttpMessageHandler(() => _api.Server.CreateHandler());
            }));
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(
            _api.Services, "Église Saint-Paul", "admin@editeur-web.test", "Claire M.", CancellationToken.None, premierSite: "Site centre");
        _organisation = ouverte.OrganisationId;
        _site = await MembreAsync("responsable@editeur-web.test", "Rose R.", Role.Responsable);
        await MembreAsync("pasteur@editeur-web.test", "Paul R.", Role.Pasteur);
        await MembreAsync("technicien@editeur-web.test", "Marc D.", Role.Technicien);
    }

    public async Task DisposeAsync()
    {
        await _web.DisposeAsync();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ThePageOpensTheGuidedPathForAnEditorAndTellsAReaderThatThereIsNoPlanYet()
    {
        var lieux = await LieuxAsync();
        using var responsable = await ConnecterAsync("responsable@editeur-web.test");
        using var pasteur = await ConnecterAsync("pasteur@editeur-web.test");
        var page = $"/organisations/{_organisation}/plans?batiment={lieux.Eglise}&niveau={lieux.Rdc}";

        var pourResponsable = await PageAsync(responsable, page);
        var pourPasteur = await PageAsync(pasteur, page);

        Assert.Contains("Nouveau plan · Église · Rez-de-chaussée", pourResponsable, StringComparison.Ordinal);
        Assert.Contains("data-mode=\"guide\"", pourResponsable, StringComparison.Ordinal);
        Assert.Contains("data-modifiable=\"true\"", pourResponsable, StringComparison.Ordinal);
        Assert.Contains("<script type=\"module\" src=\"plans/editeur.mjs\"></script>", pourResponsable, StringComparison.Ordinal);
        Assert.Contains(">Sous-sol</a>", pourResponsable, StringComparison.Ordinal); // sélecteur de niveau
        Assert.Contains("aria-current=\"page\">Plans</a>", pourResponsable, StringComparison.Ordinal);
        Assert.Contains("Masquez d'abord les noms du cartouche", pourResponsable, StringComparison.Ordinal);
        Assert.DoesNotContain("style=", pourResponsable, StringComparison.Ordinal); // politique de sécurité du contenu
        Assert.Contains("Aucun plan de bâtiment pour ce niveau", pourPasteur, StringComparison.Ordinal);
        Assert.DoesNotContain("editeur.mjs", pourPasteur, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReaderSeesTheWorkshopWithoutAnyEditingCommand()
    {
        var lieux = await LieuxAsync();
        var plan = await PlanAsync(lieux.Rdc);
        await PlanAsync(lieux.SousSol);
        using var responsable = await ConnecterAsync("responsable@editeur-web.test");
        using var pasteur = await ConnecterAsync("pasteur@editeur-web.test");
        var plans = $"/organisations/{_organisation}/sites/{_site}/plans-batiment";
        using var premiere = await EnvoyerAsync(responsable, HttpMethod.Put, $"{plans}/{plan}", Version(0), await JetonAsync(responsable));
        var page = $"/organisations/{_organisation}/plans?batiment={lieux.Eglise}&niveau={lieux.Rdc}";

        var pourPasteur = await PageAsync(pasteur, page);
        var pourResponsable = await PageAsync(responsable, page);

        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode);
        Assert.Contains("Plan · Église · Rez-de-chaussée", pourPasteur, StringComparison.Ordinal);
        Assert.Contains("data-mode=\"atelier\"", pourPasteur, StringComparison.Ordinal);
        Assert.Contains("data-modifiable=\"false\"", pourPasteur, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"enregistrer\"", pourPasteur, StringComparison.Ordinal);
        Assert.DoesNotContain("data-panneau=\"calibration\"", pourPasteur, StringComparison.Ordinal);
        Assert.Contains("data-action=\"enregistrer\"", pourResponsable, StringComparison.Ordinal);
        Assert.Contains("data-panneau=\"calibration\"", pourResponsable, StringComparison.Ordinal);
        Assert.DoesNotContain("data-action=\"calque\"", pourResponsable, StringComparison.Ordinal); // sous-sol sans version : pas de calque
    }

    [Fact]
    public async Task TheEditorRelaysCreatesSavesAndExplainsAConflict()
    {
        var lieux = await LieuxAsync();
        using var responsable = await ConnecterAsync("responsable@editeur-web.test");
        using var pasteur = await ConnecterAsync("pasteur@editeur-web.test");
        using var anonyme = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var plans = $"/organisations/{_organisation}/sites/{_site}/plans-batiment";
        var jeton = await JetonAsync(responsable);

        using var cree = await EnvoyerAsync(responsable, HttpMethod.Post, plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, jeton);
        var plan = (await cree.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var existant = await EnvoyerAsync(responsable, HttpMethod.Post, plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, jeton);
        using var sansJeton = await EnvoyerAsync(responsable, HttpMethod.Put, $"{plans}/{plan}", Version(0), jeton: null);
        using var premiere = await EnvoyerAsync(responsable, HttpMethod.Put, $"{plans}/{plan}", Version(0), jeton);
        using var perimee = await EnvoyerAsync(responsable, HttpMethod.Put, $"{plans}/{plan}", Version(0), jeton);
        using var parLecteur = await EnvoyerAsync(pasteur, HttpMethod.Put, $"{plans}/{plan}", Version(1), await JetonAsync(pasteur));
        using var lecture = await responsable.GetAsync(new Uri($"{plans}/{plan}", UriKind.Relative));
        using var fonds = await responsable.GetAsync(new Uri($"{plans}/{plan}/fonds", UriKind.Relative));
        using var sansSession = await anonyme.GetAsync(new Uri($"{plans}/{plan}", UriKind.Relative));
        using var inconnu = await responsable.GetAsync(new Uri($"{plans}/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, cree.StatusCode);
        Assert.Contains("no-store", cree.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, existant.StatusCode);
        var dejaLa = await existant.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("plan_existant", plan), (dejaLa.GetProperty("code").GetString(), dejaLa.GetProperty("planId").GetGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode);
        Assert.Equal(1, (await premiere.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        var conflit = await perimee.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("version_depassee", conflit.GetProperty("code").GetString());
        Assert.Equal(1, conflit.GetProperty("versionCourante").GetInt32());
        Assert.Equal("Rose R.", conflit.GetProperty("auteur").GetString());
        Assert.NotEqual(JsonValueKind.Null, conflit.GetProperty("modifieLe").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, parLecteur.StatusCode);
        Assert.Equal(1, (await lecture.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());
        Assert.Contains("no-store", lecture.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Array, (await fonds.Content.ReadFromJsonAsync<JsonElement>()).ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, sansSession.StatusCode); // l'éditeur reçoit 401, pas la page de connexion
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
        Assert.Equal(PlansBatimentEndpoints.CorpsMax, EditeurPlanEndpoints.CorpsMax); // même limite de corps que l'API
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM patrimoine.versions_plan_batiment"));
    }

    [Fact]
    public async Task SigningOutAsksTheBrowserToClearLocalStorage()
    {
        using var responsable = await ConnecterAsync("responsable@editeur-web.test");
        using var contenu = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = await JetonAsync(responsable) });

        using var deconnexion = await responsable.PostAsync(new Uri("/deconnexion", UriKind.Relative), contenu);

        Assert.Equal(HttpStatusCode.Redirect, deconnexion.StatusCode);
        Assert.Equal("\"storage\"", deconnexion.Headers.GetValues("Clear-Site-Data").Single()); // brouillons de l'éditeur effacés
    }

    private static object Version(int version) => new { commandeId = Guid.NewGuid(), version, contenu = new { schema = 1 } };

    private static async Task<HttpResponseMessage> EnvoyerAsync(HttpClient navigateur, HttpMethod methode, string chemin, object corps, string? jeton)
    {
        using var demande = new HttpRequestMessage(methode, new Uri(chemin, UriKind.Relative)) { Content = JsonContent.Create(corps) };
        if (jeton is not null)
        {
            demande.Headers.Add("RequestVerificationToken", jeton); // comme l'éditeur : jeton lu dans la page, renvoyé en en-tête
        }

        return await navigateur.SendAsync(demande);
    }

    private static async Task<string> PageAsync(HttpClient navigateur, string chemin)
    {
        using var page = await navigateur.GetAsync(new Uri(chemin, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
    }

    private async Task<string> JetonAsync(HttpClient navigateur) =>
        Antifalsification().Match(await PageAsync(navigateur, $"/organisations/{_organisation}/inventaire")).Groups[1].Value;

    private static async Task<string> ConnexionApiAsync(HttpClient client, string email)
    {
        using var reponse = await client.PostAsJsonAsync("/auth/login", new { email, motDePasse = MotDePasse });
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jetonAcces").GetString()!;
    }

    private static async Task<Guid> CreerAsync(HttpClient client, string chemin, object corps, string jeton)
    {
        using var demande = new HttpRequestMessage(HttpMethod.Post, new Uri(chemin, UriKind.Relative)) { Content = JsonContent.Create(corps) };
        demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await client.SendAsync(demande);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // Église à deux niveaux (sous-sol, rez-de-chaussée), créée par l'API.
    private async Task<(Guid Eglise, Guid SousSol, Guid Rdc)> LieuxAsync()
    {
        using var client = _api.CreateClient();
        var inventaire = $"/organizations/{_organisation}/sites/{_site}/inventory";
        var technicien = await ConnexionApiAsync(client, "technicien@editeur-web.test");
        var eglise = await CreerAsync(client, $"{inventaire}/buildings", new { nom = "Église" }, technicien);
        var sousSol = await CreerAsync(client, $"{inventaire}/buildings/{eglise}/levels", new { nom = "Sous-sol", ordre = -1 }, technicien);
        var rdc = await CreerAsync(client, $"{inventaire}/buildings/{eglise}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        return (eglise, sousSol, rdc);
    }

    private async Task<Guid> PlanAsync(Guid niveau)
    {
        using var client = _api.CreateClient();
        var inventaire = $"/organizations/{_organisation}/sites/{_site}/inventory";
        var responsable = await ConnexionApiAsync(client, "responsable@editeur-web.test");
        using var structure = new HttpRequestMessage(HttpMethod.Get, new Uri($"{inventaire}/structure", UriKind.Relative));
        structure.Headers.Authorization = new AuthenticationHeaderValue("Bearer", responsable);
        using var lue = await client.SendAsync(structure);
        var batiment = (await lue.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single().GetProperty("id").GetGuid();
        return await CreerAsync(client, $"{inventaire}/building-plans", new { batimentId = batiment, niveauId = niveau }, responsable);
    }

    private async Task<HttpClient> ConnecterAsync(string email)
    {
        var navigateur = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        using var page = await navigateur.GetAsync(new Uri("/connexion", UriKind.Relative));
        var jeton = Antifalsification().Match(WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync())).Groups[1].Value;
        using var contenu = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = jeton,
            ["_handler"] = "connexion",
            ["Saisie.Email"] = email,
            ["Saisie.MotDePasse"] = MotDePasse,
        });
        using var connexion = await navigateur.PostAsync(new Uri("/connexion", UriKind.Relative), contenu);
        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
        return navigateur;
    }

    private async Task<Guid> MembreAsync(string email, string nom, Role role)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_organisation);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var compte = Compte.Nouveau(email, nom);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse)).Succeeded);
        var membre = new Membre(Guid.NewGuid(), _organisation, compte.Id, DateTimeOffset.UtcNow);
        var site = await context.Sites.SingleAsync();
        membre.Reviser([(role, site)]);
        membre.Accepter(DateTimeOffset.UtcNow);
        context.Add(membre);
        await context.SaveChangesAsync();
        return site.Id;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" (?:type=\"hidden\" )?value=\"([^\"]+)\"")]
    private static partial Regex Antifalsification();
}

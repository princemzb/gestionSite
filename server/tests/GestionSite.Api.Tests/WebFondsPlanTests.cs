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
using ImageMagick;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>
/// Relais des fonds de plan de bâtiment par l'application web (S21-04) : import avec jeton antifalsification en en-tête,
/// refus de l'API transmis, image servie avec son type, sans cache, aux seuls internes du site. Application web, API et
/// PostgreSQL réels ; images synthétiques.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed partial class WebFondsPlanTests(InfrastructureFixture infrastructure) : IAsyncLifetime
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
            .ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => _api.Server.CreateHandler())));
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(
            _api.Services, "Église Saint-Paul", "admin@fonds-web.test", "Claire M.", CancellationToken.None, premierSite: "Site centre");
        _organisation = ouverte.OrganisationId;
        _site = await MembreAsync("responsable@fonds-web.test", "Rose R.", Role.Responsable);
        await MembreAsync("pasteur@fonds-web.test", "Paul R.", Role.Pasteur);
        await MembreAsync("technicien@fonds-web.test", "Marc D.", Role.Technicien);
    }

    public async Task DisposeAsync()
    {
        await _web.DisposeAsync();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task AManagerImportsABackgroundThatInternalAccountsSeeWithoutCache()
    {
        var plan = await PlanAsync();
        using var responsable = await ConnecterAsync("responsable@fonds-web.test");
        using var pasteur = await ConnecterAsync("pasteur@fonds-web.test");
        using var anonyme = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var dessin = new MagickImage(MagickColors.White, 640, 480) { Format = MagickFormat.Png };
        var fonds = $"/organisations/{_organisation}/sites/{_site}/plans-batiment/{plan}/fonds";

        using var importe = await ImporterAsync(responsable, fonds, dessin.ToByteArray(), "plan.png", "image/png", await JetonAsync(responsable));
        var fond = await importe.Content.ReadFromJsonAsync<JsonElement>();
        var image = $"{fonds}/{fond.GetProperty("id").GetGuid()}";
        using var lecture = await pasteur.GetAsync(new Uri(image, UriKind.Relative));
        using var sansSession = await anonyme.GetAsync(new Uri(image, UriKind.Relative));
        using var inconnu = await pasteur.GetAsync(new Uri($"{fonds}/{Guid.NewGuid()}", UriKind.Relative));
        using var parLecteur = await ImporterAsync(pasteur, fonds, dessin.ToByteArray(), "plan.png", "image/png", await JetonAsync(pasteur));
        using var importSansSession = await ImporterAsync(anonyme, fonds, dessin.ToByteArray(), "plan.png", "image/png", jeton: null);

        Assert.Equal(HttpStatusCode.OK, importe.StatusCode);
        Assert.Contains("no-store", importe.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("image/png", fond.GetProperty("typeContenu").GetString());
        Assert.Equal(HttpStatusCode.OK, lecture.StatusCode);
        Assert.Equal("image/png", lecture.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", lecture.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", lecture.Headers.GetValues("X-Content-Type-Options").Single());
        using (var relue = new MagickImage(await lecture.Content.ReadAsByteArrayAsync()))
        {
            Assert.Equal((640u, 480u), (relue.Width, relue.Height));
        }

        Assert.Equal(HttpStatusCode.Redirect, sansSession.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parLecteur.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, importSansSession.StatusCode); // l'éditeur reçoit 401, pas la page de connexion
    }

    [Fact]
    public async Task WithoutTheAntiforgeryTokenNothingIsSentAndRefusalsAreExplained()
    {
        var plan = await PlanAsync();
        using var responsable = await ConnecterAsync("responsable@fonds-web.test");
        var fonds = $"/organisations/{_organisation}/sites/{_site}/plans-batiment/{plan}/fonds";
        var jeton = await JetonAsync(responsable);

        using var sansJeton = await ImporterAsync(responsable, fonds, FondsPlanBatimentTests.JpegAvecPosition(), "plan.jpg", "image/jpeg", jeton: null);
        using var svg = await ImporterAsync(responsable, fonds, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "plan.png", "image/png", jeton);
        using var sansFichier = await ImporterAsync(responsable, fonds, null, "plan.png", "image/png", jeton);
        using var tropGros = await ImporterAsync(responsable, fonds, new byte[(int)FondsPlanEndpoints.CorpsMax + 1], "plan.jpg", "image/jpeg", jeton);

        Assert.Equal(HttpStatusCode.BadRequest, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, svg.StatusCode);
        var refus = await svg.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("donnee_invalide", "fichier"), (refus.GetProperty("code").GetString(), refus.GetProperty("champ").GetString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sansFichier.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tropGros.StatusCode); // limite de corps de la route, avant tout envoi à l'API
        Assert.Equal(TraitementFonds.TailleMax + (1024 * 1024), FondsPlanEndpoints.CorpsMax); // alignée sur la limite de l'API
        Assert.Equal(0, await _database.CountAsync("SELECT count(*) FROM patrimoine.fonds_plan_batiment"));
    }

    private static async Task<HttpResponseMessage> ImporterAsync(
        HttpClient navigateur, string fonds, byte[]? contenu, string nom, string type, string? jeton)
    {
        using var formulaire = new MultipartFormDataContent { { new StringContent(Guid.NewGuid().ToString()), "commandeId" } };
        if (contenu is not null)
        {
            var fichier = new ByteArrayContent(contenu);
            fichier.Headers.ContentType = new MediaTypeHeaderValue(type);
            formulaire.Add(fichier, "fichier", nom);
        }

        using var demande = new HttpRequestMessage(HttpMethod.Post, new Uri(fonds, UriKind.Relative)) { Content = formulaire };
        if (jeton is not null)
        {
            demande.Headers.Add("RequestVerificationToken", jeton); // comme l'éditeur : jeton lu dans la page, renvoyé en en-tête
        }

        return await navigateur.SendAsync(demande);
    }

    // Jeton antifalsification de la session, lu dans une page (formulaire de déconnexion).
    private async Task<string> JetonAsync(HttpClient navigateur)
    {
        using var page = await navigateur.GetAsync(new Uri($"/organisations/{_organisation}/inventaire", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return Antifalsification().Match(WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync())).Groups[1].Value;
    }

    // Église à un niveau et son plan, créés par l'API.
    private async Task<Guid> PlanAsync()
    {
        using var client = _api.CreateClient();
        async Task<string> Connexion(string email)
        {
            using var reponse = await client.PostAsJsonAsync("/auth/login", new { email, motDePasse = MotDePasse });
            return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jetonAcces").GetString()!;
        }

        async Task<Guid> Creer(string chemin, object corps, string jeton)
        {
            using var demande = new HttpRequestMessage(HttpMethod.Post, new Uri(chemin, UriKind.Relative)) { Content = JsonContent.Create(corps) };
            demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
            using var reponse = await client.SendAsync(demande);
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
            return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var inventaire = $"/organizations/{_organisation}/sites/{_site}/inventory";
        var technicien = await Connexion("technicien@fonds-web.test");
        var eglise = await Creer($"{inventaire}/buildings", new { nom = "Église" }, technicien);
        var rdc = await Creer($"{inventaire}/buildings/{eglise}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        return await Creer($"{inventaire}/building-plans", new { batimentId = eglise, niveauId = rdc }, await Connexion("responsable@fonds-web.test"));
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

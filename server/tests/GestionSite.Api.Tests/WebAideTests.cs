using System.Net;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using GestionSite.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>Aide intégrée (DOC-02) de bout en bout : application web, API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed partial class WebAideTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string MotDePasse = "un-mot-de-passe-solide";
    private readonly string _livret = Path.Combine(Path.GetTempPath(), $"livret-{Guid.NewGuid():N}.pdf");
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private WebApplicationFactory<WebOptions> _web = null!;
    private Guid _organisation;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        using (_api.CreateClient())
        {
        }

        _web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .UseSetting("Web:Livret", _livret)
            .ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => _api.Server.CreateHandler())));
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(
            _api.Services, "Église Saint-Paul", "admin@aide-web.test", "Claire M.", CancellationToken.None, premierSite: "Site centre");
        _organisation = ouverte.OrganisationId;
        await MembreAsync("technicien@aide-web.test", "Marc D.", Role.Technicien);
    }

    public async Task DisposeAsync()
    {
        File.Delete(_livret);
        await _web.DisposeAsync();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task EachScreenOpensItsManualSectionInAPanel()
    {
        using var technicien = await ConnecterAsync("technicien@aide-web.test");
        using var reponse = await technicien.GetAsync(new Uri($"/organisations/{_organisation}/dossiers", UriKind.Relative));
        var dossiers = await LireAsync(reponse);
        var depots = await LirePageAsync(technicien, $"/organisations/{_organisation}/depots");

        Assert.Contains("<button type=\"button\" class=\"bouton-aide\" popovertarget=\"aide-panneau\">", dossiers, StringComparison.Ordinal);
        Assert.Contains("id=\"aide-panneau\" class=\"aide-panneau\" popover", dossiers, StringComparison.Ordinal);
        Assert.Contains("Aide · Chapitre 5, Dossiers", dossiers, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"aide-panneau-titre\">Suivre les dossiers d'un site</h2>", dossiers, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/aide/05-dossiers#dossiers-declarer\">Déclarer un problème</a>", dossiers, StringComparison.Ordinal);
        Assert.Contains("Lire tout le chapitre Dossiers", dossiers, StringComparison.Ordinal);
        Assert.DoesNotContain("Livret complet", dossiers, StringComparison.Ordinal); // livret absent : pas de lien mort
        Assert.DoesNotContain("<script", dossiers, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", reponse.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        // « Dépôts reçus » au menu des internes du site (décision du 03/11), écran courant marqué.
        Assert.Contains($"href=\"/organisations/{_organisation}/depots\">Dépôts reçus</a>", dossiers, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/depots\" aria-current=\"page\">Dépôts reçus</a>", depots, StringComparison.Ordinal);
        Assert.Contains("Recevoir les signalements (dépôts reçus)", depots, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/aide\">Aide</a>", dossiers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHelpPageListsTheTasksOfThePersonsRoleFirstAndSearchesTheManual()
    {
        using var technicien = await ConnecterAsync("technicien@aide-web.test");
        await File.WriteAllBytesAsync(_livret, "%PDF-1.7 livret"u8.ToArray());

        var aide = await LirePageAsync(technicien, $"/organisations/{_organisation}/aide");
        var recherche = await LirePageAsync(technicien, $"/organisations/{_organisation}/aide?q=cl%C3%B4turer");
        var rien = await LirePageAsync(technicien, $"/organisations/{_organisation}/aide?q=zzzintrouvable");

        Assert.Contains("Aide et manuel d'utilisation", aide, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/aide\" aria-current=\"page\">Aide</a>", aide, StringComparison.Ordinal);
        Assert.Matches("aide-role sien\" aria-label=\"Technicien\">\\s*<span class=\"aide-surtitre\">Votre rôle</span>", aide);
        Assert.True(aide.IndexOf("aria-label=\"Technicien\"", StringComparison.Ordinal) < aide.IndexOf("aria-label=\"Administrateur\"", StringComparison.Ordinal));
        Assert.Contains($"href=\"/organisations/{_organisation}/aide/05-dossiers#dossiers-mes-interventions\">Mes interventions</a>", aide, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/aide/12-faq\"><strong>12.</strong> Questions fréquentes</a>", aide, StringComparison.Ordinal);
        Assert.Contains("Télécharger le livret (PDF, 0,0 Mo)", aide, StringComparison.Ordinal);
        Assert.Contains("Résultats pour « clôturer »", recherche, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/aide/05-dossiers#dossiers-cloturer\">Clôturer une intervention</a>", recherche, StringComparison.Ordinal);
        Assert.Contains("Aucun résultat pour « zzzintrouvable »", rien, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChapterIsReadInFullWithItsAnchorsAndNeighbours()
    {
        using var technicien = await ConnecterAsync("technicien@aide-web.test");

        var chapitre = await LirePageAsync(technicien, $"/organisations/{_organisation}/aide/05-dossiers");
        var inconnu = await LirePageAsync(technicien, $"/organisations/{_organisation}/aide/99-inconnu");
        var sansOrganisation = await LirePageAsync(technicien, "/aide/10-workflows");

        Assert.Contains("<h1 id=\"aide-chapitre-titre\">Dossiers</h1>", chapitre, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"dossiers-qualifier\">Qualifier un dossier</h2>", chapitre, StringComparison.Ordinal);
        Assert.Contains("src=\"/aide/images/dossier-qualifier.png\"", chapitre, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/aide/04-depot-public\">← Dépôt public par QR code</a>", chapitre, StringComparison.Ordinal);
        Assert.Contains($"href=\"/organisations/{_organisation}/aide/06-inventaire\">Inventaire →</a>", chapitre, StringComparison.Ordinal);
        Assert.Contains("Chapitre introuvable", inconnu, StringComparison.Ordinal);
        Assert.Contains("Le schéma de ce cycle figure dans le livret PDF du manuel.", sansOrganisation, StringComparison.Ordinal);
        Assert.Contains("href=\"/aide/05-dossiers#dossiers\"", sansOrganisation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CapturesArePublicAndTheBookletIsReservedToMembers()
    {
        using var anonyme = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var technicien = await ConnecterAsync("technicien@aide-web.test");

        using var capture = await anonyme.GetAsync(new Uri("/aide/images/connexion.png", UriKind.Relative));
        using var absente = await anonyme.GetAsync(new Uri("/aide/images/inconnue.png", UriKind.Relative));
        using var traversee = await anonyme.GetAsync(new Uri("/aide/images/..%2Fappsettings.json", UriKind.Relative));
        using var livretAnonyme = await anonyme.GetAsync(new Uri("/aide/livret.pdf", UriKind.Relative));
        using var aideAnonyme = await anonyme.GetAsync(new Uri("/aide", UriKind.Relative));
        using var livretAbsent = await technicien.GetAsync(new Uri("/aide/livret.pdf", UriKind.Relative));
        await File.WriteAllBytesAsync(_livret, "%PDF-1.7 livret"u8.ToArray());
        using var livret = await technicien.GetAsync(new Uri("/aide/livret.pdf", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, capture.StatusCode);
        Assert.Equal("image/png", capture.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], (await capture.Content.ReadAsByteArrayAsync())[..4]);
        Assert.Equal(HttpStatusCode.NotFound, absente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, traversee.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, livretAnonyme.StatusCode);
        Assert.StartsWith("/connexion", livretAnonyme.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, aideAnonyme.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, livretAbsent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, livret.StatusCode);
        Assert.Equal("application/pdf", livret.Content.Headers.ContentType?.MediaType);
        Assert.Equal("manuel-gestionsite.pdf", livret.Content.Headers.ContentDisposition?.FileNameStar ?? livret.Content.Headers.ContentDisposition?.FileName);
    }

    [Fact]
    public async Task TheSignInScreensHaveTheirHelpWithoutLinksReservedToMembers()
    {
        using var anonyme = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var connexion = await LirePageAsync(anonyme, "/connexion");
        var activation = await LirePageAsync(anonyme, "/activation?jeton=x");

        Assert.Contains("<h2 id=\"aide-panneau-titre\">Se connecter</h2>", connexion, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"aide-panneau-titre\">Activer son compte</h2>", activation, StringComparison.Ordinal);
        Assert.DoesNotContain("Lire tout le chapitre", connexion, StringComparison.Ordinal);
        Assert.DoesNotContain("Sur cet écran aussi", connexion, StringComparison.Ordinal);
    }

    private async Task MembreAsync(string email, string nom, Role role)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_organisation);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var compte = Compte.Nouveau(email, nom);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse)).Succeeded);
        var membre = new Membre(Guid.NewGuid(), _organisation, compte.Id, DateTimeOffset.UtcNow);
        membre.Reviser([(role, await context.Sites.SingleAsync())]);
        membre.Accepter(DateTimeOffset.UtcNow);
        context.Add(membre);
        await context.SaveChangesAsync();
    }

    private async Task<HttpClient> ConnecterAsync(string email)
    {
        var navigateur = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var html = await LirePageAsync(navigateur, "/connexion");
        using var contenu = new FormUrlEncodedContent([
            new("__RequestVerificationToken", Antifalsification().Match(html).Groups[1].Value), new("_handler", "connexion"),
            new("Saisie.Email", email), new("Saisie.MotDePasse", MotDePasse)]);
        using var connexion = await navigateur.PostAsync(new Uri("/connexion", UriKind.Relative), contenu);
        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
        return navigateur;
    }

    private static async Task<string> LirePageAsync(HttpClient navigateur, string page)
    {
        using var reponse = await navigateur.GetAsync(new Uri(page, UriKind.Relative));
        return await LireAsync(reponse);
    }

    private static async Task<string> LireAsync(HttpResponseMessage reponse)
    {
        var corps = WebUtility.HtmlDecode(await reponse.Content.ReadAsStringAsync());
        Assert.True(reponse.StatusCode == HttpStatusCode.OK, $"{reponse.StatusCode} : {corps[..Math.Min(corps.Length, 2000)]}");
        return corps;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" (?:type=\"hidden\" )?value=\"([^\"]+)\"")]
    private static partial Regex Antifalsification();
}

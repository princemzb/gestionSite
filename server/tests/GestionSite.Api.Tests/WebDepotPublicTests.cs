using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.SharedKernel;
using GestionSite.Web;
using ImageMagick;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Formulaire public sur téléphone, accusé et écran Sites (US-08, S08-04) de bout en bout : vraie application web,
/// vraie API, PostgreSQL et stockage réels.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed partial class WebDepotPublicTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string AdminEmail = "admin@depot-web.test";
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private OrganisationOuverte _ouverte = null!;
    private string _code = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        using (_api.CreateClient())
        {
        }

        _ouverte = await IdentiteModule.OuvrirOrganisationAsync(
            _api.Services, "Église Saint-Paul", AdminEmail, "Claire M.", CancellationToken.None, premierSite: "Site centre");
        _code = (string)(await ScalarAsync("SELECT code FROM identite.codes_sites_publics"))!;
    }

    public async Task DisposeAsync()
    {
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task TheChoicePageNamesTheSiteAndOffersThreeChoices()
    {
        await using var web = Web(_api);
        using var visiteur = Navigateur(web);

        var page = await LirePageAsync(visiteur, $"/s/{_code}");

        Assert.Contains("Que voulez-vous signaler ?", page, StringComparison.Ordinal);
        Assert.Contains("Église Saint-Paul · Site centre", page, StringComparison.Ordinal);
        Assert.Contains($"/s/{_code}/signaler?choix=danger", page, StringComparison.Ordinal);
        Assert.Contains($"/s/{_code}/signaler?choix=demande", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/signaler")]
    public async Task AnUnknownLinkSaysSo(string suite)
    {
        await using var web = Web(_api);
        using var visiteur = Navigateur(web);

        var page = await LirePageAsync(visiteur, $"/s/ZZZZZZZZZZ{suite}");

        Assert.Contains("Ce lien n'est pas valable", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReportWithAPhotoIsAcknowledgedWithoutAnyFollowUp()
    {
        await using var web = Web(_api);
        using var visiteur = Navigateur(web);

        var accuse = await EnvoyerAsync(visiteur, "incident", "Le robinet goutte.", photo: true);

        Assert.Contains("Merci, c'est transmis.", accuse, StringComparison.Ordinal);
        Assert.Contains("vous recontacter au 06 00 00 00 00", accuse, StringComparison.Ordinal);
        Assert.Contains("Il n'y a pas de suivi en ligne.", accuse, StringComparison.Ordinal);
        Assert.Equal(1, await _database.CountAsync(
            "SELECT count(*) FROM maintenance.signalements WHERE nature = 'Incident' AND NOT danger AND description = 'Le robinet goutte.'"));
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.photos_signalement"));
    }

    [Fact]
    public async Task ADangerReportGivesTheSiteEmergencyNumber()
    {
        await using var web = Web(_api);
        using var visiteur = Navigateur(web);
        await ExecuterAsync("UPDATE identite.sites SET numero_urgence = '01 23 45 67 89'");

        var formulaire = await LirePageAsync(visiteur, $"/s/{_code}/signaler?choix=danger");
        var accuse = await EnvoyerAsync(visiteur, "danger", "Fuite au tableau électrique.", photo: false);

        Assert.Contains("Vous signalez un danger.", formulaire, StringComparison.Ordinal);
        Assert.Contains("Vous avez signalé un danger.", accuse, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:0123456789\"", accuse, StringComparison.Ordinal);
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.alertes_danger"));
    }

    [Fact]
    public async Task ARefusedFieldKeepsTheFormWithAnExplanation()
    {
        await using var web = Web(_api);
        using var visiteur = Navigateur(web);

        var page = await EnvoyerAsync(visiteur, "demande", "  ", photo: false);

        Assert.Contains("Décrivez ce qui se passe", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Merci, c'est transmis.", page, StringComparison.Ordinal);
        Assert.Equal(0, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements"));
    }

    [Fact]
    public async Task TooManyReportsFromOneAddressAreRefused()
    {
        await using var web = Web(_api, reglages: [("Web:LimiteDepotsParAdresse", "1")]);
        using var visiteur = Navigateur(web);

        var refuse = await EnvoyerAsync(visiteur, "incident", "  ", photo: false); // une erreur de saisie ne compte pas
        var premier = await EnvoyerAsync(visiteur, "incident", "Premier.", photo: false);
        var second = await EnvoyerAsync(visiteur, "incident", "Second.", photo: false);

        Assert.Contains("Décrivez ce qui se passe", refuse, StringComparison.Ordinal);
        Assert.Contains("Merci, c'est transmis.", premier, StringComparison.Ordinal);
        Assert.Contains("Trop d'envois depuis votre connexion", second, StringComparison.Ordinal);
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements"));
    }

    [Fact]
    public async Task TheAntiRobotWidgetIsShownAndItsTokenReachesTheApi()
    {
        var verification = new VerificationEnregistree();
        await using var api = _api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IVerificationHumaine>(_ => verification)));
        await using var web = Web(api, reglages: [("Web:CleSiteTurnstile", "1x00000000000000000000AA")]);
        using var visiteur = Navigateur(web);

        var formulaire = await LirePageAsync(visiteur, $"/s/{_code}/signaler");
        var accuse = await EnvoyerAsync(visiteur, "incident", "Vitre fêlée.", photo: false, ("cf-turnstile-response", "jeton-du-navigateur"));

        Assert.Contains("class=\"cf-turnstile\" data-sitekey=\"1x00000000000000000000AA\"", formulaire, StringComparison.Ordinal);
        Assert.Contains("https://challenges.cloudflare.com/turnstile/v0/api.js", formulaire, StringComparison.Ordinal);
        Assert.Contains("Merci, c'est transmis.", accuse, StringComparison.Ordinal);
        Assert.Equal(["jeton-du-navigateur"], verification.Jetons);
    }

    [Fact]
    public async Task TheAdministratorSeesTheQrAndPrintsThePosterOfEachSite()
    {
        await using var web = Web(_api);
        using var admin = await AdministrateurConnecteAsync(web);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        var page = $"/organisations/{_ouverte.OrganisationId}/sites";

        var sites = await LirePageAsync(admin, page);
        var affiche = await LirePageAsync(admin, $"{page}/{site}/affiche");
        using var qr = await admin.GetAsync(new Uri($"{page}/{site}/qr.png", UriKind.Relative));
        var octets = await qr.Content.ReadAsByteArrayAsync();
        var membres = await LirePageAsync(admin, $"/organisations/{_ouverte.OrganisationId}/membres");

        Assert.Contains("Site centre", sites, StringComparison.Ordinal);
        Assert.Contains($"/s/{_code}", sites, StringComparison.Ordinal);
        Assert.Contains("<svg", sites, StringComparison.Ordinal);
        Assert.Contains("Imprimer l'affiche", sites, StringComparison.Ordinal);
        Assert.Contains("Un problème ici ?", affiche, StringComparison.Ordinal);
        Assert.Contains($"/s/{_code}", affiche, StringComparison.Ordinal);
        Assert.Equal("image/png", qr.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], octets[..4]);
        Assert.Contains($"href=\"/organisations/{_ouverte.OrganisationId}/sites\"", membres, StringComparison.Ordinal); // navigation
    }

    [Fact]
    public async Task TheAdministratorSetsTheEmergencyNumberFromTheSitesScreen()
    {
        await using var web = Web(_api);
        using var admin = await AdministrateurConnecteAsync(web);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        var page = $"/organisations/{_ouverte.OrganisationId}/sites";

        using var enregistre = await PosterAsync(admin, page, formName: null, $"{page}/{site}/numero-urgence", ("numero", "01 23 45 67 89"));
        using var invalide = await PosterAsync(admin, page, formName: null, $"{page}/{site}/numero-urgence", ("numero", "appelez Paul"));
        var apres = await LirePageAsync(admin, page);

        var message = await LirePageAsync(admin, enregistre.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.Redirect, enregistre.StatusCode);
        Assert.Contains("Numéro enregistré.", message, StringComparison.Ordinal);
        Assert.EndsWith("numero=enregistre&site=" + site, enregistre.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.EndsWith("numero=invalide&site=" + site, invalide.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("value=\"01 23 45 67 89\"", apres, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AManagerSeesTheirSitesButCannotChangeTheEmergencyNumber()
    {
        await using var web = Web(_api);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        await MembreAsync("responsable@depot-web.test", Role.Responsable, site);
        using var responsable = await ConnecteAsync(web, "responsable@depot-web.test");
        var page = $"/organisations/{_ouverte.OrganisationId}/sites";

        using var accueil = await responsable.GetAsync(new Uri("/", UriKind.Relative));
        var sites = await LirePageAsync(responsable, page);
        using var refus = await PosterAsync(responsable, page, formName: null, $"{page}/{site}/numero-urgence", ("numero", "01 23 45 67 89"));
        var message = await LirePageAsync(responsable, refus.Headers.Location!.OriginalString);

        Assert.EndsWith($"/organisations/{_ouverte.OrganisationId}/dossiers", accueil.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Contains($"/s/{_code}", sites, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"numero\"", sites, StringComparison.Ordinal);
        Assert.DoesNotContain(">Membres<", sites, StringComparison.Ordinal);
        Assert.Contains("Seul l'administrateur modifie le numéro", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATechnicianHasNoPublicLinks()
    {
        await using var web = Web(_api);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        await MembreAsync("technicien@depot-web.test", Role.Technicien, site);
        using var technicien = await ConnecteAsync(web, "technicien@depot-web.test");
        var page = $"/organisations/{_ouverte.OrganisationId}/sites";

        var sites = await LirePageAsync(technicien, page);
        var affiche = await LirePageAsync(technicien, $"{page}/{site}/affiche");
        using var qr = await technicien.GetAsync(new Uri($"{page}/{site}/qr.png", UriKind.Relative));

        Assert.Contains("Sites introuvables", sites, StringComparison.Ordinal);
        Assert.Contains("Affiche introuvable", affiche, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, qr.StatusCode);
    }

    [Fact]
    public async Task AnUnknownSiteNumberIsReported()
    {
        await using var web = Web(_api);
        using var admin = await AdministrateurConnecteAsync(web);
        var page = $"/organisations/{_ouverte.OrganisationId}/sites";
        var inconnu = Guid.NewGuid();

        using var reponse = await PosterAsync(admin, page, formName: null, $"{page}/{inconnu}/numero-urgence", ("numero", "01 23 45 67 89"));
        using var qr = await admin.GetAsync(new Uri($"{page}/{inconnu}/qr.png", UriKind.Relative));

        Assert.EndsWith("numero=introuvable&site=" + inconnu, reponse.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, qr.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, null, "Ce site reçoit beaucoup de signalements")]
    [InlineData(HttpStatusCode.Conflict, null, "déjà été envoyé avec un autre contenu")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "photos", "Photos : 3 au plus")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "verification", "La vérification anti-robot n'a pas abouti")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "emplacement", "Indiquez où se trouve le problème")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "nom", "Indiquez votre nom")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "contact", "Indiquez un téléphone ou un e-mail")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "commandeId", "Le formulaire a expiré ou est incomplet")]
    [InlineData(HttpStatusCode.NotFound, null, "Ce lien n'est plus valable.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, "Service momentanément indisponible")]
    public async Task EachRefusalIsExplainedAndTheFormIsKept(HttpStatusCode statut, string? champ, string attendu)
    {
        await using var web = Web(_api, depot: () => new ReponseFixe(statut, champ));
        using var visiteur = Navigateur(web);

        var page = await EnvoyerAsync(visiteur, "incident", "Le robinet goutte.", photo: true);

        Assert.Contains(attendu, page, StringComparison.Ordinal);
        Assert.Contains("Le robinet goutte.", page, StringComparison.Ordinal); // saisie conservée
    }

    [Fact]
    public async Task AnUnreachableApiKeepsTheInputAndSaysSo()
    {
        await using var web = Web(_api, depot: () => new ReponseFixe(null, null));
        using var visiteur = Navigateur(web);

        var page = await EnvoyerAsync(visiteur, "incident", "Le robinet goutte.", photo: true);

        Assert.Contains("Service momentanément indisponible", page, StringComparison.Ordinal);
        Assert.Contains("joignez à nouveau vos photos", page, StringComparison.Ordinal);
        Assert.Contains("Le robinet goutte.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableApiIsExplainedOnThePublicPages()
    {
        await using var web = Web(_api, api: () => new ReponseFixe(null, null));
        using var visiteur = Navigateur(web);

        var choix = await LirePageAsync(visiteur, $"/s/{_code}");
        var formulaire = await LirePageAsync(visiteur, $"/s/{_code}/signaler");

        Assert.Contains("Service momentanément indisponible", choix, StringComparison.Ordinal);
        Assert.Contains("Service momentanément indisponible", formulaire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATechnicianReadsTheReportsOfTheirSiteWithTheirPhotos()
    {
        await using var web = Web(_api);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        await MembreAsync("tech-depots@depot-web.test", Role.Technicien, site);
        using (var visiteur = Navigateur(web))
        {
            await EnvoyerAsync(visiteur, "danger", "Fuite au plafond.\nSeau posé dessous.", photo: true);
        }

        using var technicien = await ConnecteAsync(web, "tech-depots@depot-web.test");
        using var accueil = await technicien.GetAsync(new Uri("/", UriKind.Relative));
        // Accueil : les dossiers (US-09) ; la liste des dépôts reçus reste à son adresse.
        Assert.EndsWith("/dossiers", accueil.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var page = await LirePageAsync(technicien, new Uri(accueil.Headers.Location!.OriginalString).PathAndQuery.Replace("/dossiers", "/depots", StringComparison.Ordinal));
        var image = PhotoDepot().Match(page).Groups[1].Value;
        using var photo = await technicien.GetAsync(new Uri(image, UriKind.Relative));

        Assert.Contains("Dépôts reçus", page, StringComparison.Ordinal);
        Assert.Contains("Danger signalé", page, StringComparison.Ordinal);
        Assert.Contains("Fuite au plafond.\nSeau posé dessous.", page, StringComparison.Ordinal);
        Assert.Contains("06 00 00 00 00", page, StringComparison.Ordinal);
        Assert.Contains("(non vérifié)", page, StringComparison.Ordinal);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store, private", string.Join(", ", photo.Headers.CacheControl!.ToString().Split(", ").Order()));
    }

    [Fact]
    public async Task AnAdministratorWithoutASiteRoleSeesNoReports()
    {
        await using var web = Web(_api);
        using var visiteur = Navigateur(web);
        await EnvoyerAsync(visiteur, "incident", "Vitre fêlée.", photo: true); // la photo existe : le 404 vient du refus
        using var admin = await AdministrateurConnecteAsync(web);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        var depot = (Guid)(await ScalarAsync("SELECT id FROM maintenance.signalements"))!;

        var page = await LirePageAsync(admin, $"/organisations/{_ouverte.OrganisationId}/depots?site={site}");
        using var photo = await admin.GetAsync(new Uri($"/organisations/{_ouverte.OrganisationId}/sites/{site}/depots/{depot}/photos/1", UriKind.Relative));

        Assert.Contains("Aucun site ne vous est attribué", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Vitre fêlée.", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, photo.StatusCode);
    }

    [Fact]
    public async Task TheApiPagesReportsAndRefusesOutsideTheCallersSites()
    {
        await using var web = Web(_api);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        await MembreAsync("pasteur@depot-web.test", Role.Pasteur, site);
        using (var visiteur = Navigateur(web))
        {
            foreach (var texte in new[] { "Premier.", "Deuxième.", "Troisième." })
            {
                await EnvoyerAsync(visiteur, "incident", texte, photo: false);
            }
        }

        using var client = _api.CreateClient();
        var jeton = await JetonApiAsync(client, "pasteur@depot-web.test");
        var chemin = $"/organizations/{_ouverte.OrganisationId}/sites/{site}/reports";
        var premiere = await LireJsonAsync<List<DepotLu>>(client, $"{chemin}?limite=2", jeton);
        var suite = await LireJsonAsync<List<DepotLu>>(client, $"{chemin}?limite=2&avant={premiere[1].Id}", jeton);
        using var autreSite = await EnvoyerAvecJetonAsync(client, $"/organizations/{_ouverte.OrganisationId}/sites/{Guid.NewGuid()}/reports", jeton);
        using var administrateur = await EnvoyerAvecJetonAsync(client, chemin, await JetonApiAsync(client, AdminEmail, activer: true));

        Assert.Equal(["Troisième.", "Deuxième."], premiere.Select(d => d.Description));
        Assert.Equal(["Premier."], suite.Select(d => d.Description));
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administrateur.StatusCode); // aucun rôle de site : aucun accès implicite
    }

    [Fact]
    public async Task AReaderOfSeveralSitesBrowsesEachSiteAndOlderPages()
    {
        await using var web = Web(_api);
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        var chapelle = Guid.NewGuid();
        await ExecuterAsync($"INSERT INTO identite.sites (id, organisation_id, nom, cree_le) VALUES ('{chapelle}', '{_ouverte.OrganisationId}', 'Chapelle nord', now())");
        await MembreAsync("coordination@depot-web.test", Role.Coordination, site, chapelle);
        using (var visiteur = Navigateur(web))
        {
            await EnvoyerAsync(visiteur, "incident", "Gouttière bouchée.", photo: false);
        }

        using var lecteur = await ConnecteAsync(web, "coordination@depot-web.test");
        var depot = (Guid)(await ScalarAsync("SELECT id FROM maintenance.signalements"))!;
        var page = $"/organisations/{_ouverte.OrganisationId}/depots";

        var centre = await LirePageAsync(lecteur, $"{page}?site={site}&depot={depot}");
        var nord = await LirePageAsync(lecteur, $"{page}?site={chapelle}");
        var horsPage = await LirePageAsync(lecteur, $"{page}?site={site}&depot={Guid.NewGuid()}");
        var anciens = await LirePageAsync(lecteur, $"{page}?site={site}&avant={depot}");

        Assert.Contains("Chapelle nord", centre, StringComparison.Ordinal); // onglets des deux sites
        Assert.Contains("Gouttière bouchée.", centre, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"true\"", centre, StringComparison.Ordinal);
        Assert.Contains("Aucun dépôt reçu pour Chapelle nord.", nord, StringComparison.Ordinal);
        Assert.Contains("Le dépôt demandé n'est pas dans cette page", horsPage, StringComparison.Ordinal);
        Assert.Contains("Les plus récents", anciens, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnApiOutageIsExplainedOnTheReportsPageAndForPhotos()
    {
        var site = (Guid)(await ScalarAsync("SELECT id FROM identite.sites"))!;
        await MembreAsync("panne@depot-web.test", Role.Technicien, site);
        var panne = new Interrupteur();
        await using var web = Web(_api, api: () => new PanneSur(_api.Server.CreateHandler(), panne, "/me", "/photos/"));
        using var technicien = await ConnecteAsync(web, "panne@depot-web.test");
        panne.Actif = true; // une fois connecté

        var page = await LirePageAsync(technicien, $"/organisations/{_ouverte.OrganisationId}/depots");
        using var photo = await technicien.GetAsync(new Uri(
            $"/organisations/{_ouverte.OrganisationId}/sites/{site}/depots/{Guid.NewGuid()}/photos/1", UriKind.Relative));

        Assert.Contains("Service momentanément indisponible", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Aucun site ne vous est attribué", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, photo.StatusCode);
        Assert.Contains("no-store", photo.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<WebOptions> Web(WebApplicationFactory<Program> api, params (string Cle, string Valeur)[] reglages) =>
        Web(api, depot: null, reglages);

    /// <summary><paramref name="depot"/> : réponse simulée de l'envoi ; <paramref name="api"/> : de toute l'API.</summary>
    private static WebApplicationFactory<WebOptions> Web(
        WebApplicationFactory<Program> vraie, Func<HttpMessageHandler>? depot = null, (string Cle, string Valeur)[]? reglages = null,
        Func<HttpMessageHandler>? api = null) =>
        new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Web:ApiUrl", "http://localhost");
            foreach (var (cle, valeur) in reglages ?? [])
            {
                builder.UseSetting(cle, valeur);
            }

            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(api ?? vraie.Server.CreateHandler);
                services.AddHttpClient("api-depot").ConfigurePrimaryHttpMessageHandler(depot ?? api ?? vraie.Server.CreateHandler);
            });
        });

    private async Task MembreAsync(string email, Role role, params Guid[] sites)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_ouverte.OrganisationId);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var compte = new Compte { Id = Guid.NewGuid(), UserName = email, Email = email };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse)).Succeeded);
        var membre = new Membre(Guid.NewGuid(), _ouverte.OrganisationId, compte.Id, DateTimeOffset.UtcNow);
        var entites = await context.Sites.Where(s => sites.Contains(s.Id)).ToListAsync();
        membre.Reviser([.. entites.Select(s => (role, (Site?)s))]);
        membre.Accepter(DateTimeOffset.UtcNow);
        context.Add(membre);
        await context.SaveChangesAsync();
    }

    private static async Task<HttpClient> ConnecteAsync(WebApplicationFactory<WebOptions> web, string email)
    {
        var navigateur = Navigateur(web);
        using var connexion = await PosterAsync(navigateur, "/connexion", "connexion", action: null, ("Saisie.Email", email), ("Saisie.MotDePasse", MotDePasse));
        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
        return navigateur;
    }

    private static HttpClient Navigateur(WebApplicationFactory<WebOptions> web) =>
        web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Comme un téléphone : ouvre le formulaire (jeton anti-falsification, commande), puis l'envoie en multipart.</summary>
    private async Task<string> EnvoyerAsync(
        HttpClient visiteur, string choix, string description, bool photo, params (string Nom, string Valeur)[] autres)
    {
        var page = $"/s/{_code}/signaler?choix={choix}";
        var formulaire = await LirePageAsync(visiteur, page);
        using var contenu = new MultipartFormDataContent
        {
            { new StringContent(Antifalsification().Match(formulaire).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent("signalement"), "_handler" },
            { new StringContent(Commande().Match(formulaire).Groups[1].Value), "Saisie.CommandeId" },
            { new StringContent(choix), "Saisie.Choix" },
            { new StringContent(description), "Saisie.Description" },
            { new StringContent("Toilettes, rez-de-chaussée"), "Saisie.Emplacement" },
            { new StringContent("Paul D."), "Saisie.Nom" },
            { new StringContent("06 00 00 00 00"), "Saisie.Contact" },
        };
        foreach (var (nom, valeur) in autres)
        {
            contenu.Add(new StringContent(valeur), nom);
        }

        if (photo)
        {
            using var image = new MagickImage(MagickColors.SteelBlue, 32, 24) { Format = MagickFormat.Jpeg };
            contenu.Add(new ByteArrayContent(image.ToByteArray()), "photos", "photo.jpg");
        }

        using var reponse = await visiteur.PostAsync(new Uri(page, UriKind.Relative), contenu);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return WebUtility.HtmlDecode(await reponse.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> AdministrateurConnecteAsync(WebApplicationFactory<WebOptions> web)
    {
        using (var activation = Navigateur(web))
        {
            await PosterAsync(activation, "/activation?jeton=x", "activation", action: null,
                ("Saisie.Jeton", _ouverte.JetonActivation!), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse));
        }

        var admin = Navigateur(web);
        using var connexion = await PosterAsync(admin, "/connexion", "connexion", action: null, ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", MotDePasse));
        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
        return admin;
    }

    private static async Task<HttpResponseMessage> PosterAsync(
        HttpClient navigateur, string page, string? formName, string? action, params (string Nom, string Valeur)[] champs)
    {
        var html = await LirePageAsync(navigateur, page);
        var valeurs = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", Antifalsification().Match(html).Groups[1].Value) };
        if (formName is not null)
        {
            valeurs.Add(new("_handler", formName));
        }

        valeurs.AddRange(champs.Select(c => new KeyValuePair<string, string>(c.Nom, c.Valeur)));
        using var contenu = new FormUrlEncodedContent(valeurs);
        return await navigateur.PostAsync(new Uri(action ?? page, UriKind.Relative), contenu);
    }

    private static async Task<string> LirePageAsync(HttpClient navigateur, string page)
    {
        using var reponse = await navigateur.GetAsync(new Uri(page, UriKind.Relative));
        var corps = WebUtility.HtmlDecode(await reponse.Content.ReadAsStringAsync());
        Assert.True(reponse.StatusCode == HttpStatusCode.OK, $"{reponse.StatusCode} : {corps[..Math.Min(corps.Length, 3000)]}");
        return corps;
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connexion = new NpgsqlConnection(_database.ConnectionString);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand(sql, connexion);
        return await commande.ExecuteScalarAsync();
    }

    private async Task ExecuterAsync(string sql)
    {
        await using var connexion = new NpgsqlConnection(_database.ConnectionString);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand(sql, connexion);
        await commande.ExecuteNonQueryAsync();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" (?:type=\"hidden\" )?value=\"([^\"]+)\"")]
    private static partial Regex Antifalsification();

    [GeneratedRegex("name=\"Saisie.CommandeId\" value=\"([0-9a-f-]{36})\"")]
    private static partial Regex Commande();

    /// <summary>API simulée : statut et champ fixes (null : injoignable). Lit le site pour la page, refuse le reste.</summary>
    private sealed class ReponseFixe(HttpStatusCode? statut, string? champ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (statut is not { } code)
            {
                throw new HttpRequestException("injoignable");
            }

            var corps = champ is null ? "{}" : $"{{\"code\":\"donnee_invalide\",\"champ\":\"{champ}\"}}";
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(corps, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private async Task<string> JetonApiAsync(HttpClient client, string email, bool activer = false)
    {
        if (activer)
        {
            using var activation = await client.PostAsJsonAsync("/auth/activate", new { jeton = _ouverte.JetonActivation, motDePasse = MotDePasse });
        }

        using var reponse = await client.PostAsJsonAsync("/auth/login", new { email, motDePasse = MotDePasse });
        return (await reponse.Content.ReadFromJsonAsync<JetonLu>())!.JetonAcces;
    }

    private static async Task<HttpResponseMessage> EnvoyerAvecJetonAsync(HttpClient client, string chemin, string jeton)
    {
        using var demande = new HttpRequestMessage(HttpMethod.Get, new Uri(chemin, UriKind.Relative));
        demande.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jeton);
        return await client.SendAsync(demande);
    }

    private static async Task<T> LireJsonAsync<T>(HttpClient client, string chemin, string jeton)
    {
        using var reponse = await EnvoyerAvecJetonAsync(client, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    [GeneratedRegex("<img src=\"(/organisations/[^\"]+/photos/1)\"")]
    private static partial Regex PhotoDepot();

    private sealed record JetonLu(string JetonAcces);

    private sealed record DepotLu(Guid Id, string Description);

    /// <summary>Vraie API, sauf les chemins indiqués qui répondent 503 (réveil, panne).</summary>
    private sealed class Interrupteur
    {
        public bool Actif { get; set; }
    }

    private sealed class PanneSur(HttpMessageHandler vraie, Interrupteur panne, params string[] chemins) : DelegatingHandler(vraie)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            panne.Actif && chemins.Any(c => request.RequestUri!.AbsolutePath.Contains(c, StringComparison.Ordinal))
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") })
                : base.SendAsync(request, cancellationToken);
    }

    private sealed class VerificationEnregistree : IVerificationHumaine
    {
        public List<string?> Jetons { get; } = [];

        public Task<bool> VerifierAsync(string? jeton, CancellationToken cancellationToken)
        {
            Jetons.Add(jeton);
            return Task.FromResult(true);
        }
    }
}

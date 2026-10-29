using System.Net;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Web;
using ImageMagick;
using Microsoft.AspNetCore.Mvc.Testing;
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
        await using var web = Web(_api, ("Web:LimiteDepotsParAdresse", "1"));
        using var visiteur = Navigateur(web);

        var premier = await EnvoyerAsync(visiteur, "incident", "Premier.", photo: false);
        var second = await EnvoyerAsync(visiteur, "incident", "Second.", photo: false);

        Assert.Contains("Merci, c'est transmis.", premier, StringComparison.Ordinal);
        Assert.Contains("Trop d'envois depuis votre connexion", second, StringComparison.Ordinal);
        Assert.Equal(1, await _database.CountAsync("SELECT count(*) FROM maintenance.signalements"));
    }

    [Fact]
    public async Task TheAntiRobotWidgetIsShownAndItsTokenReachesTheApi()
    {
        var verification = new VerificationEnregistree();
        await using var api = _api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IVerificationHumaine>(_ => verification)));
        await using var web = Web(api, ("Web:CleSiteTurnstile", "1x00000000000000000000AA"));
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

        Assert.Equal(HttpStatusCode.Redirect, enregistre.StatusCode);
        Assert.EndsWith("numero=enregistre&site=" + site, enregistre.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.EndsWith("numero=invalide&site=" + site, invalide.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("value=\"01 23 45 67 89\"", apres, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<WebOptions> Web(WebApplicationFactory<Program> api, params (string Cle, string Valeur)[] reglages) =>
        new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Web:ApiUrl", "http://localhost");
            foreach (var (cle, valeur) in reglages)
            {
                builder.UseSetting(cle, valeur);
            }

            builder.ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => api.Server.CreateHandler()));
        });

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

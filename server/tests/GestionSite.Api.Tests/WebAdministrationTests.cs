using System.Net;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite;
using GestionSite.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>
/// Application web (WEB-01) de bout en bout : vraie application web, vraie API, PostgreSQL réel.
/// Parcours ouverture → activation → connexion → membres → invitation → activation du membre.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed partial class WebAdministrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string AdminEmail = "admin@web.test";
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private WebApplicationFactory<WebOptions> _web = null!;
    private OrganisationOuverte _ouverte = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        using (_api.CreateClient())
        {
        }

        _web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost") // nom accepté par l'API (AllowedHosts)
            .ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => _api.Server.CreateHandler())));
        _ouverte = await IdentiteModule.OuvrirOrganisationAsync(
            _api.Services, "Église Saint-Paul", AdminEmail, "Claire M.", CancellationToken.None, premierSite: "Site centre");
    }

    public async Task DisposeAsync()
    {
        await _web.DisposeAsync();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task TheAdministratorActivatesThenLandsOnTheMembersScreen()
    {
        using var navigateur = Navigateur();

        var activation = await PosterAsync(navigateur, $"/activation?jeton={Uri.EscapeDataString(_ouverte.JetonActivation!)}", "activation",
            ("Saisie.Jeton", _ouverte.JetonActivation!), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse));
        var accueil = await navigateur.GetAsync(new Uri("/", UriKind.Relative));
        var membres = await LirePageAsync(navigateur, Chemin(accueil));

        Assert.Equal(HttpStatusCode.Redirect, activation.StatusCode);
        Assert.Equal($"/organisations/{_ouverte.OrganisationId}/membres", Chemin(accueil));
        Assert.Contains("Qui fait quoi, par site", membres, StringComparison.Ordinal);
        Assert.Contains("Claire M.", membres, StringComparison.Ordinal);
        Assert.Contains("Site centre", membres, StringComparison.Ordinal);
        Assert.Contains("Église Saint-Paul", membres, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvitationInThreeStepsGivesALinkThatActivatesTheNewMember()
    {
        using var admin = await AdministrateurConnecteAsync();
        var page = $"/organisations/{_ouverte.OrganisationId}/membres";
        var etape2 = await LireAsync(await PosterAsync(admin, page + "?inviter=1", "invitation",
            ("Saisie.Etape", "1"), ("Saisie.Nom", "Luc B."), ("Saisie.Email", "luc@web.test")));
        var siteId = SiteId().Match(etape2).Groups[1].Value;
        var etape3 = await LireAsync(await PosterAsync(admin, page, "invitation", ("Saisie.Etape", "2"), ("Saisie.Nom", "Luc B."),
            ("Saisie.Email", "luc@web.test"), ("Saisie.Roles[0].SiteId", siteId), ("Saisie.Roles[0].Role", "Technicien")));
        var lien = Lien().Match(etape3).Groups[1].Value;

        Assert.Contains("Rôles de Luc B. sur chaque site", etape2, StringComparison.Ordinal);
        Assert.Contains("Invitation créée pour Luc B.", etape3, StringComparison.Ordinal);
        Assert.Contains("Invitation en attente", etape3, StringComparison.Ordinal);
        using var luc = Navigateur();
        var jeton = Uri.UnescapeDataString(lien[(lien.IndexOf("jeton=", StringComparison.Ordinal) + 6)..]);
        var activation = await PosterAsync(luc, new Uri(lien).PathAndQuery, "activation",
            ("Saisie.Jeton", jeton), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse));
        Assert.Equal(HttpStatusCode.Redirect, activation.StatusCode);
        Assert.Contains("Votre compte est actif", await LirePageAsync(luc, "/"), StringComparison.Ordinal); // technicien : pas d'administration
        Assert.Contains("Actif", await LirePageAsync(admin, page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvitingTheSameAddressTwiceIsExplained()
    {
        using var admin = await AdministrateurConnecteAsync();
        var page = $"/organisations/{_ouverte.OrganisationId}/membres";

        var etape2 = await LireAsync(await PosterAsync(admin, page + "?inviter=1", "invitation",
            ("Saisie.Etape", "1"), ("Saisie.Nom", "Claire bis"), ("Saisie.Email", AdminEmail)));
        var siteId = SiteId().Match(etape2).Groups[1].Value;
        var refus = await LireAsync(await PosterAsync(admin, page, "invitation", ("Saisie.Etape", "2"), ("Saisie.Nom", "Claire bis"),
            ("Saisie.Email", AdminEmail), ("Saisie.Roles[0].SiteId", siteId), ("Saisie.Roles[0].Role", "Pasteur")));
        var sansRole = await LireAsync(await PosterAsync(admin, page, "invitation", ("Saisie.Etape", "2"), ("Saisie.Nom", "Luc B."),
            ("Saisie.Email", "luc@web.test"), ("Saisie.Roles[0].SiteId", siteId), ("Saisie.Roles[0].Role", "")));

        Assert.Contains("Cette personne est déjà membre ou invitée.", refus, StringComparison.Ordinal);
        Assert.Contains("Choisissez au moins un rôle", sansRole, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExpiringAccessTokenIsRenewedWithoutLoggingTheUserOut()
    {
        // Jeton d'accès de 30 s : chaque appel passe par le renouvellement (moins d'une minute restante).
        await using var apiCourte = _api.WithWebHostBuilder(b => b.UseSetting("Authentification:DureeJetonAcces", "00:00:30"));
        await using var web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => apiCourte.Server.CreateHandler())));
        using var navigateur = web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await PosterAsync(navigateur, "/activation?jeton=x", "activation",
            ("Saisie.Jeton", _ouverte.JetonActivation!), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse));

        for (var i = 0; i < 3; i++)
        {
            Assert.Contains("Qui fait quoi, par site", await LirePageAsync(navigateur, $"/organisations/{_ouverte.OrganisationId}/membres"), StringComparison.Ordinal);
        }

        // Deux onglets au même moment, même cookie : le renouvellement est partagé, pas pris pour un vol.
        var simultanes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            navigateur.GetAsync(new Uri($"/organisations/{_ouverte.OrganisationId}/membres", UriKind.Relative))));
        Assert.All(simultanes, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Contains("Qui fait quoi, par site", await LirePageAsync(navigateur, $"/organisations/{_ouverte.OrganisationId}/membres"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionsSurviveARestartWhenKeysAreKeptInObjectStorage()
    {
        // Deux instances successives (réveil du conteneur) partageant les clés dans le stockage S3.
        WebApplicationFactory<WebOptions> Instance() => _web.WithWebHostBuilder(builder => builder
            .UseSetting("Web:Cles:ServiceUrl", infrastructure.StorageUrl)
            .UseSetting("Web:Cles:AccessKey", InfrastructureFixture.AccessKey)
            .UseSetting("Web:Cles:SecretKey", InfrastructureFixture.SecretKey)
            .UseSetting("Web:Cles:Compartiment", InfrastructureFixture.Bucket)
            .UseSetting("Web:Cles:Region", InfrastructureFixture.Region));
        string cookie;
        await using (var avant = Instance())
        {
            using var navigateur = avant.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            using var activation = await PosterAsync(navigateur, "/activation?jeton=x", "activation",
                ("Saisie.Jeton", _ouverte.JetonActivation!), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse));
            Assert.Equal(HttpStatusCode.Redirect, activation.StatusCode);
            cookie = activation.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("gestionsite=", StringComparison.Ordinal)).Split(';')[0];
        }

        await using var apres = Instance();
        using var nouveau = apres.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var page = new HttpRequestMessage(HttpMethod.Get, new Uri($"/organisations/{_ouverte.OrganisationId}/membres", UriKind.Relative));
        page.Headers.Add("Cookie", cookie);
        using var reponse = await nouveau.SendAsync(page);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        // Preuve que les clés viennent bien du stockage S3 (et non du dossier local par défaut).
        using var s3 = GestionSite.Api.Infrastructure.ObjectStorageClientFactory.Create(
            infrastructure.StorageUrl, InfrastructureFixture.AccessKey, InfrastructureFixture.SecretKey, InfrastructureFixture.Region);
        var cles = await s3.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request { BucketName = InfrastructureFixture.Bucket, Prefix = "protection-donnees/web/" });
        Assert.Contains(cles.S3Objects ?? [], o => o.Key.EndsWith(".xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HealthEndpointReflectsTheApi()
    {
        using var navigateur = Navigateur();
        await using var enPanne = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => new ReseauEnPanne())));
        using var sansApi = enPanne.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await navigateur.GetAsync(new Uri("/sante", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await sansApi.GetAsync(new Uri("/sante", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AnApiStillStartingIsWaitedForInsteadOfFailing()
    {
        // Réveil du conteneur : l'API refuse d'abord les connexions, puis répond.
        await using var web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .ConfigureServices(services => services.AddHttpClient("api")
                .ConfigurePrimaryHttpMessageHandler(() => new ApiQuiDemarre(_api.Server.CreateHandler(), refus: 2))));
        await ActiverAdministrateurAsync();
        using var navigateur = web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        using var connexion = await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", MotDePasse));

        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
    }

    [Fact]
    public async Task IncompleteKeyStorageSettingsStopStartup()
    {
        await using var web = _web.WithWebHostBuilder(builder => builder.UseSetting("Web:Cles:ServiceUrl", "http://stockage.exemple"));

        var erreur = Assert.ThrowsAny<Exception>(() => web.CreateClient());

        Assert.Contains("Web:Cles incomplet", erreur.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableApiIsReportedAsUnavailable()
    {
        await using var web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => new ReseauEnPanne())));
        using var navigateur = web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        var page = await LireAsync(await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", MotDePasse)));

        Assert.Contains("Service momentanément indisponible.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogoutRequiresTheAntiforgeryToken()
    {
        using var admin = await AdministrateurConnecteAsync();

        using var sansJeton = await admin.PostAsync(new Uri("/deconnexion", UriKind.Relative), new FormUrlEncodedContent([]));
        var toujoursConnecte = await admin.GetAsync(new Uri($"/organisations/{_ouverte.OrganisationId}/membres", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.OK, toujoursConnecte.StatusCode);
    }

    [Fact]
    public async Task ActivationLinksUseTheConfiguredPublicAddress()
    {
        await using var web = _web.WithWebHostBuilder(builder => builder.UseSetting("Web:UrlPublique", "https://gestion.exemple.test/"));
        await ActiverAdministrateurAsync();
        using var admin = web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await PosterAsync(admin, "/connexion", "connexion", ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", MotDePasse));
        var page = $"/organisations/{_ouverte.OrganisationId}/membres";
        var etape2 = await LireAsync(await PosterAsync(admin, page + "?inviter=1", "invitation",
            ("Saisie.Etape", "1"), ("Saisie.Nom", "Luc B."), ("Saisie.Email", "luc@web.test")));

        var etape3 = await LireAsync(await PosterAsync(admin, page, "invitation", ("Saisie.Etape", "2"), ("Saisie.Nom", "Luc B."),
            ("Saisie.Email", "luc@web.test"), ("Saisie.Roles[0].SiteId", SiteId().Match(etape2).Groups[1].Value), ("Saisie.Roles[0].Role", "Technicien")));

        Assert.StartsWith("https://gestion.exemple.test/activation?jeton=", Lien().Match(etape3).Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownOrganisationShowsNoData()
    {
        using var admin = await AdministrateurConnecteAsync();

        var page = await LirePageAsync(admin, $"/organisations/{Guid.NewGuid()}/membres");

        Assert.Contains("Organisation introuvable", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPasswordThenLockoutAreExplained()
    {
        await ActiverAdministrateurAsync();
        using var navigateur = Navigateur();

        var faux = await LireAsync(await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", "mauvais")));
        for (var i = 0; i < 4; i++)
        {
            await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", "mauvais"));
        }

        var verrouille = await LireAsync(await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", MotDePasse)));

        Assert.Contains("E-mail ou mot de passe incorrect.", faux, StringComparison.Ordinal);
        Assert.Contains("Compte temporairement verrouillé.", verrouille, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReusedActivationLinkIsExplained()
    {
        await ActiverAdministrateurAsync();
        using var navigateur = Navigateur();

        var page = await LireAsync(await PosterAsync(navigateur, "/activation?jeton=x", "activation",
            ("Saisie.Jeton", _ouverte.JetonActivation!), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse)));
        var differents = await LireAsync(await PosterAsync(navigateur, "/activation?jeton=x", "activation",
            ("Saisie.Jeton", "x"), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", "autre-mot-de-passe")));

        Assert.Contains("Lien expiré ou déjà utilisé.", page, StringComparison.Ordinal);
        Assert.Contains("Les deux mots de passe ne sont pas identiques.", differents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PagesRequireASessionAndLogoutEndsIt()
    {
        using var anonyme = Navigateur();
        var sansSession = await anonyme.GetAsync(new Uri($"/organisations/{_ouverte.OrganisationId}/membres", UriKind.Relative));
        using var admin = await AdministrateurConnecteAsync();
        var page = await LirePageAsync(admin, $"/organisations/{_ouverte.OrganisationId}/membres");

        var deconnexion = await PosterAsync(admin, $"/organisations/{_ouverte.OrganisationId}/membres", formName: null, action: "/deconnexion");
        var apres = await admin.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, sansSession.StatusCode);
        Assert.StartsWith("/connexion", Chemin(sansSession), StringComparison.Ordinal);
        Assert.Contains("Se déconnecter", page, StringComparison.Ordinal);
        Assert.Equal("/connexion", Chemin(deconnexion));
        Assert.Equal(HttpStatusCode.Redirect, apres.StatusCode);
    }

    [Fact]
    public async Task TokensStayOnTheServerAndSecurityHeadersAreSent()
    {
        using var navigateur = Navigateur();
        var reponse = await PosterAsync(navigateur, $"/activation?jeton=x", "activation",
            ("Saisie.Jeton", _ouverte.JetonActivation!), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse));
        var cookie = reponse.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("gestionsite=", StringComparison.Ordinal));
        var page = await navigateur.GetAsync(new Uri($"/organisations/{_ouverte.OrganisationId}/membres", UriKind.Relative));
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJ", html, StringComparison.Ordinal); // aucun JWT dans la page
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
    }

    // Redirections parfois absolues (http://localhost/…) : on compare le chemin.
    private static string Chemin(HttpResponseMessage reponse)
    {
        var cible = reponse.Headers.Location!;
        return cible.IsAbsoluteUri ? cible.PathAndQuery : cible.OriginalString;
    }

    private HttpClient Navigateur() => _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private async Task ActiverAdministrateurAsync()
    {
        using var navigateur = Navigateur();
        await PosterAsync(navigateur, "/activation?jeton=x", "activation",
            ("Saisie.Jeton", _ouverte.JetonActivation!), ("Saisie.MotDePasse", MotDePasse), ("Saisie.Confirmation", MotDePasse));
    }

    private async Task<HttpClient> AdministrateurConnecteAsync()
    {
        await ActiverAdministrateurAsync();
        var navigateur = Navigateur();
        var connexion = await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", AdminEmail), ("Saisie.MotDePasse", MotDePasse));
        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
        return navigateur;
    }

    /// <summary>Comme un navigateur : lit la page (jeton anti-falsification), puis poste le formulaire.</summary>
    private static async Task<HttpResponseMessage> PosterAsync(
        HttpClient navigateur, string page, string? formName, params (string Nom, string Valeur)[] champs) =>
        await PosterAsync(navigateur, page, formName, action: null, champs);

    private static async Task<HttpResponseMessage> PosterAsync(
        HttpClient navigateur, string page, string? formName, string? action, params (string Nom, string Valeur)[] champs)
    {
        var html = await LirePageAsync(navigateur, page);
        var jeton = Antifalsification().Match(html).Groups[1].Value;
        var valeurs = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", jeton) };
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
        var corps = WebUtility.HtmlDecode(await reponse.Content.ReadAsStringAsync()); // texte tel qu'affiché (« É » encodé en entité)
        Assert.True(reponse.StatusCode == HttpStatusCode.OK, $"{reponse.StatusCode} : {corps[..Math.Min(corps.Length, 3000)]}");
        return corps;
    }

    private static async Task<string> LireAsync(HttpResponseMessage reponse)
    {
        using (reponse)
        {
            var corps = await reponse.Content.ReadAsStringAsync();
            Assert.True(reponse.StatusCode == HttpStatusCode.OK, $"{reponse.StatusCode} : {corps[..Math.Min(corps.Length, 4000)]}");
            return WebUtility.HtmlDecode(corps);
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" (?:type=\"hidden\" )?value=\"([^\"]+)\"")]
    private static partial Regex Antifalsification();

    [GeneratedRegex("name=\"Saisie.Roles\\[0\\].SiteId\" value=\"([0-9a-f-]{36})\"")]
    private static partial Regex SiteId();

    [GeneratedRegex("<code class=\"lien-activation\">([^<]+)</code>")]
    private static partial Regex Lien();
}

/// <summary>Réseau coupé : chaque appel échoue aussitôt, comme une API injoignable.</summary>
internal sealed class ReseauEnPanne : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("API injoignable (simulé).");
}

/// <summary>API au réveil : les premières connexions sont refusées (requête jamais partie), puis elle répond.</summary>
internal sealed class ApiQuiDemarre(HttpMessageHandler api, int refus) : DelegatingHandler(api)
{
    private int _refus = refus;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Interlocked.Decrement(ref _refus) >= 0
            ? throw new HttpRequestException("Connexion refusée (simulée).", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused))
            : base.SendAsync(request, cancellationToken);
}

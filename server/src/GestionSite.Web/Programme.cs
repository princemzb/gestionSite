using Amazon.Runtime;
using Amazon.S3;
using GestionSite.Web.Components;
using GestionSite.Web.Public;
using GestionSite.Web.Session;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;

namespace GestionSite.Web;

/// <summary>
/// Hôte web (Blazor, rendu serveur statique) : formulaires classiques, aucune connexion persistante
/// (compatible avec la mise en veille des conteneurs, ADR-08). Les jetons de l'API ne quittent jamais le
/// serveur web : ils sont rangés dans un cookie chiffré, inaccessible au JavaScript (HttpOnly).
/// </summary>
internal static class Programme
{
    internal const string ClientApi = "api";

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorComponents();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddOptions<WebOptions>().BindConfiguration(WebOptions.Section).ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.ApiUrl, UriKind.Absolute, out _), "Web:ApiUrl doit être une adresse absolue.")
            .ValidateOnStart();
        builder.Services.AddHttpClient(ClientApi, (provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WebOptions>>().Value.ApiUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<RenouvellementsPartages>();
        builder.Services.AddScoped<SessionApi>();
        builder.Services.AddScoped<DepotPublicClient>();
        builder.Services.AddHttpClient(DepotPublicClient.ClientDepot, (provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WebOptions>>().Value.ApiUrl);
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        builder.Services.AddSingleton<LimiteParAdresse>();
        builder.Services.AddSingleton<LiensPublics>();

        // Clés de chiffrement des cookies : à conserver entre redémarrages (sinon déconnexion de tous).
        var cles = builder.Services.AddDataProtection().SetApplicationName("gestionsite-web");
        var stockage = builder.Configuration.GetSection("Web:Cles").Get<StockageCles>();
        if (stockage is { ServiceUrl.Length: > 0 })
        {
            if (new[] { stockage.AccessKey, stockage.SecretKey, stockage.Compartiment }.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException("Web:Cles incomplet : AccessKey, SecretKey et Compartiment sont requis avec ServiceUrl.");
            }

            builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                new BasicAWSCredentials(stockage.AccessKey, stockage.SecretKey),
                new AmazonS3Config
                {
                    ServiceURL = stockage.ServiceUrl,
                    ForcePathStyle = true,
                    AuthenticationRegion = stockage.Region,
                    // Stockages compatibles S3 (R2, SeaweedFS) : pas d'envoi « aws-chunked » avec sommes de contrôle,
                    // qu'ils peuvent enregistrer tel quel et corrompre le contenu.
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                }));
            builder.Services.AddOptions<KeyManagementOptions>().Configure<IAmazonS3>((options, client) =>
                options.XmlRepository = new DepotClesS3(client, stockage.Compartiment, "protection-donnees/web/"));
        }
        else if (builder.Configuration["Web:DossierCles"] is { Length: > 0 } dossier)
        {
            cles.PersistKeysToFileSystem(new DirectoryInfo(dossier));
        }

        // Hors développement, toujours Secure : derrière un proxy TLS, la requête interne peut être en http.
        var securite = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/connexion";
            options.Cookie.Name = "gestionsite";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = securite;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromDays(14); // borne de la session : celle du jeton de renouvellement
        });
        builder.Services.AddAuthorization();
        // Par défaut, le cookie antiforgery n'est jamais Secure ; « Always » est exclu : l'antiforgery refuse alors
        // toute requête interne en http. Il suit donc le schéma d'origine (https derrière le relais, Web:RelaisHttps).
        builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
        var relaisHttps = builder.Configuration.GetValue<bool>("Web:RelaisHttps");
        if (relaisHttps)
        {
            // Le relais est le seul à joindre le conteneur : son adresse n'est pas connue à l'avance.
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        var app = builder.Build();
        if (relaisHttps)
        {
            app.UseForwardedHeaders();
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.Use(async (contexte, suite) =>
        {
            var entetes = contexte.Response.Headers;
            // Turnstile (formulaire public) : script et cadre de challenges.cloudflare.com seulement.
            entetes.ContentSecurityPolicy =
                "default-src 'self'; script-src 'self' https://challenges.cloudflare.com; frame-src https://challenges.cloudflare.com; " +
                "style-src 'self' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; " +
                "img-src 'self' data:; form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
            entetes.XContentTypeOptions = "nosniff";
            entetes["Referrer-Policy"] = "no-referrer"; // le jeton d'activation ne fuit pas par l'en-tête Referer
            await suite(contexte).ConfigureAwait(false);
        });
        // Formulaire public seulement : 3 photos de 10 Mo et le reste ; ailleurs, la limite de Kestrel (30 Mo).
        app.Use(async (contexte, suite) =>
        {
            if (HttpMethods.IsPost(contexte.Request.Method) && contexte.Request.Path.StartsWithSegments("/s")
                && contexte.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } taille)
            {
                taille.MaxRequestBodySize = 34 * 1024 * 1024;
            }

            await suite(contexte).ConfigureAwait(false);
        });
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapPost("/deconnexion", SessionApi.DeconnecterAsync).RequireAuthorization();
        app.MapSitesAdministration();
        // Disponibilité de l'ensemble (web + API interne) pour la plateforme et la vérification du déploiement.
        app.MapGet("/sante", async (IHttpClientFactory clients) =>
        {
            try
            {
                using var reponse = await clients.CreateClient(ClientApi).GetAsync(new Uri("/health/ready", UriKind.Relative), CancellationToken.None)
                    .ConfigureAwait(false);
                return reponse.IsSuccessStatusCode ? Results.Text("Disponible") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception erreur) when (erreur is HttpRequestException or TaskCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });
        app.MapRazorComponents<App>();
        await app.RunAsync().ConfigureAwait(false);
    }
}

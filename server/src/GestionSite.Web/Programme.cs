using GestionSite.Web.Components;
using GestionSite.Web.Session;
using Microsoft.AspNetCore.Authentication.Cookies;

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
        builder.Services.AddScoped<SessionApi>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/connexion";
            options.Cookie.Name = "gestionsite";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromDays(14); // borne de la session : celle du jeton de renouvellement
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();

        var app = builder.Build();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.Use(async (contexte, suite) =>
        {
            var entetes = contexte.Response.Headers;
            entetes.ContentSecurityPolicy =
                "default-src 'self'; style-src 'self' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; " +
                "img-src 'self' data:; form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
            entetes.XContentTypeOptions = "nosniff";
            entetes["Referrer-Policy"] = "no-referrer"; // le jeton d'activation ne fuit pas par l'en-tête Referer
            await suite(contexte).ConfigureAwait(false);
        });
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapPost("/deconnexion", SessionApi.DeconnecterAsync).RequireAuthorization();
        app.MapRazorComponents<App>();
        await app.RunAsync().ConfigureAwait(false);
    }
}

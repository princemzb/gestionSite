using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GestionSite.Web;
using GestionSite.Web.Session;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>
/// Session de l'application web : une page lit plusieurs ressources en parallèle (préventif) ; refusées ensemble (jeton
/// d'accès révoqué), elles ne renouvellent le jeton qu'une fois et le cookie de session n'est réécrit qu'une fois.
/// </summary>
public sealed class SessionApiTests
{
    [Fact]
    public async Task ParallelReadsRefusedTogetherRenewTheAccessTokenOnce()
    {
        var api = new ApiSimulee(renouvellementAccepte: true);
        var (fournisseur, contexte) = await ContexteAsync(api);
        await using var _ = fournisseur;
        using var session = ActivatorUtilities.CreateInstance<SessionApi>(fournisseur);

        var lectures = await Task.WhenAll(
            session.LireAsync<JsonElement>("/a", CancellationToken.None), session.LireAsync<JsonElement>("/b", CancellationToken.None));

        Assert.All(lectures, lecture => Assert.True(lecture.GetProperty("lu").GetBoolean()));
        Assert.Equal(1, api.Renouvellements);
        Assert.Single(contexte.Response.Headers.SetCookie, c => c!.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARefusedRenewalClosesTheSessionForEveryParallelRead()
    {
        var api = new ApiSimulee(renouvellementAccepte: false);
        var (fournisseur, contexte) = await ContexteAsync(api);
        await using var _ = fournisseur;
        using var session = ActivatorUtilities.CreateInstance<SessionApi>(fournisseur);
        var lectures = new[] { session.LireAsync<JsonElement>("/a", CancellationToken.None), session.LireAsync<JsonElement>("/b", CancellationToken.None) };

        await Assert.ThrowsAsync<SessionExpireeException>(() => Task.WhenAll(lectures));

        Assert.All(lectures, lecture => Assert.IsType<SessionExpireeException>(lecture.Exception?.InnerException));
        Assert.Equal(1, api.Renouvellements); // le jeton de renouvellement refusé n'est pas représenté
        Assert.Single(contexte.Response.Headers.SetCookie, c => c!.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
    }

    // Session d'une requête web dont le cookie porte un jeton d'accès que l'API refuse.
    private static async Task<(ServiceProvider Fournisseur, HttpContext Contexte)> ContexteAsync(ApiSimulee api)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddHttpClient(Programme.ClientApi, client => client.BaseAddress = new Uri("http://api")).ConfigurePrimaryHttpMessageHandler(() => api);
        services.AddMemoryCache();
        services.AddSingleton<RenouvellementsPartages>();
        services.AddSingleton(TimeProvider.System);
        var accesseur = new AccesseurFixe();
        services.AddSingleton<IHttpContextAccessor>(accesseur);
        var fournisseur = services.BuildServiceProvider();
        var cookie = await CookieAsync(fournisseur);
        var contexte = new DefaultHttpContext { RequestServices = fournisseur };
        contexte.Request.Headers.Cookie = cookie;
        accesseur.HttpContext = contexte;
        return (fournisseur, contexte);
    }

    // Cookie d'une session ouverte avec un jeton d'accès encore valide en apparence, mais que l'API refuse.
    private static async Task<string> CookieAsync(IServiceProvider fournisseur)
    {
        await using var portee = fournisseur.CreateAsyncScope(); // gestionnaires d'authentification propres à cette requête
        var connexion = new DefaultHttpContext { RequestServices = portee.ServiceProvider };
        var proprietes = new AuthenticationProperties();
        proprietes.StoreTokens([
            new AuthenticationToken { Name = "acces", Value = ApiSimulee.Revoque },
            new AuthenticationToken { Name = "renouvellement", Value = "renouvellement-1" },
            new AuthenticationToken { Name = "acces_expire", Value = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O") },
        ]);
        var identite = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], CookieAuthenticationDefaults.AuthenticationScheme);
        await connexion.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identite), proprietes);
        return connexion.Response.Headers.SetCookie.ToString().Split(';')[0];
    }

    // Contexte fixé pour toute la durée du test (l'accesseur standard suit le flux asynchrone de son affectation).
    private sealed class AccesseurFixe : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class ApiSimulee(bool renouvellementAccepte) : HttpMessageHandler
    {
        internal const string Revoque = "acces-revoque";
        private readonly TaskCompletionSource _deuxRefus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _refus;
        private int _renouvellements;

        internal int Renouvellements => _renouvellements;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/auth/refresh")
            {
                Interlocked.Increment(ref _renouvellements);
                if (!renouvellementAccepte)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }

                var maintenant = DateTimeOffset.UtcNow;
                var jetons = new JetonsApi($"acces-{_renouvellements}", maintenant.AddMinutes(15), $"renouvellement-{_renouvellements + 1}", maintenant.AddDays(14));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(jetons) };
            }

            if (request.Headers.Authorization?.Parameter == Revoque)
            {
                // Les deux lectures sont refusées avant tout renouvellement.
                if (Interlocked.Increment(ref _refus) == 2)
                {
                    _deuxRefus.SetResult();
                }

                await _deuxRefus.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { lu = true }) };
        }
    }
}

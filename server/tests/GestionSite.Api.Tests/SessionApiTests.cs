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
        var api = new ApiSimulee();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddHttpClient(Programme.ClientApi, client => client.BaseAddress = new Uri("http://api")).ConfigurePrimaryHttpMessageHandler(() => api);
        services.AddMemoryCache();
        services.AddSingleton<RenouvellementsPartages>();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<SessionApi>();
        await using var fournisseur = services.BuildServiceProvider();
        var cookie = await CookieAsync(fournisseur);

        await using var portee = fournisseur.CreateAsyncScope();
        var contexte = new DefaultHttpContext { RequestServices = portee.ServiceProvider };
        contexte.Request.Headers.Cookie = cookie;
        portee.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = contexte;
        var session = portee.ServiceProvider.GetRequiredService<SessionApi>();

        var lectures = await Task.WhenAll(
            session.LireAsync<JsonElement>("/a", CancellationToken.None), session.LireAsync<JsonElement>("/b", CancellationToken.None));

        Assert.All(lectures, lecture => Assert.True(lecture.GetProperty("lu").GetBoolean()));
        Assert.Equal(1, api.Renouvellements);
        Assert.Single(contexte.Response.Headers.SetCookie, c => c!.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
    }

    // Cookie d'une session ouverte avec un jeton d'accès encore valide en apparence, mais que l'API refuse.
    private static async Task<string> CookieAsync(IServiceProvider fournisseur)
    {
        var connexion = new DefaultHttpContext { RequestServices = fournisseur };
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

    private sealed class ApiSimulee : HttpMessageHandler
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

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Maintenance.Planificateur;

/// <summary>
/// Accès au point d'entrée interne du planificateur (section « Planificateur ») : un jeton partagé avec le Worker
/// (secret, au moins 32 caractères ; absent ou trop court, le point d'entrée n'existe pas : 404), et en préproduction un port dédié,
/// joignable par le seul Durable Object, sur lequel aucune autre route ne répond.
/// </summary>
public sealed class PlanificateurOptions
{
    public const string Section = "Planificateur";
    internal const int JetonMin = 32;

    /// <summary>Secret partagé avec le Worker (en-tête <c>X-Planificateur-Jeton</c>) ; vide ou trop court : planificateur désactivé.</summary>
    public string? Jeton { get; set; }

    /// <summary>Port local réservé au planificateur (0 : aucun port dédié, développement et tests).</summary>
    public int Port { get; set; }

    // Jeton trop court : planificateur inactif (404), sans empêcher le reste de l'API de démarrer.
    internal bool Active => Jeton is { Length: >= JetonMin };

    /// <summary>Jeton présenté identique (comparaison à temps constant, longueur comprise) et, si un port est dédié, reçu sur ce port.</summary>
    internal bool Autorise(HttpContext http)
    {
        if (!Active || (Port > 0 && http.Connection.LocalPort != Port))
        {
            return false;
        }

        var presentes = http.Request.Headers[PlanificateurEndpoints.EnteteJeton];
        return presentes.Count == 1
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(presentes[0] ?? string.Empty)), SHA256.HashData(Encoding.UTF8.GetBytes(Jeton!)));
    }
}

/// <summary>
/// Point d'entrée interne du planificateur (CONTRATS « Planificateur ») : jamais relayé par l'application web publique ;
/// tout refus est un 404 (rien n'indique qu'il existe).
/// </summary>
internal static class PlanificateurEndpoints
{
    internal const string Route = "/interne/planificateur/passage";
    internal const string EnteteJeton = "X-Planificateur-Jeton";

    internal static IEndpointRouteBuilder MapPlanificateur(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Route, async (HttpContext http, IOptions<PlanificateurOptions> options, ServicePlanificateur service, TimeProvider horloge, CancellationToken ct) =>
                options.Value.Autorise(http)
                    ? Results.Ok(await service.PasserAsync(horloge.GetUtcNow(), ct).ConfigureAwait(false))
                    : Results.NotFound())
            .AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }

    /// <summary>Sur le port dédié au planificateur, toute autre route répond 404 (le reste de l'API n'écoute qu'en local).</summary>
    internal static IApplicationBuilder RestreindrePortPlanificateur(this IApplicationBuilder app)
    {
        var port = app.ApplicationServices.GetRequiredService<IOptions<PlanificateurOptions>>().Value.Port;
        return port <= 0
            ? app
            : app.Use(async (http, suite) =>
            {
                if (http.Connection.LocalPort == port && !string.Equals(http.Request.Path.Value, Route, StringComparison.Ordinal))
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await suite(http).ConfigureAwait(false);
            });
    }
}

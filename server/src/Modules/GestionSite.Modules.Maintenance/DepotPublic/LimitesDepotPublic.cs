using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Maintenance.DepotPublic;

internal sealed class DepotPublicOptions
{
    internal const string Section = "DepotPublic";

    /// <summary>Dépôts acceptés par site et par fenêtre (filet derrière l'anti-robot ; limite par adresse côté web).</summary>
    public int LimiteParSite { get; init; } = 20;

    public TimeSpan Fenetre { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>Limite de dépôts par site (décision du 28/10) : un site inondé ne bloque pas les autres.</summary>
internal static class LimitesDepotPublic
{
    internal const string Politique = "depot-public";

    internal static IServiceCollection AddLimitesDepotPublic(this IServiceCollection services)
    {
        services.AddOptions<DepotPublicOptions>().BindConfiguration(DepotPublicOptions.Section)
            .Validate(o => o.LimiteParSite > 0 && o.Fenetre > TimeSpan.Zero, "DepotPublic : limite et fenêtre positives.")
            .ValidateOnStart();
        return services.AddRateLimiter(limites =>
        {
            limites.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limites.OnRejected = (rejet, _) => new ValueTask(Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests, title: "Trop de signalements pour ce site : réessayez plus tard.",
                extensions: new Dictionary<string, object?> { ["code"] = "trop_de_depots" }).ExecuteAsync(rejet.HttpContext));
            limites.AddPolicy(Politique, http =>
            {
                var reglage = http.RequestServices.GetRequiredService<IOptions<DepotPublicOptions>>().Value;
                var site = (http.Request.RouteValues["code"] as string ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal)
                    .ToUpperInvariant();
                return RateLimitPartition.GetFixedWindowLimiter(site, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = reglage.LimiteParSite,
                    Window = reglage.Fenetre,
                    QueueLimit = 0,
                });
            });
        });
    }
}

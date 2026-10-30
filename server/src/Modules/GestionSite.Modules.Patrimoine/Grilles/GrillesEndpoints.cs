using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Patrimoine.Grilles;

internal sealed record DemandeMesure(string? Libelle, string? Unite);

internal sealed record DemandeGrille(IReadOnlyList<string?>? Criteres, IReadOnlyList<DemandeMesure>? Mesures, int? Version);

internal sealed record VersionCreee(int Version);

/// <summary>Grilles d'évaluation du site (CONTRATS : grilles, S11-01).</summary>
internal static class GrillesEndpoints
{
    internal static IEndpointRouteBuilder MapGrilles(this IEndpointRouteBuilder endpoints)
    {
        var grilles = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/inventory/grids")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(StructureEndpoints.TraduireRefus);
        grilles.MapGet("/", async (Guid orgId, Guid siteId, ServiceGrilles service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, ct).ConfigureAwait(false)));
        grilles.MapGet("/{corpsEtat}", async (Guid orgId, Guid siteId, string corpsEtat, ServiceGrilles service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, corpsEtat, ct).ConfigureAwait(false)));
        grilles.MapGet("/{corpsEtat}/default", async (Guid orgId, Guid siteId, string corpsEtat, ServiceGrilles service, CancellationToken ct) =>
            Results.Ok(await service.ParDefautAsync(orgId, siteId, corpsEtat, ct).ConfigureAwait(false)));
        grilles.MapPut("/{corpsEtat}", async (Guid orgId, Guid siteId, string corpsEtat, DemandeGrille demande, ServiceGrilles service, CancellationToken ct) =>
            Results.Ok(new VersionCreee(await service.ModifierAsync(orgId, siteId, corpsEtat, demande, ct).ConfigureAwait(false))));
        return endpoints;
    }
}

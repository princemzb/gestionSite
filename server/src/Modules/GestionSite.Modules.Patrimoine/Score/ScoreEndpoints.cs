using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Patrimoine.Score;

/// <summary>État du site : note, couverture, fraîcheur, zones et historique (CONTRATS : score, S11-03).</summary>
internal static class ScoreEndpoints
{
    internal static IEndpointRouteBuilder MapScore(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/organizations/{orgId:guid}/sites/{siteId:guid}/inventory/score", async (
                Guid orgId, Guid siteId, ServiceScore service, CancellationToken ct) =>
                Results.Ok(await service.LireAsync(orgId, siteId, ct).ConfigureAwait(false)))
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(StructureEndpoints.TraduireRefus);
        return endpoints;
    }
}

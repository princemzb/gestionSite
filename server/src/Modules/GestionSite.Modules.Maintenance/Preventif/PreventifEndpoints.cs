using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.Preventif;

/// <summary>Préventif d'un site (CONTRATS : préventif, US-12).</summary>
internal static class PreventifEndpoints
{
    internal static IEndpointRouteBuilder MapPreventif(this IEndpointRouteBuilder endpoints)
    {
        var preventif = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/preventive")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire);
        preventif.MapGet("/proposals", async (Guid orgId, Guid siteId, ServicePropositions service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, ct).ConfigureAwait(false)));
        return endpoints;
    }
}

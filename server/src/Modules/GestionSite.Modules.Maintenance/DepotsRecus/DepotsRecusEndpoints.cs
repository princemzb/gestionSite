using GestionSite.Modules.Identite.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.DepotsRecus;

/// <summary>Dépôts reçus d'un site, en lecture (CONTRATS : dépôts reçus, S08-05).</summary>
internal static class DepotsRecusEndpoints
{
    internal static IEndpointRouteBuilder MapDepotsRecus(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/reports").RequireAuthorization().AddEndpointFilter(TraduireRefus);
        site.MapGet(string.Empty, async (Guid orgId, Guid siteId, Guid? avant, int? limite, ServiceDepotsRecus service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, avant, limite ?? 50, ct).ConfigureAwait(false)));
        site.MapGet("/{reportId:guid}/photos/{rang:int}", async (Guid orgId, Guid siteId, Guid reportId, int rang, ServiceDepotsRecus service, CancellationToken ct) =>
            await service.PhotoAsync(orgId, siteId, reportId, rang, ct).ConfigureAwait(false) is { } photo
                ? Results.File(photo, "image/jpeg")
                : Results.NotFound());
        return endpoints;
    }

    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (AccesDepotsRefuseException refus)
        {
            return refus.Decision == DecisionAutorisation.Refuse
                ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Action non permise.",
                    extensions: new Dictionary<string, object?> { ["code"] = "interdit" })
                : Results.NotFound();
        }
    }
}

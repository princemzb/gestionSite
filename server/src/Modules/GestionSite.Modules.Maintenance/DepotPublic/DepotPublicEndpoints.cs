using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.DepotPublic;

internal sealed record ReponseSitePublic(string Site, string Organisation);

/// <summary>Formulaire public sans compte (CONTRATS : POST /public/sites/{code}/reports).</summary>
internal static class DepotPublicEndpoints
{
    internal static IEndpointRouteBuilder MapDepotPublic(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.MapGroup("/public/sites/{code}").AllowAnonymous().AddEndpointFilter(TraduireInvalide);
        site.MapGet(string.Empty, async (string code, ServiceDepotPublic service, CancellationToken ct) =>
            await service.SiteAsync(code, ct).ConfigureAwait(false) is { } trouve
                ? Results.Ok(new ReponseSitePublic(trouve.NomSite, trouve.NomOrganisation))
                : Results.NotFound());
        site.MapPost("/reports", async (string code, DemandeDepot demande, ServiceDepotPublic service, CancellationToken ct) =>
            await service.DeposerAsync(code, demande, ct).ConfigureAwait(false) is { } accuse ? Results.Ok(accuse) : Results.NotFound());
        return endpoints;
    }

    private static async ValueTask<object?> TraduireInvalide(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (ArgumentException invalide) when (invalide.ParamName is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Donnée invalide.",
                extensions: new Dictionary<string, object?> { ["code"] = "donnee_invalide", ["champ"] = invalide.ParamName });
        }
    }
}

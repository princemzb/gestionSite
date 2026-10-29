using System.Text.Json;
using GestionSite.Modules.Maintenance.Domain;
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
        site.MapPost("/reports", async (string code, HttpRequest requete, ServiceDepotPublic service, CancellationToken ct) =>
            await service.DeposerAsync(code, await LireAsync(requete, ct).ConfigureAwait(false), ct).ConfigureAwait(false) is { } accuse
                ? Results.Ok(accuse)
                : Results.NotFound());
        return endpoints;
    }

    // Lu ici plutôt que lié par le framework : un corps illisible reçoit la même réponse 422 que les autres saisies.
    private static async Task<DemandeDepot> LireAsync(HttpRequest requete, CancellationToken cancellationToken)
    {
        try
        {
            return await requete.ReadFromJsonAsync<DemandeDepot>(cancellationToken).ConfigureAwait(false)
                ?? throw new SaisieInvalideException("corps", "Corps de requête manquant.");
        }
        catch (Exception illisible) when (illisible is JsonException or InvalidOperationException)
        {
            throw new SaisieInvalideException("corps", "Corps de requête illisible.");
        }
    }

    private static async ValueTask<object?> TraduireInvalide(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (SaisieInvalideException invalide)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Donnée invalide.",
                extensions: new Dictionary<string, object?> { ["code"] = "donnee_invalide", ["champ"] = invalide.Champ });
        }
        catch (CommandeReutiliseeException reutilisee)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: reutilisee.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "commande_reutilisee" });
        }
    }
}

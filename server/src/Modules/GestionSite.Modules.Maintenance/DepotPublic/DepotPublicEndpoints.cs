using GestionSite.SharedKernel.Photos;
using GestionSite.SharedKernel;
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
        var site = endpoints.MapGroup("/public/sites/{code}").AllowAnonymous().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(TraduireRefus);
        site.MapGet(string.Empty, async (string code, ServiceDepotPublic service, CancellationToken ct) =>
            await service.SiteAsync(code, ct).ConfigureAwait(false) is { } trouve
                ? Results.Ok(new ReponseSitePublic(trouve.NomSite, trouve.NomOrganisation))
                : Results.NotFound());
        site.MapPost("/reports", async (string code, HttpRequest requete, ServiceDepotPublic service, CancellationToken ct) =>
            {
                var (demande, photos) = await FormulaireAvecPhotos.LireAsync<DemandeDepot>(requete, ct).ConfigureAwait(false);
                return await service.DeposerAsync(code, demande, photos, ct).ConfigureAwait(false) is { } accuse
                    ? Results.Ok(accuse)
                    : Results.NotFound();
            })
            // 3 photos de 10 Mo et le reste du formulaire.
            .AvecPhotos();
        return endpoints;
    }


    // Refus propres au dépôt public ; saisie invalide : RefusCommuns, placé avant.
    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (TropDeDepotsException trop)
        {
            return RefusCommuns.Probleme(StatusCodes.Status429TooManyRequests, trop.Message, "trop_de_depots");
        }
        catch (DepotIndisponibleException indisponible)
        {
            return RefusCommuns.Probleme(StatusCodes.Status503ServiceUnavailable, indisponible.Message, "depot_indisponible");
        }
        catch (CommandeReutiliseeException reutilisee)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, reutilisee.Message, "commande_reutilisee");
        }
    }
}

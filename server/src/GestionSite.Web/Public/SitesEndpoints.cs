using GestionSite.Web.Session;
using Microsoft.AspNetCore.Antiforgery;

namespace GestionSite.Web.Public;

/// <summary>QR à télécharger et numéro d'urgence d'un site (écran Sites, S08-04) : droits vérifiés par l'API.</summary>
internal static class SitesEndpoints
{
    internal static IEndpointRouteBuilder MapSitesAdministration(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.MapGroup("/organisations/{organisationId:guid}/sites/{siteId:guid}").RequireAuthorization();
        site.MapGet("/qr.png", TelechargerQrAsync);
        site.MapPost("/numero-urgence", EnregistrerNumeroAsync);
        return endpoints;
    }

    private static async Task<IResult> TelechargerQrAsync(
        Guid organisationId, Guid siteId, SessionApi session, LiensPublics liens, HttpContext contexte)
    {
        try
        {
            var lien = (await session.LireAsync<List<LienPublicApi>>($"/organizations/{organisationId}/sites/public-links", CancellationToken.None)
                .ConfigureAwait(false)).FirstOrDefault(l => l.SiteId == siteId);
            return lien is null
                ? Results.NotFound()
                : Results.File(LiensPublics.Png(liens.Adresse(lien.Code, $"{contexte.Request.Scheme}://{contexte.Request.Host}{contexte.Request.PathBase}")),
                    "image/png", $"qr-{lien.Code}.png");
        }
        catch (SessionExpireeException)
        {
            return Results.LocalRedirect("/connexion");
        }
        catch (RefusApiException)
        {
            return Results.NotFound();
        }
    }

    // Formulaire de chaque carte de l'écran Sites ; jeton anti-falsification vérifié ici (point d'accès sans formulaire lié).
    private static async Task<IResult> EnregistrerNumeroAsync(
        Guid organisationId, Guid siteId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification)
    {
        if (!await antifalsification.IsRequestValidAsync(contexte).ConfigureAwait(false))
        {
            return Results.BadRequest();
        }

        var numero = (await contexte.Request.ReadFormAsync(CancellationToken.None).ConfigureAwait(false))["numero"].ToString();
        string issue;
        try
        {
            await session.ModifierAsync($"/organizations/{organisationId}/sites/{siteId}/emergency-number", new { numeroUrgence = numero }, CancellationToken.None)
                .ConfigureAwait(false);
            issue = "enregistre";
        }
        catch (SessionExpireeException)
        {
            return Results.LocalRedirect("/connexion");
        }
        catch (RefusApiException refus)
        {
            issue = refus.Statut switch
            {
                System.Net.HttpStatusCode.UnprocessableEntity => "invalide",
                System.Net.HttpStatusCode.Forbidden => "refuse",
                System.Net.HttpStatusCode.NotFound => "introuvable",
                _ => "indisponible",
            };
        }

        return Results.LocalRedirect($"/organisations/{organisationId}/sites?numero={issue}&site={siteId}");
    }
}

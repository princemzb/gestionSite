using GestionSite.Web.Session;
using Microsoft.AspNetCore.Antiforgery;

namespace GestionSite.Web.Dossiers;

/// <summary>
/// Actions d'un clic sur un dossier (démarrer, reprendre une intervention, rattacher à un doublon) : un bouton par
/// intervention ou par suggestion, posté ici puis renvoyé vers la fiche. Droits vérifiés par l'API ; jeton
/// anti-falsification vérifié ici (point d'accès sans formulaire lié).
/// </summary>
internal static class ActionsDossierEndpoints
{
    internal static IEndpointRouteBuilder MapActionsDossier(this IEndpointRouteBuilder endpoints)
    {
        var dossier = endpoints.MapGroup("/organisations/{organisationId:guid}/sites/{siteId:guid}/dossiers/{dossierId:guid}").RequireAuthorization();
        dossier.MapPost("/interventions/{numero:int}/demarrer", (Guid organisationId, Guid siteId, Guid dossierId, int numero, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            ExecuterAsync(organisationId, siteId, dossierId, $"interventions/{numero}/start", "interventions", null, session, contexte, antifalsification));
        dossier.MapPost("/interventions/{numero:int}/reprendre", (Guid organisationId, Guid siteId, Guid dossierId, int numero, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            ExecuterAsync(organisationId, siteId, dossierId, $"interventions/{numero}/resume", "interventions", null, session, contexte, antifalsification));
        dossier.MapPost("/rattacher", (Guid organisationId, Guid siteId, Guid dossierId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            ExecuterAsync(organisationId, siteId, dossierId, "duplicate-of", "constat", "principalId", session, contexte, antifalsification));
        // Photos après intervention : droits vérifiés par l'API, jamais mises en cache partagé.
        dossier.MapGet("/interventions/{numero:int}/photos/{rang:int}", async (Guid organisationId, Guid siteId, Guid dossierId, int numero, int rang, SessionApi session, HttpContext contexte) =>
        {
            contexte.Response.Headers.CacheControl = "private, no-store";
            try
            {
                var photo = await session.LireOctetsAsync(
                    $"/organizations/{organisationId}/sites/{siteId}/cases/{dossierId}/interventions/{numero}/photos/{rang}", CancellationToken.None).ConfigureAwait(false);
                return photo is null ? Results.NotFound() : Results.File(photo, "image/jpeg");
            }
            catch (SessionExpireeException)
            {
                return Results.LocalRedirect("/connexion");
            }
            catch (RefusApiException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });
        return endpoints;
    }

    private static async Task<IResult> ExecuterAsync(
        Guid organisationId, Guid siteId, Guid dossierId, string action, string onglet, string? champGuid, SessionApi session, HttpContext contexte,
        IAntiforgery antifalsification)
    {
        if (!await antifalsification.IsRequestValidAsync(contexte).ConfigureAwait(false))
        {
            return Results.BadRequest();
        }

        var formulaire = await contexte.Request.ReadFormAsync(CancellationToken.None).ConfigureAwait(false);
        var corps = new Dictionary<string, object?>
        {
            ["version"] = uint.TryParse(formulaire["version"], out var version) ? version : null,
        };
        if (champGuid is not null)
        {
            corps[champGuid] = Guid.TryParse(formulaire[champGuid], out var valeur) ? valeur : null;
        }

        string issue;
        try
        {
            await session.ExecuterAsync($"/organizations/{organisationId}/sites/{siteId}/cases/{dossierId}/{action}", corps, CancellationToken.None).ConfigureAwait(false);
            issue = "ok";
        }
        catch (SessionExpireeException)
        {
            return Results.LocalRedirect("/connexion");
        }
        catch (RefusApiException refus)
        {
            // Champ refusé (422) ou élément disparu (404) : la fiche dit pourquoi, pas « service indisponible ».
            issue = refus.Champ ?? refus.Statut switch
            {
                System.Net.HttpStatusCode.NotFound => "introuvable",
                System.Net.HttpStatusCode.Forbidden => "interdit",
                _ => refus.Code ?? "indisponible",
            };
        }

        return Results.LocalRedirect($"/organisations/{organisationId}/dossiers/{dossierId}?site={siteId}&onglet={onglet}&issue={Uri.EscapeDataString(issue)}");
    }
}

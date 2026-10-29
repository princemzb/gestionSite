using GestionSite.Web.Session;

namespace GestionSite.Web.Inventaire;

/// <summary>Photos des évaluations (US-11) : relayées depuis l'API, qui vérifie les droits ; jamais mises en cache partagé.</summary>
internal static class PhotosEvaluationEndpoints
{
    internal static IEndpointRouteBuilder MapPhotosEvaluation(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/organisations/{organisationId:guid}/sites/{siteId:guid}/composants/{composantId:guid}/evaluations/{evaluationId:guid}/photos/{rang:int}",
            async (Guid organisationId, Guid siteId, Guid composantId, Guid evaluationId, int rang, SessionApi session, HttpContext contexte) =>
            {
                contexte.Response.Headers.CacheControl = "private, no-store";
                try
                {
                    var photo = await session.LireOctetsAsync(
                        $"/organizations/{organisationId}/sites/{siteId}/inventory/components/{composantId}/evaluations/{evaluationId}/photos/{rang}",
                        CancellationToken.None).ConfigureAwait(false);
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
            }).RequireAuthorization();
        return endpoints;
    }
}

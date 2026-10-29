using GestionSite.Web.Session;

namespace GestionSite.Web.Inventaire;

/// <summary>Photos des évaluations (US-11) : relayées depuis l'API, qui vérifie les droits.</summary>
internal static class PhotosEvaluationEndpoints
{
    internal static IEndpointRouteBuilder MapPhotosEvaluation(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/organisations/{organisationId:guid}/sites/{siteId:guid}/composants/{composantId:guid}/evaluations/{evaluationId:guid}/photos/{rang:int}",
            (Guid organisationId, Guid siteId, Guid composantId, Guid evaluationId, int rang, SessionApi session, HttpContext contexte) =>
                RelaisPhoto.RelayerAsync(session, contexte,
                    $"/organizations/{organisationId}/sites/{siteId}/inventory/components/{composantId}/evaluations/{evaluationId}/photos/{rang}"))
            .RequireAuthorization();
        return endpoints;
    }
}

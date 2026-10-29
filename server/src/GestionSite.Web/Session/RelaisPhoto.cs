namespace GestionSite.Web.Session;

/// <summary>
/// Photo relayée depuis l'API, qui vérifie les droits (interventions, évaluations) : jamais mise en cache partagé ;
/// session expirée → connexion ; API indisponible → 503.
/// </summary>
internal static class RelaisPhoto
{
    internal static async Task<IResult> RelayerAsync(SessionApi session, HttpContext contexte, string cheminApi)
    {
        contexte.Response.Headers.CacheControl = "private, no-store";
        try
        {
            var photo = await session.LireOctetsAsync(cheminApi, CancellationToken.None).ConfigureAwait(false);
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
    }
}

namespace GestionSite.Web.Session;

/// <summary>
/// Photo (ou document) relayée depuis l'API, qui vérifie les droits (interventions, évaluations, contrôles) : jamais mise
/// en cache partagé ; session expirée → connexion ; API indisponible → 503.
/// </summary>
internal static class RelaisPhoto
{
    internal static Task<IResult> RelayerAsync(SessionApi session, HttpContext contexte, string cheminApi) =>
        RelayerAsync(session, contexte, cheminApi, "image/jpeg", nomTelechargement: null);

    /// <summary>
    /// Fichier relayé ; avec un nom de téléchargement, servi en pièce jointe, sans interprétation du type ni exécution
    /// dans l'origine de l'application (nosniff, CSP sandbox) : rapport PDF d'un contrôle.
    /// </summary>
    internal static async Task<IResult> RelayerAsync(SessionApi session, HttpContext contexte, string cheminApi, string typeContenu, string? nomTelechargement)
    {
        contexte.Response.Headers.CacheControl = "private, no-store";
        if (nomTelechargement is not null)
        {
            contexte.Response.Headers.XContentTypeOptions = "nosniff";
            contexte.Response.Headers.ContentSecurityPolicy = "sandbox";
        }

        try
        {
            var octets = await session.LireOctetsAsync(cheminApi, CancellationToken.None).ConfigureAwait(false);
            return octets is null ? Results.NotFound() : Results.File(octets, typeContenu, nomTelechargement);
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

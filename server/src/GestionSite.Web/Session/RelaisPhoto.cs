namespace GestionSite.Web.Session;

/// <summary>
/// Photo (ou document) relayée depuis l'API, qui vérifie les droits (interventions, évaluations, contrôles, fonds de plan) :
/// jamais mise en cache partagé ; session expirée → connexion ; API indisponible → 503.
/// </summary>
internal static class RelaisPhoto
{
    internal static Task<IResult> RelayerAsync(SessionApi session, HttpContext contexte, string cheminApi) =>
        RelayerAsync(session, contexte, cheminApi, "image/jpeg", nomTelechargement: null);

    /// <summary>
    /// Fichier relayé ; avec un nom de téléchargement, servi en pièce jointe, sans interprétation du type ni exécution
    /// dans l'origine de l'application (nosniff, CSP sandbox) : rapport PDF d'un contrôle.
    /// </summary>
    internal static Task<IResult> RelayerAsync(SessionApi session, HttpContext contexte, string cheminApi, string typeContenu, string? nomTelechargement)
    {
        contexte.Response.Headers.CacheControl = "private, no-store";
        if (nomTelechargement is not null)
        {
            contexte.Response.Headers.XContentTypeOptions = "nosniff";
            contexte.Response.Headers.ContentSecurityPolicy = "sandbox";
        }

        return AvecSessionAsync(async () =>
        {
            var octets = await session.LireOctetsAsync(cheminApi, CancellationToken.None).ConfigureAwait(false);
            return octets is null ? Results.NotFound() : Results.File(octets, typeContenu, nomTelechargement);
        });
    }

    /// <summary>
    /// Image dont le type vient de l'API (fond de plan de bâtiment : PNG ou JPEG) : jamais en cache, type jamais réinterprété
    /// (nosniff) ; tout autre type annoncé est servi comme données brutes, jamais comme document actif.
    /// </summary>
    internal static Task<IResult> RelayerImageAsync(SessionApi session, HttpContext contexte, string cheminApi)
    {
        contexte.Response.Headers.CacheControl = "private, no-store";
        contexte.Response.Headers.XContentTypeOptions = "nosniff";
        return AvecSessionAsync(async () =>
        {
            var fichier = await session.LireFichierAsync(cheminApi, CancellationToken.None).ConfigureAwait(false);
            return fichier is { } image
                ? Results.File(image.Contenu, image.TypeContenu is "image/png" or "image/jpeg" ? image.TypeContenu : "application/octet-stream")
                : Results.NotFound();
        });
    }

    private static async Task<IResult> AvecSessionAsync(Func<Task<IResult>> relais)
    {
        try
        {
            return await relais().ConfigureAwait(false);
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

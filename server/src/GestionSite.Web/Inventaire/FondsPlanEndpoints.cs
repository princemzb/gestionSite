using System.Text.Json;
using GestionSite.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace GestionSite.Web.Inventaire;

/// <summary>
/// Fonds de plan de bâtiment (US-21, S21-04), relayés vers l'API qui vérifie les droits et traite l'image. Import appelé
/// par l'éditeur (S21-07) : formulaire multipart (« commandeId », « fichier »), jeton antifalsification en en-tête
/// (« RequestVerificationToken ») ou dans le formulaire, réponse JSON ; lecture servie comme image, jamais en cache.
/// </summary>
internal static class FondsPlanEndpoints
{
    /// <summary>
    /// Un fichier de 25 Mo et le reste du formulaire (limite propre à cette route), comme l'API (TraitementFonds.TailleMax,
    /// hors de portée de l'application web : l'égalité est vérifiée par les tests).
    /// </summary>
    internal const long CorpsMax = (25 + 1) * 1024 * 1024;

    internal static IEndpointRouteBuilder MapFondsPlan(this IEndpointRouteBuilder endpoints)
    {
        const string Fonds = "/organisations/{organisationId:guid}/sites/{siteId:guid}/plans-batiment/{planId:guid}/fonds";
        endpoints.MapPost(Fonds, ImporterAsync).RequireAuthorization()
            .WithMetadata(new RequestSizeLimitAttribute(CorpsMax)).WithFormOptions(multipartBodyLengthLimit: CorpsMax);
        endpoints.MapGet($"{Fonds}/{{fondId:guid}}", (Guid organisationId, Guid siteId, Guid planId, Guid fondId, SessionApi session, HttpContext contexte) =>
                RelaisPhoto.RelayerImageAsync(session, contexte, $"{Api(organisationId, siteId, planId)}/{fondId}"))
            .RequireAuthorization();
        return endpoints;
    }

    /// <summary>Requête d'import d'un fond (POST …/plans-batiment/{plan}/fonds), qui attend du JSON et jamais une redirection.</summary>
    internal static bool EstImport(HttpRequest requete) =>
        HttpMethods.IsPost(requete.Method) && requete.Path.StartsWithSegments("/organisations", out var reste)
        && reste.Value!.Contains("/plans-batiment/", StringComparison.Ordinal) && reste.Value.EndsWith("/fonds", StringComparison.Ordinal);

    // Fichier lié par le framework : le jeton antifalsification est exigé avant tout envoi à l'API.
    private static async Task<IResult> ImporterAsync(
        Guid organisationId, Guid siteId, Guid planId, [FromForm] Guid? commandeId, IFormFile? fichier, SessionApi session, HttpContext contexte)
    {
        contexte.Response.Headers.CacheControl = "no-store";
        try
        {
            var fond = await session.EnvoyerFormulaireAsync<JsonElement>(
                Api(organisationId, siteId, planId), new { commandeId }, fichier is null ? [] : [fichier], "fichier", CancellationToken.None).ConfigureAwait(false);
            return Results.Ok(fond);
        }
        catch (SessionExpireeException)
        {
            return Results.Unauthorized();
        }
        catch (RefusApiException refus)
        {
            // Refus de l'API transmis tel quel (statut, code stable, champ) : l'éditeur l'explique à l'utilisateur.
            return Results.Problem(statusCode: (int)refus.Statut, title: refus.Message, extensions: new Dictionary<string, object?>
            {
                ["code"] = refus.Code,
                ["champ"] = refus.Champ,
            });
        }
    }

    private static string Api(Guid organisationId, Guid siteId, Guid planId) =>
        $"/organizations/{organisationId}/sites/{siteId}/inventory/building-plans/{planId}/backgrounds";
}

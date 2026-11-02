using System.Text.Json;
using GestionSite.Web.Session;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace GestionSite.Web.Inventaire;

internal sealed record DemandeCreationPlan(Guid? BatimentId, Guid? NiveauId);

/// <summary>
/// Relais JSON de l'éditeur de plans de bâtiment (US-21, S21-07, ADR-11) : lecture d'un plan et de ses fonds, création du
/// plan d'un niveau et enregistrement d'une version, vers l'API qui vérifie les droits et la géométrie. Écritures avec le
/// jeton antifalsification en en-tête (« RequestVerificationToken ») ; jamais en cache ; sans session : 401, jamais la page
/// de connexion ; refus de l'API transmis avec leur statut, leur code, le champ fautif et, pour un conflit, la version
/// gagnante.
/// </summary>
internal static class EditeurPlanEndpoints
{
    /// <summary>Corps d'un enregistrement : celui de l'API (PlansBatimentEndpoints.CorpsMax, vérifié par les tests).</summary>
    internal const long CorpsMax = 4 * 1024 * 1024;

    // Membres d'un refus de l'API que l'éditeur utilise (conflit de version, plan déjà créé, page absente d'un PDF).
    private static readonly string[] DetailsTransmis = ["versionCourante", "auteur", "modifieLe", "planId", "pages"];

    internal static IEndpointRouteBuilder MapEditeurPlan(this IEndpointRouteBuilder endpoints)
    {
        const string Plans = "/organisations/{organisationId:guid}/sites/{siteId:guid}/plans-batiment";
        endpoints.MapPost(Plans, (Guid organisationId, Guid siteId, DemandeCreationPlan demande, SessionApi session, IAntiforgery antifalsification, HttpContext contexte) =>
                RelayerAsync(contexte, antifalsification, () => session.EcrireAsync<JsonElement>(
                    $"{Api(organisationId, siteId)}", new { batimentId = demande.BatimentId, niveauId = demande.NiveauId }, CancellationToken.None)))
            .RequireAuthorization();
        endpoints.MapGet($"{Plans}/{{planId:guid}}", (Guid organisationId, Guid siteId, Guid planId, SessionApi session, HttpContext contexte) =>
                RelayerAsync(contexte, null, () => session.LireAsync<JsonElement>($"{Api(organisationId, siteId)}/{planId}", CancellationToken.None)))
            .RequireAuthorization();
        endpoints.MapPut($"{Plans}/{{planId:guid}}", (Guid organisationId, Guid siteId, Guid planId, JsonElement corps, SessionApi session, IAntiforgery antifalsification, HttpContext contexte) =>
                RelayerAsync(contexte, antifalsification, () => session.ModifierAvecReponseAsync<JsonElement>(
                    $"{Api(organisationId, siteId)}/{planId}", corps, CancellationToken.None)))
            .RequireAuthorization().WithMetadata(new RequestSizeLimitAttribute(CorpsMax));
        endpoints.MapGet($"{Plans}/{{planId:guid}}/fonds", (Guid organisationId, Guid siteId, Guid planId, SessionApi session, HttpContext contexte) =>
                RelayerAsync(contexte, null, () => session.LireAsync<JsonElement>($"{Api(organisationId, siteId)}/{planId}/backgrounds", CancellationToken.None)))
            .RequireAuthorization();
        return endpoints;
    }

    /// <summary>
    /// Appel de l'éditeur (fetch, réponse JSON attendue) : toute route sous …/plans-batiment, sauf l'image d'un fond
    /// (GET …/fonds/{fond}), affichée par le navigateur et renvoyée vers la connexion sans session.
    /// </summary>
    internal static bool EstAppelEditeur(HttpRequest requete)
    {
        if (!requete.Path.StartsWithSegments("/organisations", out var reste) || !reste.Value!.Contains("/plans-batiment", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = reste.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var imageDeFond = HttpMethods.IsGet(requete.Method) && segments.Length >= 2 && segments[^2] == "fonds";
        return !imageDeFond;
    }

    private static async Task<IResult> RelayerAsync(HttpContext contexte, IAntiforgery? antifalsification, Func<Task<JsonElement>> appel)
    {
        contexte.Response.Headers.CacheControl = "no-store";
        // Corps JSON : le framework ne vérifie pas le jeton antifalsification d'office (il le fait pour les formulaires).
        if (antifalsification is not null && !await antifalsification.IsRequestValidAsync(contexte).ConfigureAwait(false))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Jeton antifalsification absent ou invalide.",
                extensions: new Dictionary<string, object?> { ["code"] = "jeton_invalide" });
        }

        try
        {
            return Results.Ok(await appel().ConfigureAwait(false));
        }
        catch (SessionExpireeException)
        {
            return Results.Unauthorized();
        }
        catch (RefusApiException refus)
        {
            var extensions = new Dictionary<string, object?> { ["code"] = refus.Code, ["champ"] = refus.Champ };
            foreach (var cle in DetailsTransmis)
            {
                if (refus.Details.TryGetValue(cle, out var valeur))
                {
                    extensions[cle] = valeur;
                }
            }

            return Results.Problem(statusCode: (int)refus.Statut, title: refus.Message, extensions: extensions);
        }
    }

    private static string Api(Guid organisationId, Guid siteId) => $"/organizations/{organisationId}/sites/{siteId}/inventory/building-plans";
}

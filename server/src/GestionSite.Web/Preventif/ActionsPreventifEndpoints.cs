using GestionSite.Web.Session;
using Microsoft.AspNetCore.Antiforgery;

namespace GestionSite.Web.Preventif;

/// <summary>
/// Actions d'un clic du préventif (US-12) : confirmer ou écarter une proposition, suspendre ou reprendre un plan ; un
/// formulaire par ligne, posté ici puis renvoyé vers l'écran. Droits vérifiés par l'API ; jeton anti-falsification vérifié
/// ici (point d'accès sans formulaire lié). Photos et rapports des contrôles relayés depuis l'API.
/// </summary>
internal static class ActionsPreventifEndpoints
{
    internal static IEndpointRouteBuilder MapActionsPreventif(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.MapGroup("/organisations/{organisationId:guid}/sites/{siteId:guid}/preventif").RequireAuthorization();
        site.MapPost("/propositions/confirmer", (Guid organisationId, Guid siteId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            ExecuterAsync(contexte, antifalsification, formulaire => Propositions(organisationId, siteId, formulaire), async formulaire =>
                await session.EcrireAsync<CreeApi>(Api(organisationId, siteId, "proposals/confirm"), new
                {
                    commandeId = Guid.TryParse(formulaire["commandeId"], out var commande) ? commande : Guid.NewGuid(),
                    cle = formulaire["cle"].ToString(),
                }, CancellationToken.None).ConfigureAwait(false)));
        site.MapPost("/propositions/ecarter", (Guid organisationId, Guid siteId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            ExecuterAsync(contexte, antifalsification, formulaire => Propositions(organisationId, siteId, formulaire), async formulaire =>
                await session.ExecuterAsync(Api(organisationId, siteId, "proposals/dismiss"), new
                {
                    cle = formulaire["cle"].ToString(),
                    motif = formulaire["motif"].ToString(),
                    commentaire = formulaire["commentaire"].ToString(),
                }, CancellationToken.None).ConfigureAwait(false)));
        site.MapPost("/plans/{planId:guid}/suspendre", (Guid organisationId, Guid siteId, Guid planId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            ExecuterAsync(contexte, antifalsification, _ => Plan(organisationId, siteId, planId), async formulaire =>
                await session.ExecuterAsync(Api(organisationId, siteId, $"plans/{planId}/suspend"), new
                {
                    motif = formulaire["motif"].ToString(),
                    version = uint.TryParse(formulaire["version"], out var version) ? version : (uint?)null,
                }, CancellationToken.None).ConfigureAwait(false)));
        site.MapPost("/plans/{planId:guid}/reprendre", (Guid organisationId, Guid siteId, Guid planId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            ExecuterAsync(contexte, antifalsification, _ => Plan(organisationId, siteId, planId), async formulaire =>
                await session.ExecuterAsync(Api(organisationId, siteId, $"plans/{planId}/resume"), new
                {
                    version = uint.TryParse(formulaire["version"], out var version) ? version : (uint?)null,
                }, CancellationToken.None).ConfigureAwait(false)));
        // Photos et rapport PDF d'un contrôle : droits vérifiés par l'API, jamais mis en cache partagé ; le rapport est
        // téléchargé en pièce jointe (jamais affiché dans l'origine de l'application). Photos : écran de contrôle (S12-07).
        site.MapGet("/controles/{controleId:guid}/photos/{rang:int}", (Guid organisationId, Guid siteId, Guid controleId, int rang, SessionApi session, HttpContext contexte) =>
            RelaisPhoto.RelayerAsync(session, contexte, Api(organisationId, siteId, $"controls/{controleId}/photos/{rang}")));
        site.MapGet("/controles/{controleId:guid}/rapport", (Guid organisationId, Guid siteId, Guid controleId, SessionApi session, HttpContext contexte) =>
            RelaisPhoto.RelayerAsync(session, contexte, Api(organisationId, siteId, $"controls/{controleId}/report"), "application/pdf", $"rapport-{controleId:N}.pdf"));
        return endpoints;
    }

    private static string Api(Guid organisationId, Guid siteId, string chemin) => $"/organizations/{organisationId}/sites/{siteId}/preventive/{chemin}";

    // Retour sur l'onglet du bâtiment affiché quand le formulaire le précise.
    private static string Propositions(Guid organisationId, Guid siteId, IFormCollection formulaire) =>
        $"/organisations/{organisationId}/preventif/propositions?site={siteId}" + (Guid.TryParse(formulaire["batiment"], out var batiment) ? $"&batiment={batiment}" : "");

    private static string Plan(Guid organisationId, Guid siteId, Guid planId) => $"/organisations/{organisationId}/preventif/plan?site={siteId}&plan={planId}";

    // Refus : l'écran reçoit l'issue (champ refusé, code, ou droit) et l'explique ; succès : « ok ».
    private static async Task<IResult> ExecuterAsync(
        HttpContext contexte, IAntiforgery antifalsification, Func<IFormCollection, string> retour, Func<IFormCollection, Task> action)
    {
        if (!await antifalsification.IsRequestValidAsync(contexte).ConfigureAwait(false))
        {
            return Results.BadRequest();
        }

        var formulaire = await contexte.Request.ReadFormAsync(CancellationToken.None).ConfigureAwait(false);
        string issue;
        try
        {
            await action(formulaire).ConfigureAwait(false);
            issue = "ok";
        }
        catch (SessionExpireeException)
        {
            return Results.LocalRedirect("/connexion");
        }
        catch (RefusApiException refus)
        {
            issue = refus.Champ ?? refus.Code ?? refus.Statut switch
            {
                System.Net.HttpStatusCode.NotFound => "introuvable",
                System.Net.HttpStatusCode.Forbidden => "interdit",
                _ => "indisponible",
            };
        }

        return Results.LocalRedirect($"{retour(formulaire)}&issue={Uri.EscapeDataString(issue)}");
    }

    /// <summary>Message d'une issue d'action, commun aux écrans du préventif.</summary>
    internal static string? Message(string? issue) => issue switch
    {
        null or "" => null,
        "ok" => null,
        "cle" => "Cette proposition n'existe plus (classement ou composant changé) : la liste est rechargée.",
        "deja_planifiee" => "Cette proposition est déjà confirmée : le plan existe.",
        "motif" => "Choisissez un motif.",
        "commentaire" => "Commentaire obligatoire pour le motif « Autre » (500 caractères au plus).",
        "modifie_entre_temps" => "Quelqu'un d'autre vient de modifier ce plan : la fiche est rechargée, recommencez.",
        "statut_incompatible" => "Cette action n'est plus possible dans l'état actuel du plan.",
        "interdit" => "Vous n'avez pas le droit de faire cette action.",
        "introuvable" => "Cet élément n'existe plus.",
        _ => "Action refusée ou service momentanément indisponible : réessayez.",
    };
}

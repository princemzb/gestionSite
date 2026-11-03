using System.Net;
using GestionSite.Web.Session;
using Microsoft.AspNetCore.Antiforgery;

namespace GestionSite.Web.Administration;

/// <summary>
/// Réponse à une invitation depuis la carte en tête de l'application (maquette 4A, US-31) : l'API vérifie que l'invitation
/// est bien celle de l'appelant (404 sinon) ; jeton anti-falsification vérifié ici (point d'accès sans formulaire lié).
/// L'issue s'affiche sur l'accueil, qui propose d'ouvrir l'organisation rejointe.
/// </summary>
internal static class InvitationsEndpoints
{
    internal static IEndpointRouteBuilder MapInvitations(this IEndpointRouteBuilder endpoints)
    {
        var invitation = endpoints.MapGroup("/invitations/{membreId:guid}").RequireAuthorization();
        invitation.MapPost("/accepter", (Guid membreId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            RepondreAsync(membreId, "accept", "acceptee", session, contexte, antifalsification));
        invitation.MapPost("/refuser", (Guid membreId, SessionApi session, HttpContext contexte, IAntiforgery antifalsification) =>
            RepondreAsync(membreId, "decline", "refusee", session, contexte, antifalsification));
        return endpoints;
    }

    private static async Task<IResult> RepondreAsync(
        Guid membreId, string action, string reussite, SessionApi session, HttpContext contexte, IAntiforgery antifalsification)
    {
        if (!await antifalsification.IsRequestValidAsync(contexte).ConfigureAwait(false))
        {
            return Results.BadRequest();
        }

        // Organisation de l'invitation lue avant de répondre : après l'acceptation, l'accueil propose de l'ouvrir.
        Guid? organisation = null;
        string issue;
        try
        {
            organisation = (await session.MoiAsync(CancellationToken.None).ConfigureAwait(false)).Invitations?
                .FirstOrDefault(i => i.MembreId == membreId)?.OrganisationId;
            await session.ExecuterAsync($"/me/invitations/{membreId}/{action}", new { }, CancellationToken.None).ConfigureAwait(false);
            issue = reussite;
        }
        catch (SessionExpireeException)
        {
            return Results.LocalRedirect("/connexion");
        }
        catch (RefusApiException refus)
        {
            issue = refus.Statut switch
            {
                HttpStatusCode.NotFound => "introuvable",
                HttpStatusCode.Conflict => "modifiee",
                _ => "indisponible",
            };
        }

        return Results.LocalRedirect(issue == "acceptee" && organisation is { } id ? $"/?invitation={issue}&organisation={id}" : $"/?invitation={issue}");
    }
}

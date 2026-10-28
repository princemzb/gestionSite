using GestionSite.Modules.Identite.Contracts;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Plateforme.PocSynchronisation;

internal static class PocSynchronisationEndpoints
{
    internal static IEndpointRouteBuilder MapPocSynchronisation(this IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<PocSynchronisationOptions>>().Value.Active)
        {
            return endpoints; // POC inactif : aucune route
        }

        endpoints.MapPost("/poc/sync/organizations/{orgId:guid}/commands", AppliquerAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> AppliquerAsync(
        Guid orgId, CommandeNote commande, CompteCourant compte, IAutorisations autorisations, ServiceCommandes service,
        CancellationToken cancellationToken)
    {
        // Identité déduite de la session ; celle de la commande est vérifiée, jamais crue (SYNCHRONISATION.md).
        if (compte.Id != commande.ActeurId)
        {
            return Probleme(StatusCodes.Status422UnprocessableEntity, "rejected", "acteur_different",
                "Commande d'un autre auteur : elle reste sur l'appareil de son auteur.");
        }

        if (commande.OperationId == Guid.Empty || commande.NoteId == Guid.Empty || commande.BaseVersion < 0
            || string.IsNullOrWhiteSpace(commande.DeviceId) || commande.DeviceId.Length > 100
            || string.IsNullOrWhiteSpace(commande.Texte) || commande.Texte.Length > 2000)
        {
            return Probleme(StatusCodes.Status422UnprocessableEntity, "rejected", "commande_invalide", "Commande incomplète ou invalide.");
        }

        var decision = await autorisations.VerifierAsync(orgId, Droit.DocumenterIntervention, commande.SiteId, cancellationToken).ConfigureAwait(false);
        if (decision != DecisionAutorisation.Autorise)
        {
            return decision == DecisionAutorisation.Refuse ? Results.Forbid() : Results.NotFound();
        }

        try
        {
            return Results.Ok(await service.AppliquerAsync(orgId, compte.Id!.Value, commande, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationReutiliseeException)
        {
            return Probleme(StatusCodes.Status409Conflict, "rejected", "operation_reutilisee", "Identifiant d'opération déjà utilisé pour un autre contenu.");
        }
    }

    private static IResult Probleme(int statut, string resultat, string code, string message) =>
        Results.Problem(statusCode: statut, title: message, extensions: new Dictionary<string, object?> { ["resultat"] = resultat, ["code"] = code });
}

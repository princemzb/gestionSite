using GestionSite.Modules.Patrimoine.Composants;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Patrimoine.Evaluations;

// Constats et mesures : une réponse par point et par mesure de la grille, dans son ordre (null : non vérifié, non relevée).
// État pour un composant individuel, répartition pour un lot ; commande : identifiant d'envoi choisi par l'appareil.
internal sealed record DemandeEvaluation(
    Guid? CommandeId, int? GrilleVersion, IReadOnlyList<bool?>? Constats, IReadOnlyList<decimal?>? Mesures, string? Etat,
    DemandeRepartition? Repartition, string? Remarque);

/// <summary>Évaluations des composants d'un site (CONTRATS : évaluations, S11-02).</summary>
internal static class EvaluationsEndpoints
{
    internal static IEndpointRouteBuilder MapEvaluations(this IEndpointRouteBuilder endpoints)
    {
        var evaluations = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/inventory/components/{composantId:guid}/evaluations")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(StructureEndpoints.TraduireRefus);
        evaluations.MapGet("/", async (Guid orgId, Guid siteId, Guid composantId, ServiceEvaluations service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, composantId, ct).ConfigureAwait(false)));
        evaluations.MapPost("/", async (Guid orgId, Guid siteId, Guid composantId, HttpRequest requete, ServiceEvaluations service, CancellationToken ct) =>
            Results.Ok(await service.EvaluerAsync(orgId, siteId, composantId, () => FormulaireAvecPhotos.LireAsync<DemandeEvaluation>(requete, ct), ct)
                .ConfigureAwait(false)))
            .AvecPhotos();
        evaluations.MapGet("/{evaluationId:guid}/photos/{rang:int}", async (
            Guid orgId, Guid siteId, Guid composantId, Guid evaluationId, int rang, ServiceEvaluations service, CancellationToken ct) =>
            await service.PhotoAsync(orgId, siteId, composantId, evaluationId, rang, ct).ConfigureAwait(false) is { } photo
                ? Results.File(photo, TraitementPhotos.TypeContenu)
                : Results.NotFound());
        return endpoints;
    }
}

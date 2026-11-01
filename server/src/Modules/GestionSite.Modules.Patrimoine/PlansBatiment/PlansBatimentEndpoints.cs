using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Patrimoine.PlansBatiment;

internal sealed record DemandePlanBatiment(Guid? BatimentId, Guid? NiveauId);

// Version : celle lue (0 avant la première) ; commandeId : identifiant d'envoi, le même pour un renvoi.
internal sealed record DemandeVersionPlan(Guid? CommandeId, int? Version, string? Motif, SaisieContenuPlan? Contenu);

internal sealed record DemandeRestauration(Guid? CommandeId, int? Version, string? Motif);

/// <summary>
/// Plans de bâtiment d'un site (CONTRATS : plans de bâtiment, S21-02). « building-plans » : à ne pas confondre avec les
/// plans de maintenance préventive (US-12).
/// </summary>
internal static class PlansBatimentEndpoints
{
    /// <summary>Corps d'un enregistrement : 20 000 sommets au plus, largement sous 4 Mo en JSON.</summary>
    internal const long CorpsMax = 4 * 1024 * 1024;

    internal static IEndpointRouteBuilder MapPlansBatiment(this IEndpointRouteBuilder endpoints)
    {
        var plans = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/inventory/building-plans")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(StructureEndpoints.TraduireRefus)
            .AddEndpointFilter(TraduireRefus);
        plans.MapGet("/", async (Guid orgId, Guid siteId, ServicePlansBatiment service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, ct).ConfigureAwait(false)));
        plans.MapPost("/", async (Guid orgId, Guid siteId, DemandePlanBatiment demande, ServicePlansBatiment service, CancellationToken ct) =>
            Results.Ok(new Cree(await service.CreerAsync(orgId, siteId, demande.BatimentId, demande.NiveauId, ct).ConfigureAwait(false))));
        plans.MapGet("/{planId:guid}", async (Guid orgId, Guid siteId, Guid planId, ServicePlansBatiment service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, planId, version: null, ct).ConfigureAwait(false)));
        plans.MapPut("/{planId:guid}", async (Guid orgId, Guid siteId, Guid planId, DemandeVersionPlan demande, ServicePlansBatiment service, CancellationToken ct) =>
            Results.Ok(await service.EnregistrerAsync(orgId, siteId, planId, demande.CommandeId, demande.Version, demande.Motif, demande.Contenu, ct)
                .ConfigureAwait(false)))
            .WithMetadata(new RequestSizeLimitAttribute(CorpsMax));
        plans.MapGet("/{planId:guid}/versions", async (
            Guid orgId, Guid siteId, Guid planId, int? avant, int? limite, ServicePlansBatiment service, CancellationToken ct) =>
            Results.Ok(await service.HistoriqueAsync(orgId, siteId, planId, avant, limite, ct).ConfigureAwait(false)));
        plans.MapGet("/{planId:guid}/versions/{version:int}", async (
            Guid orgId, Guid siteId, Guid planId, int version, ServicePlansBatiment service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, planId, version, ct).ConfigureAwait(false)));
        plans.MapPost("/{planId:guid}/versions/{version:int}/restore", async (
            Guid orgId, Guid siteId, Guid planId, int version, DemandeRestauration demande, ServicePlansBatiment service, CancellationToken ct) =>
            Results.Ok(await service.RestaurerAsync(orgId, siteId, planId, version, demande.CommandeId, demande.Version, demande.Motif, ct)
                .ConfigureAwait(false)));

        // Fonds importés en image (S21-04) : multipart « demande » ({commandeId}) et un « fichier » de 25 Mo au plus.
        plans.MapPost("/{planId:guid}/backgrounds", async (Guid orgId, Guid siteId, Guid planId, HttpRequest requete, ServiceFondsPlan service, CancellationToken ct) =>
            Results.Ok(await service.ImporterAsync(
                orgId, siteId, planId, () => FormulaireAvecPhotos.LireFichierAsync<DemandeFond>(requete, TraitementFonds.Champ, TraitementFonds.TailleMax, ct), ct)
                .ConfigureAwait(false)))
            .AvecFichier(TraitementFonds.TailleMax);
        plans.MapGet("/{planId:guid}/backgrounds", async (Guid orgId, Guid siteId, Guid planId, ServiceFondsPlan service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, planId, ct).ConfigureAwait(false)));
        plans.MapGet("/{planId:guid}/backgrounds/{fondId:guid}", async (
            Guid orgId, Guid siteId, Guid planId, Guid fondId, HttpResponse reponse, ServiceFondsPlan service, CancellationToken ct) =>
        {
            // Donnée de sûreté (RP-21.10) : jamais en cache partagé, type jamais réinterprété.
            reponse.Headers.CacheControl = "private, no-store";
            reponse.Headers.XContentTypeOptions = "nosniff";
            return await service.ImageAsync(orgId, siteId, planId, fondId, ct).ConfigureAwait(false) is { } image
                ? Results.File(image.Contenu, image.TypeContenu)
                : Results.NotFound();
        });
        return endpoints;
    }

    // Refus propres aux plans de bâtiment ; accès, saisie et éléments introuvables : filtres communs, placés avant.
    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (VersionPlanDepasseeException depassee)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: depassee.Message, extensions: new Dictionary<string, object?>
            {
                ["code"] = "version_depassee",
                ["versionCourante"] = depassee.VersionCourante,
                ["auteur"] = depassee.Auteur,
                ["modifieLe"] = depassee.Le,
            });
        }
        catch (PlanExistantException existant)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: existant.Message, extensions: new Dictionary<string, object?>
            {
                ["code"] = "plan_existant",
                ["planId"] = existant.PlanId,
            });
        }
        catch (EnvoiPlanReutiliseException reutilise)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, reutilise.Message, "commande_reutilisee");
        }
        catch (QuotaFondsAtteintException plein)
        {
            return RefusCommuns.Probleme(StatusCodes.Status422UnprocessableEntity, plein.Message, "quota_atteint");
        }
        catch (PdfRefuseException pdf)
        {
            // Message propre au refus (titre) : l'éditeur l'affiche tel quel, ou s'appuie sur le code ; « pages » pour une page absente.
            var extensions = new Dictionary<string, object?> { ["code"] = pdf.Code, ["champ"] = pdf.Champ };
            if (pdf.Pages is { } pages)
            {
                extensions["pages"] = pages;
            }

            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: pdf.Message, extensions: extensions);
        }
    }
}

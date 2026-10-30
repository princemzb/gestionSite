using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.Preventif;

internal sealed record DemandePeriode(int? Valeur, string? Unite);

// Cible : Composant (composantId), ZoneCorpsEtat (zoneId, corpsEtat) ou Batiment (batimentId).
internal sealed record DemandeCible(string? Type, Guid? ComposantId, Guid? ZoneId, string? CorpsEtat, Guid? BatimentId);

// Création : tout ; modification : mêmes champs sauf commande, cible et nature, avec la version lue. Période absente : plan ponctuel.
internal sealed record DemandePlan(
    Guid? CommandeId, DemandeCible? Cible, string? Nature, string? Libelle, string? Base, string? Reference, Guid? IntervenantCompteId, string? Prestataire,
    Guid? ReferentCompteId, string? DonneurOrdre, DemandePeriode? Periode, string? Mode, DateOnly? PremiereEcheance, uint? Version);

internal sealed record DemandeConfirmation(
    Guid? CommandeId, string? Cle, DateOnly? PremiereEcheance, Guid? IntervenantCompteId, string? Prestataire, Guid? ReferentCompteId, string? DonneurOrdre);

internal sealed record DemandeEcart(string? Cle, string? Motif, string? Commentaire);

internal sealed record DemandeSuspension(string? Motif, uint? Version);

internal sealed record PlanCree(Guid Id);

/// <summary>Préventif d'un site (CONTRATS : préventif, US-12).</summary>
internal static class PreventifEndpoints
{
    internal static IEndpointRouteBuilder MapPreventif(this IEndpointRouteBuilder endpoints)
    {
        var preventif = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/preventive")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(TraduireRefus);
        preventif.MapGet("/proposals", async (Guid orgId, Guid siteId, ServicePropositions service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, ct).ConfigureAwait(false)));
        preventif.MapPost("/proposals/confirm", async (Guid orgId, Guid siteId, DemandeConfirmation demande, ServicePlans service, CancellationToken ct) =>
            Results.Ok(new PlanCree(await service.ConfirmerAsync(orgId, siteId, demande, ct).ConfigureAwait(false))));
        preventif.MapPost("/proposals/dismiss", async (Guid orgId, Guid siteId, DemandeEcart demande, ServicePlans service, CancellationToken ct) =>
        {
            await service.EcarterAsync(orgId, siteId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        preventif.MapGet("/schedule", async (Guid orgId, Guid siteId, ServicePlans service, CancellationToken ct) =>
            Results.Ok(await service.EcheancierAsync(orgId, siteId, ct).ConfigureAwait(false)));
        preventif.MapGet("/plans", async (Guid orgId, Guid siteId, ServicePlans service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, ct).ConfigureAwait(false)));
        preventif.MapPost("/plans", async (Guid orgId, Guid siteId, DemandePlan demande, ServicePlans service, CancellationToken ct) =>
            Results.Ok(new PlanCree(await service.CreerAsync(orgId, siteId, demande, ct).ConfigureAwait(false))));
        preventif.MapGet("/plans/{id:guid}", async (Guid orgId, Guid siteId, Guid id, ServicePlans service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, id, ct).ConfigureAwait(false)));
        preventif.MapPut("/plans/{id:guid}", async (Guid orgId, Guid siteId, Guid id, DemandePlan demande, ServicePlans service, CancellationToken ct) =>
        {
            await service.ModifierAsync(orgId, siteId, id, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        preventif.MapPost("/plans/{id:guid}/suspend", async (Guid orgId, Guid siteId, Guid id, DemandeSuspension demande, ServicePlans service, CancellationToken ct) =>
        {
            await service.SuspendreAsync(orgId, siteId, id, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        preventif.MapPost("/plans/{id:guid}/resume", async (Guid orgId, Guid siteId, Guid id, DemandeVersion demande, ServicePlans service, CancellationToken ct) =>
        {
            await service.ReprendreAsync(orgId, siteId, id, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        return endpoints;
    }

    // Refus propres au préventif ; accès et saisie : RefusCommuns, placé avant.
    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (PlanIntrouvableException)
        {
            return Results.NotFound();
        }
        catch (PlanModifieException modifie)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, modifie.Message, "modifie_entre_temps");
        }
        catch (PlanIncompatibleException incompatible)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, incompatible.Message, "statut_incompatible");
        }
        catch (PropositionDejaPlanifieeException deja)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, deja.Message, "deja_planifiee");
        }
        catch (CommandeReutiliseeException reutilisee)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, reutilisee.Message, "commande_reutilisee");
        }
    }
}

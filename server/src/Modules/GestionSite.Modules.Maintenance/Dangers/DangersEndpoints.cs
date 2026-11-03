using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.Dangers;

/// <summary>Suivi des dangers d'un site (CONTRATS « Danger », US-10, S10-04).</summary>
internal static class DangersEndpoints
{
    internal static IEndpointRouteBuilder MapDangers(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(TraduireRefus);
        site.MapGet("/dangers", async (Guid orgId, Guid siteId, ServiceDangers service, CancellationToken ct) =>
            Results.Ok(await service.EnCoursAsync(orgId, siteId, ct).ConfigureAwait(false)));
        var danger = site.MapGroup("/cases/{id:guid}/dangers/{dangerId:guid}");
        danger.MapPost("/acknowledge", async (Guid orgId, Guid siteId, Guid id, Guid dangerId, DemandePriseEnCompte demande, ServiceDangers service, CancellationToken ct) =>
        {
            await service.PrendreEnCompteAsync(orgId, siteId, id, dangerId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        danger.MapPost("/securing", async (Guid orgId, Guid siteId, Guid id, Guid dangerId, DemandeMiseEnSecurite demande, ServiceDangers service, CancellationToken ct) =>
        {
            await service.DeclarerMiseEnSecuriteAsync(orgId, siteId, id, dangerId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        danger.MapPost("/securing/verify", async (Guid orgId, Guid siteId, Guid id, Guid dangerId, DemandeVerification demande, ServiceDangers service, CancellationToken ct) =>
        {
            await service.VerifierMiseEnSecuriteAsync(orgId, siteId, id, dangerId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        danger.MapPost("/lift", async (Guid orgId, Guid siteId, Guid id, Guid dangerId, DemandeFinDanger demande, ServiceDangers service, CancellationToken ct) =>
        {
            await service.LeverAsync(orgId, siteId, id, dangerId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        danger.MapPost("/dismiss", async (Guid orgId, Guid siteId, Guid id, Guid dangerId, DemandeFinDanger demande, ServiceDangers service, CancellationToken ct) =>
        {
            await service.EcarterAsync(orgId, siteId, id, dangerId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        return endpoints;
    }

    // Refus propres aux dangers ; accès et saisie : RefusCommuns, placé avant.
    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (DangerIntrouvableException)
        {
            return Results.NotFound();
        }
        catch (Domain.DangerIncompatibleException incompatible)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, incompatible.Message, incompatible.Code);
        }
        catch (DossierModifieException modifie)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, modifie.Message, "modifie_entre_temps");
        }
    }
}

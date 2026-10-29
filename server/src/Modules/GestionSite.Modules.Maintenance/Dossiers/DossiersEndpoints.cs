using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.Dossiers;

internal sealed record DemandeDeclaration(
    Guid CommandeId, string? Nature, Guid? ZoneId, string? Emplacement, string? Description, bool Danger, bool ActiviteBloquee);

internal sealed record DemandeQualification(string? Priorite, Guid? ZoneId, Guid? ComposantId, uint? Version);

internal sealed record DemandeRattachement(Guid? PrincipalId, uint? Version);

internal sealed record DemandeClassement(string? Motif, string? Commentaire, uint? Version);

internal sealed record DossierCree(Guid Id);

internal sealed record DemandeAffectation(string? Titre, Guid? IntervenantCompteId, string? Prestataire, Guid? ReferentCompteId, uint? Version);

internal sealed record DemandeVersion(uint? Version);

internal sealed record InterventionCreee(int Numero);

/// <summary>Dossiers d'un site (CONTRATS : dossiers, S09-01).</summary>
internal static class DossiersEndpoints
{
    internal static IEndpointRouteBuilder MapDossiers(this IEndpointRouteBuilder endpoints)
    {
        var dossiers = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/cases")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(TraduireRefus);
        dossiers.MapGet("/", async (Guid orgId, Guid siteId, bool? clos, ServiceDossiers service, CancellationToken ct) =>
            Results.Ok(await service.ListerAsync(orgId, siteId, clos ?? false, ct).ConfigureAwait(false)));
        dossiers.MapGet("/assignees", async (Guid orgId, Guid siteId, ServiceInterventions service, CancellationToken ct) =>
            Results.Ok(await service.IntervenantsAsync(orgId, siteId, ct).ConfigureAwait(false)));
        dossiers.MapGet("/mine", async (Guid orgId, Guid siteId, ServiceInterventions service, CancellationToken ct) =>
            Results.Ok(await service.MesInterventionsAsync(orgId, siteId, ct).ConfigureAwait(false)));
        dossiers.MapPost("/{id:guid}/interventions", async (Guid orgId, Guid siteId, Guid id, DemandeAffectation demande, ServiceInterventions service, CancellationToken ct) =>
            Results.Ok(new InterventionCreee(await service.AffecterAsync(orgId, siteId, id, demande, ct).ConfigureAwait(false))));
        dossiers.MapPost("/{id:guid}/interventions/{numero:int}/start", async (
            Guid orgId, Guid siteId, Guid id, int numero, DemandeVersion demande, ServiceInterventions service, CancellationToken ct) =>
        {
            await service.DemarrerAsync(orgId, siteId, id, numero, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        dossiers.MapGet("/{id:guid}", async (Guid orgId, Guid siteId, Guid id, ServiceDossiers service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, id, ct).ConfigureAwait(false)));
        dossiers.MapPost("/", async (Guid orgId, Guid siteId, DemandeDeclaration demande, ServiceDossiers service, CancellationToken ct) =>
            Results.Ok(new DossierCree(await service.DeclarerAsync(orgId, siteId, demande, ct).ConfigureAwait(false))));
        dossiers.MapPost("/{id:guid}/qualify", async (Guid orgId, Guid siteId, Guid id, DemandeQualification demande, ServiceDossiers service, CancellationToken ct) =>
        {
            await service.QualifierAsync(orgId, siteId, id, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        dossiers.MapPost("/{id:guid}/duplicate-of", async (Guid orgId, Guid siteId, Guid id, DemandeRattachement demande, ServiceDossiers service, CancellationToken ct) =>
        {
            await service.RattacherAsync(orgId, siteId, id, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        dossiers.MapPost("/{id:guid}/dismiss", async (Guid orgId, Guid siteId, Guid id, DemandeClassement demande, ServiceDossiers service, CancellationToken ct) =>
        {
            await service.ClasserAsync(orgId, siteId, id, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        return endpoints;
    }

    // Refus propres aux dossiers ; accès et saisie : RefusCommuns, placé avant.
    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (Exception introuvable) when (introuvable is DossierIntrouvableException or InterventionIntrouvableException)
        {
            return Results.NotFound();
        }
        catch (Domain.InterventionIncompatibleException incompatible)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, incompatible.Message, "statut_incompatible");
        }
        catch (Domain.DossierNonQualifieException nonQualifie)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, nonQualifie.Message, "non_qualifie");
        }
        catch (Domain.StatutIncompatibleException incompatible)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, incompatible.Message, "statut_incompatible");
        }
        catch (DossierModifieException modifie)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, modifie.Message, "modifie_entre_temps");
        }
        catch (DepotPublic.CommandeReutiliseeException reutilisee)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, reutilisee.Message, "commande_reutilisee");
        }
        catch (Domain.DoublonsRattachesException rattaches)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, rattaches.Message, "doublons_rattaches");
        }
    }
}

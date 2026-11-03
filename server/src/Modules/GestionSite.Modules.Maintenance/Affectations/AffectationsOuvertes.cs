using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Affectations;

/// <summary>Nombre d'interventions et de contrôles ouverts confiés à une personne sur un site.</summary>
internal sealed record AffectationsSite(Guid SiteId, int Interventions, int Controles);

/// <summary>
/// Affectations ouvertes d'un membre (US-31, décision du porteur du 03/11) : avant une révocation ou un retrait de rôle,
/// l'administrateur est averti, rien n'est changé, un responsable du site réaffecte. Des nombres par site seulement :
/// l'administrateur sans rôle sur le site n'accède pas aux dossiers (DEC-04), ni à leurs titres.
/// </summary>
internal static class AffectationsOuvertes
{
    internal static IEndpointRouteBuilder MapAffectationsOuvertes(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/organizations/{orgId:guid}/open-assignments/{compteId:guid}", ListerAsync)
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire);
        return endpoints;
    }

    private static async Task<IResult> ListerAsync(
        Guid orgId, Guid compteId, IAutorisations autorisations, MaintenanceDbContext context, CancellationToken cancellationToken)
    {
        var decision = await autorisations.VerifierAsync(orgId, Droit.GererComptes, siteId: null, cancellationToken).ConfigureAwait(false);
        if (decision != DecisionAutorisation.Autorise)
        {
            throw new AccesRefuseException(siteVisible: decision == DecisionAutorisation.Refuse);
        }

        // En charge : l'intervenant interne, ou le référent d'un prestataire ; organisation fixée par l'autorisation (filtres et RLS).
        var interventions = await context.Interventions.AsNoTracking()
            .Where(i => i.Statut != StatutIntervention.Cloturee && (i.IntervenantCompteId == compteId || i.ReferentCompteId == compteId))
            .GroupBy(i => i.SiteId).Select(g => new { SiteId = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var controles = await context.Occurrences.AsNoTracking()
            .Where(o => o.Statut == StatutOccurrence.Affectee && (o.IntervenantCompteId == compteId || o.ReferentCompteId == compteId))
            .GroupBy(o => o.SiteId).Select(g => new { SiteId = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var sites = interventions.Select(i => i.SiteId).Union(controles.Select(c => c.SiteId)).Order();
        return Results.Ok(sites.Select(site => new AffectationsSite(
            site,
            interventions.Find(i => i.SiteId == site)?.Nombre ?? 0,
            controles.Find(c => c.SiteId == site)?.Nombre ?? 0)).ToList());
    }
}

using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Patrimoine.Etablissement;

// Champs nullables : un champ obligatoire absent est une saisie invalide (422), jamais une valeur par défaut silencieuse.
internal sealed record DemandeClassement(
    bool? Erp, string? Type, IReadOnlyList<string>? TypesSecondaires, int? Categorie, int? Effectif, bool? LocauxSommeil, bool? MonumentHistorique,
    string? PermisAvant1997, DateOnly? DerniereVisiteCommission, uint? Version);

internal sealed record DemandeEtablissement(bool? SalariesPresents, uint? Version);

/// <summary>Classement ERP des bâtiments et données du site pour le préventif (CONTRATS : établissement, S12-01).</summary>
internal static class EtablissementEndpoints
{
    internal static IEndpointRouteBuilder MapEtablissement(this IEndpointRouteBuilder endpoints)
    {
        var inventaire = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/inventory")
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(StructureEndpoints.TraduireRefus);
        inventaire.MapGet("/establishment", async (Guid orgId, Guid siteId, ServiceEtablissement service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, ct).ConfigureAwait(false)));
        inventaire.MapPut("/establishment", async (Guid orgId, Guid siteId, DemandeEtablissement demande, ServiceEtablissement service, CancellationToken ct) =>
        {
            await service.DeclarerAsync(orgId, siteId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        inventaire.MapPut("/buildings/{batimentId:guid}/classification", async (
            Guid orgId, Guid siteId, Guid batimentId, DemandeClassement demande, ServiceEtablissement service, CancellationToken ct) =>
        {
            await service.ClasserAsync(orgId, siteId, batimentId, demande, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        return endpoints;
    }
}

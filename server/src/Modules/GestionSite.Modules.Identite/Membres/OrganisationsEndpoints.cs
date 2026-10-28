using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Identite.Membres;

internal sealed record DemandeSite(string Nom);

internal sealed record ReponseSite(Guid Id, string Nom);

internal sealed record DemandeMembre(string Nom, string Email, IReadOnlyList<RoleDemande> Roles);

internal sealed record DemandeRevision(IReadOnlyList<RoleDemande> Roles, int Version);

internal sealed record DemandeActivation(string Jeton, string MotDePasse);

/// <summary>Sites, membres et activation (CONTRATS, US-06 S06-04).</summary>
internal static class OrganisationsEndpoints
{
    internal static IEndpointRouteBuilder MapOrganisations(this IEndpointRouteBuilder endpoints)
    {
        var organisation = endpoints.MapGroup("/organizations/{orgId:guid}").RequireAuthorization().AddEndpointFilter(TraduireRefus);
        organisation.MapGet("/sites", ListerSitesAsync);
        organisation.MapPost("/sites", CreerSiteAsync);
        organisation.MapPost("/members", CreerMembreAsync);
        organisation.MapPut("/members/{memberId:guid}", ReviserMembreAsync);
        organisation.MapDelete("/members/{memberId:guid}", RevoquerMembreAsync);
        organisation.MapPost("/members/{memberId:guid}/activation", RenouvelerActivationAsync);
        endpoints.MapPost("/auth/activate", ActiverAsync).AllowAnonymous().AddEndpointFilter(TraduireRefus);
        return endpoints;
    }

    private static async Task<IResult> ListerSitesAsync(Guid orgId, ServiceMembres service, CancellationToken cancellationToken) =>
        Results.Ok((await service.ListerSitesAsync(orgId, cancellationToken).ConfigureAwait(false)).Select(s => new ReponseSite(s.Id, s.Nom)));

    private static async Task<IResult> CreerSiteAsync(Guid orgId, DemandeSite demande, ServiceMembres service, CancellationToken cancellationToken)
    {
        var site = await service.CreerSiteAsync(orgId, demande.Nom, cancellationToken).ConfigureAwait(false);
        return Results.Created($"/organizations/{orgId}/sites/{site.Id}", new ReponseSite(site.Id, site.Nom));
    }

    private static async Task<IResult> CreerMembreAsync(Guid orgId, DemandeMembre demande, ServiceMembres service, CancellationToken cancellationToken)
    {
        var cree = await service.CreerAsync(orgId, demande.Nom, demande.Email, demande.Roles ?? [], cancellationToken).ConfigureAwait(false);
        return Results.Created($"/organizations/{orgId}/members/{cree.MembreId}", cree);
    }

    private static async Task<IResult> ReviserMembreAsync(
        Guid orgId, Guid memberId, DemandeRevision demande, ServiceMembres service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ReviserAsync(orgId, memberId, demande.Roles ?? [], demande.Version, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RevoquerMembreAsync(Guid orgId, Guid memberId, ServiceMembres service, CancellationToken cancellationToken)
    {
        await service.RevoquerAsync(orgId, memberId, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> RenouvelerActivationAsync(Guid orgId, Guid memberId, ServiceMembres service, CancellationToken cancellationToken) =>
        Results.Ok(await service.RenouvelerActivationAsync(orgId, memberId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ActiverAsync(DemandeActivation demande, ServiceActivation service, CancellationToken cancellationToken)
    {
        await service.ActiverAsync(demande.Jeton, demande.MotDePasse, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    // Refus métier et données invalides du domaine : réponse structurée, code stable (CONTRATS).
    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (RefusException refus)
        {
            return Results.Problem(statusCode: refus.Statut, title: refus.Message, extensions: new Dictionary<string, object?> { ["code"] = refus.Code });
        }
        catch (ArgumentException invalide) when (invalide.ParamName is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Donnée invalide.",
                extensions: new Dictionary<string, object?> { ["code"] = "donnee_invalide", ["champ"] = invalide.ParamName });
        }
    }
}

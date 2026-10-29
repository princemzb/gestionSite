using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Patrimoine.Structure;

internal sealed record DemandeNom(string? Nom);

// Champs nullables : un champ absent est une saisie invalide (422), jamais une valeur par défaut silencieuse.
internal sealed record DemandeNiveau(string? Nom, int? Ordre);

internal sealed record DemandeZone(string? Nom, Guid? BatimentId, Guid? NiveauId);

internal sealed record Cree(Guid Id);

/// <summary>Structure de l'inventaire d'un site (CONTRATS : inventaire, S07-01).</summary>
internal static class StructureEndpoints
{
    internal static IEndpointRouteBuilder MapStructure(this IEndpointRouteBuilder endpoints)
    {
        var inventaire = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/inventory")
            .RequireAuthorization().AddEndpointFilter(TraduireRefus);
        inventaire.MapGet("/structure", async (Guid orgId, Guid siteId, ServiceStructure service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, ct).ConfigureAwait(false)));
        inventaire.MapPost("/buildings", async (Guid orgId, Guid siteId, DemandeNom demande, ServiceStructure service, CancellationToken ct) =>
            Results.Ok(new Cree(await service.CreerBatimentAsync(orgId, siteId, demande.Nom, ct).ConfigureAwait(false))));
        inventaire.MapPost("/buildings/{batimentId:guid}/levels", async (Guid orgId, Guid siteId, Guid batimentId, DemandeNiveau demande, ServiceStructure service, CancellationToken ct) =>
            Results.Ok(new Cree(await service.CreerNiveauAsync(
                orgId, siteId, batimentId, demande.Nom, demande.Ordre ?? throw new SaisieInvalideException("ordre", "Ordre du niveau obligatoire."), ct)
                .ConfigureAwait(false))));
        inventaire.MapPost("/zones", async (Guid orgId, Guid siteId, DemandeZone demande, ServiceStructure service, CancellationToken ct) =>
            Results.Ok(new Cree(await service.CreerZoneAsync(
                orgId, siteId, demande.BatimentId ?? throw new SaisieInvalideException("batimentId", "Bâtiment obligatoire."), demande.NiveauId, demande.Nom, ct)
                .ConfigureAwait(false))));
        foreach (var (segment, cible) in new[] { ("buildings", CibleInventaire.Batiment), ("levels", CibleInventaire.Niveau), ("zones", CibleInventaire.Zone) })
        {
            inventaire.MapPut($"/{segment}/{{id:guid}}", async (Guid orgId, Guid siteId, Guid id, DemandeNom demande, ServiceStructure service, CancellationToken ct) =>
            {
                await service.RenommerAsync(orgId, siteId, cible, id, demande.Nom, ct).ConfigureAwait(false);
                return Results.NoContent();
            });
        }

        return endpoints;
    }

    private static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (AccesInventaireRefuseException refus)
        {
            return refus.Decision == DecisionAutorisation.Refuse
                ? Probleme(StatusCodes.Status403Forbidden, "Action non permise.", "interdit")
                : Results.NotFound();
        }
        catch (ElementIntrouvableException)
        {
            return Results.NotFound();
        }
        catch (NomExistantException existant)
        {
            return Probleme(StatusCodes.Status409Conflict, existant.Message, "nom_existant");
        }
        catch (ModifieEntreTempsException modifie)
        {
            return Probleme(StatusCodes.Status409Conflict, modifie.Message, "modifie_entre_temps");
        }
        catch (SaisieInvalideException invalide)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Donnée invalide.",
                extensions: new Dictionary<string, object?> { ["code"] = "donnee_invalide", ["champ"] = invalide.Champ });
        }
    }

    private static IResult Probleme(int statut, string titre, string code) =>
        Results.Problem(statusCode: statut, title: titre, extensions: new Dictionary<string, object?> { ["code"] = code });
}

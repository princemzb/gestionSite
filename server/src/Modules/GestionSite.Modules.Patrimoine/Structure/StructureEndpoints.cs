using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.SharedKernel;
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
            .RequireAuthorization().AddEndpointFilter(RefusCommuns.Traduire).AddEndpointFilter(TraduireRefus);
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

    // Refus propres à l'inventaire (structure, composants, grilles et évaluations) ; accès et saisie : RefusCommuns, placé avant.
    internal static async ValueTask<object?> TraduireRefus(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (ElementIntrouvableException)
        {
            return Results.NotFound();
        }
        catch (NomExistantException existant)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, existant.Message, "nom_existant");
        }
        catch (ModifieEntreTempsException modifie)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, modifie.Message, "modifie_entre_temps");
        }
        catch (ComposantRetireException retire)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, retire.Message, "composant_retire");
        }
        catch (GrilleModifieeException modifiee)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, modifiee.Message, "grille_modifiee");
        }
        catch (CommandeReutiliseeException reutilisee)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, reutilisee.Message, "commande_reutilisee");
        }
    }
}

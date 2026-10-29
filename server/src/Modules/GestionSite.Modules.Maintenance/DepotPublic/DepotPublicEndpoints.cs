using System.Text.Json;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Maintenance.DepotPublic;

internal sealed record ReponseSitePublic(string Site, string Organisation);

/// <summary>Formulaire public sans compte (CONTRATS : POST /public/sites/{code}/reports).</summary>
internal static class DepotPublicEndpoints
{
    internal static IEndpointRouteBuilder MapDepotPublic(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.MapGroup("/public/sites/{code}").AllowAnonymous().AddEndpointFilter(TraduireInvalide);
        site.MapGet(string.Empty, async (string code, ServiceDepotPublic service, CancellationToken ct) =>
            await service.SiteAsync(code, ct).ConfigureAwait(false) is { } trouve
                ? Results.Ok(new ReponseSitePublic(trouve.NomSite, trouve.NomOrganisation))
                : Results.NotFound());
        site.MapPost("/reports", async (string code, HttpRequest requete, ServiceDepotPublic service, CancellationToken ct) =>
            {
                var (demande, photos) = await LireAsync(requete, ct).ConfigureAwait(false);
                return await service.DeposerAsync(code, demande, photos, ct).ConfigureAwait(false) is { } accuse
                    ? Results.Ok(accuse)
                    : Results.NotFound();
            })
            // 3 photos de 10 Mo et le reste du formulaire.
            .WithMetadata(new RequestSizeLimitAttribute(CorpsMax))
            .WithFormOptions(multipartBodyLengthLimit: CorpsMax, valueLengthLimit: 64 * 1024);
        return endpoints;
    }

    private const long CorpsMax = (TraitementPhotos.NombreMax * (long)TraitementPhotos.TailleMax) + (1024 * 1024);

    // Lu ici plutôt que lié par le framework : un corps illisible reçoit la même réponse 422 que les autres saisies.
    // JSON seul (sans photo) ou formulaire multipart : partie « demande » (JSON) et fichiers « photos ».
    private static async Task<(DemandeDepot Demande, IReadOnlyList<PhotoRecue> Photos)> LireAsync(
        HttpRequest requete, CancellationToken cancellationToken)
    {
        // Limite de Kestrel (30 Mo par défaut) relevée pour ce seul point d'entrée : 3 photos de 10 Mo et le reste.
        if (requete.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } taille)
        {
            taille.MaxRequestBodySize = CorpsMax;
        }

        try
        {
            if (!requete.HasFormContentType)
            {
                return (await requete.ReadFromJsonAsync<DemandeDepot>(cancellationToken).ConfigureAwait(false) ?? throw Illisible(), []);
            }

            var formulaire = await requete.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var demande = JsonSerializer.Deserialize<DemandeDepot>(formulaire["demande"].ToString(), JsonSerializerOptions.Web) ?? throw Illisible();
            var fichiers = formulaire.Files.GetFiles("photos");
            if (fichiers.Count > TraitementPhotos.NombreMax || fichiers.Any(f => f.Length > TraitementPhotos.TailleMax))
            {
                throw new SaisieInvalideException("photos", "Trois photos au plus, de 10 Mo chacune.");
            }

            var photos = new List<PhotoRecue>(fichiers.Count);
            foreach (var fichier in fichiers)
            {
                using var copie = new MemoryStream((int)fichier.Length);
                await fichier.CopyToAsync(copie, cancellationToken).ConfigureAwait(false);
                photos.Add(new PhotoRecue(copie.ToArray()));
            }

            return (demande, photos);
        }
        catch (BadHttpRequestException tropGros) when (tropGros.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw new SaisieInvalideException("photos", "Trois photos au plus, de 10 Mo chacune.");
        }
        catch (Exception illisible) when (illisible is JsonException or InvalidOperationException or InvalidDataException or IOException)
        {
            throw Illisible(); // dont un envoi interrompu (multipart tronqué)
        }
    }

    private static SaisieInvalideException Illisible() => new("corps", "Corps de requête manquant ou illisible.");

    private static async ValueTask<object?> TraduireInvalide(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (SaisieInvalideException invalide)
        {
            return RefusCommuns.SaisieInvalide(invalide.Champ);
        }
        catch (TropDeDepotsException trop)
        {
            return RefusCommuns.Probleme(StatusCodes.Status429TooManyRequests, trop.Message, "trop_de_depots");
        }
        catch (DepotIndisponibleException indisponible)
        {
            return RefusCommuns.Probleme(StatusCodes.Status503ServiceUnavailable, indisponible.Message, "depot_indisponible");
        }
        catch (CommandeReutiliseeException reutilisee)
        {
            return RefusCommuns.Probleme(StatusCodes.Status409Conflict, reutilisee.Message, "commande_reutilisee");
        }
    }
}

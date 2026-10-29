using System.Text.Json;
using GestionSite.Modules.Maintenance.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
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
        catch (Exception illisible) when (illisible is JsonException or InvalidOperationException or InvalidDataException)
        {
            throw Illisible();
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
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Donnée invalide.",
                extensions: new Dictionary<string, object?> { ["code"] = "donnee_invalide", ["champ"] = invalide.Champ });
        }
        catch (CommandeReutiliseeException reutilisee)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: reutilisee.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "commande_reutilisee" });
        }
    }
}

using System.Text.Json;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace GestionSite.Modules.Maintenance.DepotPublic;

/// <summary>
/// Formulaire avec photos (dépôt public, clôture d'intervention) : JSON seul, ou multipart avec une partie « demande »
/// (JSON) et jusqu'à 3 fichiers « photos » de 10 Mo. Lu ici plutôt que lié par le framework : un corps illisible reçoit
/// la même réponse 422 que les autres saisies.
/// </summary>
internal static class FormulaireAvecPhotos
{
    internal const long CorpsMax = (TraitementPhotos.NombreMax * (long)TraitementPhotos.TailleMax) + (1024 * 1024);

    /// <summary>Limites de taille du point d'entrée : 3 photos de 10 Mo et le reste du formulaire.</summary>
    internal static RouteHandlerBuilder AvecPhotos(this RouteHandlerBuilder route) =>
        route.WithMetadata(new RequestSizeLimitAttribute(CorpsMax)).WithFormOptions(multipartBodyLengthLimit: CorpsMax, valueLengthLimit: 64 * 1024);

    internal static async Task<(T Demande, IReadOnlyList<PhotoRecue> Photos)> LireAsync<T>(HttpRequest requete, CancellationToken cancellationToken)
        where T : class
    {
        // Limite de Kestrel (30 Mo par défaut) relevée pour ce seul point d'entrée.
        if (requete.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } taille)
        {
            taille.MaxRequestBodySize = CorpsMax;
        }

        try
        {
            if (!requete.HasFormContentType)
            {
                return (await requete.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false) ?? throw Illisible(), []);
            }

            var formulaire = await requete.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var demande = JsonSerializer.Deserialize<T>(formulaire["demande"].ToString(), JsonSerializerOptions.Web) ?? throw Illisible();
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
}

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace GestionSite.SharedKernel.Photos;

/// <summary>Formulaire reçu : la demande, ses photos et, s'il est prévu, un document (rapport PDF d'une vérification).</summary>
public sealed record FormulaireRecu<T>(T Demande, IReadOnlyList<PhotoRecue> Photos, byte[]? Document);

/// <summary>
/// Formulaire avec photos (dépôt public, clôture d'intervention) : JSON seul, ou multipart avec une partie « demande »
/// (JSON) et jusqu'à 3 fichiers « photos » de 10 Mo, plus un document de 10 Mo au plus pour les points d'entrée qui en
/// attendent un. Lu ici plutôt que lié par le framework : un corps illisible reçoit la même réponse 422 que les autres saisies.
/// </summary>
public static class FormulaireAvecPhotos
{
    public const long CorpsMax = (TraitementPhotos.NombreMax * (long)TraitementPhotos.TailleMax) + (1024 * 1024);

    /// <summary>Taille maximale d'un document joint (rapport PDF, décision du 30/10).</summary>
    public const int DocumentMax = 10 * 1024 * 1024;

    public const long CorpsAvecDocumentMax = CorpsMax + DocumentMax;

    /// <summary>Limites de taille du point d'entrée : 3 photos de 10 Mo et le reste du formulaire.</summary>
    public static RouteHandlerBuilder AvecPhotos(this RouteHandlerBuilder route) => Limiter(route, CorpsMax);

    /// <summary>Limites de taille du point d'entrée : 3 photos et un document de 10 Mo, et le reste du formulaire.</summary>
    public static RouteHandlerBuilder AvecPhotosEtDocument(this RouteHandlerBuilder route) => Limiter(route, CorpsAvecDocumentMax);

    public static async Task<(T Demande, IReadOnlyList<PhotoRecue> Photos)> LireAsync<T>(HttpRequest requete, CancellationToken cancellationToken)
        where T : class
    {
        var recu = await LireAsync<T>(requete, document: null, CorpsMax, cancellationToken).ConfigureAwait(false);
        return (recu.Demande, recu.Photos);
    }

    /// <summary>Formulaire avec photos et un document facultatif dans la partie <paramref name="document"/> (un seul fichier).</summary>
    public static Task<FormulaireRecu<T>> LireAvecDocumentAsync<T>(HttpRequest requete, string document, CancellationToken cancellationToken)
        where T : class => LireAsync<T>(requete, document, CorpsAvecDocumentMax, cancellationToken);

    private static RouteHandlerBuilder Limiter(RouteHandlerBuilder route, long corpsMax) =>
        route.WithMetadata(new RequestSizeLimitAttribute(corpsMax)).WithFormOptions(multipartBodyLengthLimit: corpsMax, valueLengthLimit: 64 * 1024);

    private static async Task<FormulaireRecu<T>> LireAsync<T>(HttpRequest requete, string? document, long corpsMax, CancellationToken cancellationToken)
        where T : class
    {
        // Limite de Kestrel (30 Mo par défaut) relevée pour ce seul point d'entrée.
        if (requete.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } taille)
        {
            taille.MaxRequestBodySize = corpsMax;
        }

        try
        {
            if (!requete.HasFormContentType)
            {
                return new(await requete.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false) ?? throw Illisible(), [], null);
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
                photos.Add(new PhotoRecue(await LireAsync(fichier, cancellationToken).ConfigureAwait(false)));
            }

            var joints = document is null ? [] : formulaire.Files.GetFiles(document);
            if (joints.Count > 1 || joints.Any(f => f.Length is 0 or > DocumentMax))
            {
                throw new SaisieInvalideException(document!, "Un seul document, de 10 Mo au plus.");
            }

            return new(demande, photos, joints.Count == 1 ? await LireAsync(joints[0], cancellationToken).ConfigureAwait(false) : null);
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

    private static async Task<byte[]> LireAsync(IFormFile fichier, CancellationToken cancellationToken)
    {
        using var copie = new MemoryStream((int)fichier.Length);
        await fichier.CopyToAsync(copie, cancellationToken).ConfigureAwait(false);
        return copie.ToArray();
    }

    private static SaisieInvalideException Illisible() => new("corps", "Corps de requête manquant ou illisible.");
}

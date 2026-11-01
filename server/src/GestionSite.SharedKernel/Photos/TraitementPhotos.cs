using ImageMagick;

namespace GestionSite.SharedKernel.Photos;

/// <summary>Photo reçue d'un formulaire, avant traitement.</summary>
public sealed record PhotoRecue(byte[] Contenu);

/// <summary>
/// Photos des formulaires (dépôt public, décision du 28/10 ; clôture d'intervention ; évaluation) : 3 au plus, 10 Mo
/// chacune, JPEG, PNG ou HEIC, décodées par <see cref="DecodageImage"/> (format imposé, dimensions bornées, orientation
/// appliquée, métadonnées retirées) puis réencodées en JPEG, réduites à 2560 px au plus.
/// </summary>
public static class TraitementPhotos
{
    public const int NombreMax = 3;
    public const int TailleMax = 10 * 1024 * 1024;
    public const uint CoteMax = 2560;
    public const string TypeContenu = "image/jpeg";

    // Au plus deux décodages simultanés : la mémoire du conteneur reste bornée quel que soit le nombre d'envois.
    private static readonly SemaphoreSlim Decodages = new(2);

    public static byte[] Preparer(byte[] contenu)
    {
        if (contenu.Length is 0 or > TailleMax)
        {
            throw new SaisieInvalideException("photos", "Chaque photo doit peser au plus 10 Mo.");
        }

        var format = Format(contenu) ?? throw new SaisieInvalideException("photos", "Photo attendue au format JPEG, PNG ou HEIC.");
        return DecodageImage.Traiter(contenu, format, Decodages, "photos", image =>
        {
            image.Resize(new MagickGeometry(CoteMax, CoteMax) { Greater = true }); // réduit seulement les plus grandes
            image.Format = MagickFormat.Jpeg;
            image.Quality = 85;
            return image.ToByteArray();
        });
    }

    /// <summary>Format d'après les octets d'en-tête, ou null si ce n'est ni JPEG, ni PNG, ni HEIC.</summary>
    public static MagickFormat? Format(ReadOnlySpan<byte> contenu) => DecodageImage.Format(contenu);
}

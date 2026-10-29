using ImageMagick;

namespace GestionSite.SharedKernel.Photos;

/// <summary>Photo reçue d'un formulaire, avant traitement.</summary>
public sealed record PhotoRecue(byte[] Contenu);

/// <summary>
/// Photos des formulaires (dépôt public, décision du 28/10 ; clôture d'intervention ; évaluation) : 3 au plus, 10 Mo chacune, JPEG, PNG ou HEIC. Le format est
/// reconnu par les octets d'en-tête puis imposé au décodeur (jamais deviné : aucun autre format d'ImageMagick
/// n'est atteignable). L'orientation est appliquée, les métadonnées (position GPS comprise) retirées et l'image
/// réencodée en JPEG, réduite à 2560 px au plus.
/// </summary>
public static class TraitementPhotos
{
    public const int NombreMax = 3;
    public const int TailleMax = 10 * 1024 * 1024;
    public const uint CoteMax = 2560;
    public const string TypeContenu = "image/jpeg";

    // Au plus deux décodages simultanés : la mémoire du conteneur reste bornée quel que soit le nombre d'envois.
    private static readonly SemaphoreSlim Decodages = new(2);

    /// <summary>
    /// Bornes d'ImageMagick pour tout le processus, posées au démarrage du module : une image annonçant des
    /// dimensions énormes est refusée avant allocation, sans repli sur le disque.
    /// </summary>
    public static void Configurer()
    {
        ResourceLimits.Width = 12_000;
        ResourceLimits.Height = 12_000;
        ResourceLimits.Area = 50_000_000; // 50 Mpx : au-delà des photos de téléphone courantes
        ResourceLimits.Memory = 512UL * 1024 * 1024;
        ResourceLimits.Disk = 0;
    }

    public static byte[] Preparer(byte[] contenu)
    {
        if (contenu.Length is 0 or > TailleMax)
        {
            throw new SaisieInvalideException("photos", "Chaque photo doit peser au plus 10 Mo.");
        }

        var format = Format(contenu) ?? throw new SaisieInvalideException("photos", "Photo attendue au format JPEG, PNG ou HEIC.");
        Decodages.Wait();
        try
        {
            using var image = new MagickImage(contenu, new MagickReadSettings { Format = format });
            image.AutoOrient();
            // Profil de couleur appliqué avant d'être retiré (ex. Display P3 d'un iPhone) : sinon couleurs faussées.
            if (image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
            }

            image.Strip();
            image.Resize(new MagickGeometry(CoteMax, CoteMax) { Greater = true }); // réduit seulement les plus grandes
            image.Format = MagickFormat.Jpeg;
            image.Quality = 85;
            return image.ToByteArray();
        }
        catch (MagickException)
        {
            throw new SaisieInvalideException("photos", "Photo illisible.");
        }
        finally
        {
            Decodages.Release();
        }
    }

    /// <summary>Format d'après les octets d'en-tête, ou null si ce n'est ni JPEG, ni PNG, ni HEIC.</summary>
    public static MagickFormat? Format(ReadOnlySpan<byte> contenu)
    {
        if (contenu.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return MagickFormat.Jpeg;
        }

        if (contenu.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return MagickFormat.Png;
        }

        // HEIF : boîte « ftyp » en tête, marque principale d'une image HEIC.
        return contenu.Length >= 12 && contenu[4..8].SequenceEqual("ftyp"u8) && MarquesHeic.Contains(System.Text.Encoding.ASCII.GetString(contenu[8..12]))
            ? MagickFormat.Heic
            : null;
    }

    private static readonly HashSet<string> MarquesHeic = new(StringComparer.Ordinal) { "heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1" };
}

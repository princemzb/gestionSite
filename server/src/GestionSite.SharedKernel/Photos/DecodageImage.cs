using System.Buffers.Binary;
using ImageMagick;

namespace GestionSite.SharedKernel.Photos;

/// <summary>
/// Décodage borné commun aux images reçues (photos des formulaires, fonds de plan de bâtiment) : format reconnu par les
/// octets d'en-tête puis imposé au décodeur (jamais deviné : aucun autre format d'ImageMagick n'est atteignable),
/// dimensions lues dans l'en-tête et refusées avant tout décodage au-delà des bornes, orientation appliquée, couleurs
/// ramenées en sRGB, métadonnées (position GPS comprise) retirées. Le réencodage revient à l'appelant.
/// </summary>
public static class DecodageImage
{
    // Places de décodage du processus : deux photos à la fois, ou une seule grande image ; la mémoire d'ImageMagick
    // (LimitesImages.MemoireMax, sans repli sur le disque) couvre le pire cas mesuré de chacune (SOC-04).
    private const int PlacesMax = 2;
    private static readonly SemaphoreSlim Places = new(PlacesMax);
    private static readonly SemaphoreSlim Seules = new(1);

    private static readonly HashSet<string> MarquesHeic = new(StringComparer.Ordinal) { "heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1" };

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

    /// <summary>
    /// Image (photo) décodée puis confiée à <paramref name="traiter"/> (réduction, réencodage), occupant une des places de
    /// décodage du processus. Dimensions annoncées par l'en-tête au-delà des bornes, image illisible ou trop lourde à traiter :
    /// saisie refusée sur <paramref name="champ"/>.
    /// </summary>
    public static T Traiter<T>(byte[] contenu, MagickFormat format, string champ, Func<MagickImage, T> traiter)
    {
        ArgumentNullException.ThrowIfNull(traiter);
        Places.Wait();
        try
        {
            return Decoder(contenu, format, champ, traiter);
        }
        finally
        {
            Places.Release();
        }
    }

    /// <summary>
    /// Grande image (fond de plan, jusqu'à 50 Mpx) traitée seule : elle occupe toutes les places de décodage, la mémoire
    /// d'ImageMagick (commune au processus) lui revient entière. Attente asynchrone, abandonnée avec la requête.
    /// </summary>
    public static async Task<T> TraiterSeuleAsync<T>(
        byte[] contenu, MagickFormat format, string champ, Func<MagickImage, T> traiter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traiter);
        await Seules.WaitAsync(cancellationToken).ConfigureAwait(false);
        var prises = 0;
        try
        {
            // Une seule grande image à la fois : elle prend les places une à une sans jamais en bloquer une autre en attente.
            for (; prises < PlacesMax; prises++)
            {
                await Places.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return Decoder(contenu, format, champ, traiter);
        }
        finally
        {
            if (prises > 0)
            {
                Places.Release(prises);
            }

            Seules.Release();
        }
    }

    // Sous une place de décodage : lecture de l'en-tête comprise, la concurrence d'ImageMagick reste bornée.
    private static T Decoder<T>(byte[] contenu, MagickFormat format, string champ, Func<MagickImage, T> traiter)
    {
        LimitesImages.Poser();
        var reglages = new MagickReadSettings { Format = format };
        ExigerDimensions(contenu, reglages, champ);
        var lue = false;
        try
        {
            using var image = new MagickImage(contenu, reglages);
            image.AutoOrient();
            // Profil de couleur appliqué avant d'être retiré (ex. Display P3 d'un iPhone) : sinon couleurs faussées.
            if (image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
            }

            image.Strip();
            lue = true;
            return traiter(image);
        }
        catch (MagickException erreur)
        {
            // Image lue et convertie : sa réduction et son réencodage n'échouent que faute de mémoire, jamais « illisible ».
            throw lue ? TropGrande(champ) : Refus(erreur, champ);
        }
    }

    // En-tête seul (sans les pixels) : une image de 20 000 px annoncés est refusée sans être décodée. PNG : dimensions lues à
    // leur place fixe (IHDR), libpng refusant lui-même les grandes images sans en dire la raison.
    private static void ExigerDimensions(byte[] contenu, MagickReadSettings reglages, string champ)
    {
        uint largeur;
        uint hauteur;
        try
        {
            if (reglages.Format == MagickFormat.Png && contenu.Length >= 24)
            {
                (largeur, hauteur) = (BinaryPrimitives.ReadUInt32BigEndian(contenu.AsSpan(16)), BinaryPrimitives.ReadUInt32BigEndian(contenu.AsSpan(20)));
            }
            else
            {
                var entete = new MagickImageInfo(contenu, reglages);
                (largeur, hauteur) = (entete.Width, entete.Height);
            }
        }
        catch (MagickException erreur)
        {
            throw Refus(erreur, champ);
        }

        if (largeur > LimitesImages.CoteMax || hauteur > LimitesImages.CoteMax || (ulong)largeur * hauteur > LimitesImages.SurfaceMax)
        {
            throw TropGrande(champ);
        }
    }

    // Bornes d'ImageMagick atteintes (dimensions annoncées, mémoire des pixels épuisée sans repli sur le disque) : image
    // trop grande ; toute autre erreur : illisible.
    private static SaisieInvalideException Refus(MagickException erreur, string champ) =>
        erreur is MagickResourceLimitErrorException or MagickCacheErrorException
        || erreur.Message.StartsWith("WidthOrHeightExceedsLimit", StringComparison.Ordinal)
            ? TropGrande(champ)
            : new SaisieInvalideException(champ, "Image illisible.");

    private static SaisieInvalideException TropGrande(string champ) =>
        new(champ, $"Image trop grande : {LimitesImages.CoteMax} px de côté et {LimitesImages.SurfaceMax / 1_000_000} millions de pixels au plus.");
}

using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using ImageMagick;

namespace GestionSite.Modules.Patrimoine.PlansBatiment;

/// <summary>Image de fond préparée : maître (8 000 px au plus) et dérivé d'affichage (4 096 px au plus), même type.</summary>
internal sealed record FondPrepare(string TypeContenu, byte[] Maitre, int Largeur, int Hauteur, byte[] Affichage, int LargeurAffichage, int HauteurAffichage);

/// <summary>
/// Fonds de plan de bâtiment importés en image (US-21, RP-21.3, S21-04) : un fichier de 25 Mo au plus, JPEG, PNG ou HEIC
/// reconnu par ses octets d'en-tête (jamais par son nom ni son type annoncé : SVG, DWG ou PDF renommés sont refusés),
/// dimensions lues dans l'en-tête et refusées avant décodage au-delà de 12 000 px ou 50 Mpx. Décodage commun aux photos
/// (orientation appliquée, métadonnées retirées), puis deux sorties réencodées : PNG pour un PNG (traits nets d'un plan
/// dessiné), JPEG sinon.
/// </summary>
internal static class TraitementFonds
{
    internal const int TailleMax = 25 * 1024 * 1024;
    internal const uint CoteMaitre = 8_000;
    internal const uint CoteAffichage = 4_096;
    internal const string Champ = "fichier";
    internal const string Png = "image/png";
    internal const string Jpeg = "image/jpeg";

    // Un fond à la fois (jusqu'à 50 Mpx) : la mémoire du conteneur reste bornée, à côté des photos.
    private static readonly SemaphoreSlim Decodages = new(1);

    internal static FondPrepare Preparer(byte[] contenu)
    {
        if (contenu.Length is 0 or > TailleMax)
        {
            throw new SaisieInvalideException(Champ, "Un fichier de 25 Mo au plus.");
        }

        var format = DecodageImage.Format(contenu)
            ?? throw new SaisieInvalideException(Champ, "Image attendue au format JPEG, PNG ou HEIC (pour un plan d'architecte, exportez-le en image ou en PDF).");
        var png = format == MagickFormat.Png;
        return DecodageImage.Traiter(contenu, format, Decodages, Champ, image =>
        {
            // Maître puis dérivé, réduits sur la même image : jamais deux copies en mémoire.
            var maitre = Encoder(image, CoteMaitre, png);
            var (largeur, hauteur) = ((int)image.Width, (int)image.Height);
            var affichage = Encoder(image, CoteAffichage, png);
            return new FondPrepare(png ? Png : Jpeg, maitre, largeur, hauteur, affichage, (int)image.Width, (int)image.Height);
        });
    }

    private static byte[] Encoder(MagickImage image, uint cote, bool png)
    {
        image.Resize(new MagickGeometry(cote, cote) { Greater = true }); // réduit seulement les plus grandes
        if (png)
        {
            image.Format = MagickFormat.Png;
            // Aucun bloc annexe (dates, textes, profils) : seules les données de l'image.
            image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "all");
        }
        else
        {
            image.Format = MagickFormat.Jpeg;
            image.Quality = 90;
        }

        return image.ToByteArray();
    }
}

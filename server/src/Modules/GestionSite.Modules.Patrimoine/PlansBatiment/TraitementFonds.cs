using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using ImageMagick;

namespace GestionSite.Modules.Patrimoine.PlansBatiment;

/// <summary>Image de fond préparée : maître (8 000 px au plus) et dérivé d'affichage (4 096 px au plus), même type.</summary>
internal sealed record FondPrepare(string TypeContenu, byte[] Maitre, int Largeur, int Hauteur, byte[] Affichage, int LargeurAffichage, int HauteurAffichage);

/// <summary>
/// Fonds de plan de bâtiment importés en image (US-21, RP-21.3, S21-04) ou en PDF (S21-05) : un fichier de 25 Mo au plus,
/// JPEG, PNG, HEIC ou PDF reconnu par ses octets d'en-tête (jamais par son nom ni son type annoncé : SVG ou DWG renommés
/// sont refusés), dimensions lues dans l'en-tête et refusées avant décodage au-delà de 12 000 px ou 50 Mpx ; un PDF est
/// d'abord rendu en PNG par le processus enfant (<see cref="ProcessusRenduPdf"/>). Décodage commun aux photos
/// (orientation appliquée, métadonnées retirées), seul à la fois, puis deux sorties réencodées : PNG pour un PNG (traits
/// nets d'un plan dessiné), JPEG sinon, ou quand le PNG réduit dépasserait 25 Mo (scan monochrome devenu nuances de gris).
/// </summary>
internal static class TraitementFonds
{
    internal const int TailleMax = 25 * 1024 * 1024;
    internal const uint CoteMaitre = 8_000;
    internal const uint CoteAffichage = 4_096;
    internal const string Champ = "fichier";
    internal const string Png = "image/png";
    internal const string Jpeg = "image/jpeg";

    /// <summary>
    /// Surface d'une page de PDF rendue : 46 Mpx, sous les 50 Mpx des images importées. Au-delà d'environ 48 Mpx, le
    /// réencodage PNG dépasse la mémoire accordée à ImageMagick (LimitesImages.MemoireMax, constaté le 01/11/2026) ; une
    /// page au format A (rapport √2) garde ses 8 000 px de grand côté (45,3 Mpx), une page carrée est rendue en 6 782 px.
    /// </summary>
    internal const ulong SurfacePdf = 46_000_000;

    /// <summary>PDF reconnu par sa signature en tête (« %PDF- »), jamais par son nom ni son type annoncé.</summary>
    internal static bool EstPdf(ReadOnlySpan<byte> contenu) => contenu.StartsWith("%PDF-"u8);

    internal static Task<FondPrepare> PreparerAsync(byte[] contenu, CancellationToken cancellationToken)
    {
        ExigerTaille(contenu);
        var format = DecodageImage.Format(contenu)
            ?? throw new SaisieInvalideException(Champ, "Image JPEG, PNG ou HEIC, ou PDF, attendu (pour un plan DWG, exportez-le en PDF à l'échelle).");
        return Reencoder(contenu, format, cancellationToken);
    }

    /// <summary>
    /// Page d'un PDF (S21-05) rendue dans le processus enfant (8 000 px de côté et 46 Mpx au plus), puis traitée comme un
    /// PNG importé : mêmes bornes vérifiées, mêmes sorties.
    /// </summary>
    internal static async Task<(FondPrepare Fond, PagePdfRendue Page)> PreparerPdfAsync(
        byte[] contenu, int page, ProcessusRenduPdf rendu, CancellationToken cancellationToken)
    {
        ExigerTaille(contenu);
        var rendue = await rendu.RendreAsync(contenu, page, CoteMaitre, SurfacePdf, cancellationToken).ConfigureAwait(false);
        return (await Reencoder(rendue.Png, MagickFormat.Png, cancellationToken).ConfigureAwait(false), rendue);
    }

    private static void ExigerTaille(byte[] contenu)
    {
        if (contenu.Length is 0 or > TailleMax)
        {
            throw new SaisieInvalideException(Champ, "Un fichier de 25 Mo au plus.");
        }
    }

    private static Task<FondPrepare> Reencoder(byte[] contenu, MagickFormat format, CancellationToken cancellationToken) =>
        DecodageImage.TraiterSeuleAsync(contenu, format, Champ, image =>
        {
            // Maître puis dérivé, réduits sur la même image : jamais deux copies en mémoire.
            image.Resize(new MagickGeometry(CoteMaitre, CoteMaitre) { Greater = true }); // réduit seulement les plus grandes
            var png = format == MagickFormat.Png;
            var maitre = Encoder(image, png);
            if (png && maitre.Length > TailleMax)
            {
                png = false;
                maitre = Encoder(image, png);
            }

            var (largeur, hauteur) = ((int)image.Width, (int)image.Height);
            image.Resize(new MagickGeometry(CoteAffichage, CoteAffichage) { Greater = true });
            var affichage = Encoder(image, png);
            return new FondPrepare(png ? Png : Jpeg, maitre, largeur, hauteur, affichage, (int)image.Width, (int)image.Height);
        }, cancellationToken);

    private static byte[] Encoder(MagickImage image, bool png)
    {
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

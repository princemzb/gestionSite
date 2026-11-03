using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PDFiumCore;

namespace GestionSite.Modules.Patrimoine.RenduPdf;

/// <summary>
/// Rendu d'une page de PDF en PNG (US-21, RP-21.3, S21-05), exécuté dans un processus enfant borné par l'API. Arguments :
/// numéro de page (à partir de 1), plus grand côté et surface maximale de l'image en pixels, mémoire maximale du processus
/// en octets. Entrée : le PDF (32 Mo lus au plus). Sortie : une ligne d'en-tête ASCII puis, pour un rendu réussi, le PNG :
/// <list type="bullet">
/// <item><c>OK pages largeurPt hauteurPt largeurPx hauteurPx taillePng</c> suivie du PNG (RVB 8 bits, fond blanc) ;</item>
/// <item><c>CHIFFRE</c> : PDF chiffré (mot de passe ou simples restrictions), jamais rendu ;</item>
/// <item><c>PAGE pages</c> : page demandée absente ;</item>
/// <item><c>ILLISIBLE</c> : pas un PDF, PDF corrompu, page vide ou image impossible à allouer.</item>
/// </list>
/// Tout autre fin (arrêt brutal de PDFium, mémoire épuisée, délai dépassé) est un refus côté API.
/// </summary>
internal static class RenduPage
{
    internal const int CodeUsage = 64;
    internal const int CodeLimites = 65;
    internal const int EntreeMax = 32 * 1024 * 1024;

    // fpdfview.h : erreurs de chargement liées au chiffrement, format de l'image, annotations dessinées.
    private const uint ErreurMotDePasse = 4;
    private const uint ErreurSecurite = 5;
    private const int FormatBgr = 2;
    private const int RenduAnnotations = 0x01;
    private const uint Blanc = 0xFFFFFFFF;

    internal static int Executer(IReadOnlyList<string> args, Stream entree, Stream sortie, Action<long> poserLimites)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(poserLimites);
        if (args.Count != 4 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var cote)
            || !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var surface)
            || !long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var memoire)
            || page < 1 || cote < 1 || surface < 1 || memoire < 1)
        {
            return CodeUsage;
        }

        poserLimites(memoire);
        var pdf = Lire(entree);
        if ((pdf is null ? "ILLISIBLE\n" : Rendre(pdf, page, cote, surface, sortie)) is { } refus)
        {
            sortie.Write(Encoding.ASCII.GetBytes(refus));
        }

        sortie.Flush();
        return 0;
    }

    /// <summary>Dimensions de l'image : plus grand côté ≤ <paramref name="cote"/>, surface ≤ <paramref name="surface"/>.</summary>
    internal static (int Largeur, int Hauteur) Dimensions(double largeurPt, double hauteurPt, int cote, long surface)
    {
        var echelle = Math.Min(cote / Math.Max(largeurPt, hauteurPt), Math.Sqrt(surface / (largeurPt * hauteurPt)));
        return (Math.Clamp((int)Math.Floor(largeurPt * echelle), 1, cote), Math.Clamp((int)Math.Floor(hauteurPt * echelle), 1, cote));
    }

    // PDF reconnu par sa signature en tête, jamais par son nom ; au-delà de la borne, refusé sans lecture complète.
    private static byte[]? Lire(Stream entree)
    {
        using var tampon = new MemoryStream();
        var bloc = new byte[81920];
        int lus;
        while ((lus = entree.Read(bloc, 0, bloc.Length)) > 0)
        {
            if (tampon.Length + lus > EntreeMax)
            {
                return null;
            }

            tampon.Write(bloc, 0, lus);
        }

        var contenu = tampon.ToArray();
        return contenu.AsSpan().StartsWith("%PDF-"u8) ? contenu : null;
    }

    // Rendu réussi : en-tête et PNG écrits sur la sortie, rien n'est rendu ; sinon, la ligne de refus à écrire.
    private static string? Rendre(byte[] pdf, int numero, int cote, long surface, Stream sortie)
    {
        fpdfview.FPDF_InitLibrary();
        var epingle = GCHandle.Alloc(pdf, GCHandleType.Pinned); // lu par PDFium jusqu'à la fermeture du document
        try
        {
            var document = fpdfview.FPDF_LoadMemDocument(epingle.AddrOfPinnedObject(), pdf.Length, null);
            if (document is null || document.__Instance == IntPtr.Zero)
            {
                return fpdfview.FPDF_GetLastError() is ErreurMotDePasse or ErreurSecurite ? "CHIFFRE\n" : "ILLISIBLE\n";
            }

            try
            {
                return RendrePage(document, numero, cote, surface, sortie);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(document);
            }
        }
        finally
        {
            epingle.Free();
        }
    }

    private static string? RendrePage(FpdfDocumentT document, int numero, int cote, long surface, Stream sortie)
    {
        // Chiffré sans mot de passe d'ouverture (simples restrictions) : refusé aussi (RP-21.3).
        if (fpdfview.FPDF_GetSecurityHandlerRevision(document) != -1)
        {
            return "CHIFFRE\n";
        }

        var pages = fpdfview.FPDF_GetPageCount(document);
        if (pages < 1)
        {
            return "ILLISIBLE\n";
        }

        if (numero > pages)
        {
            return string.Create(CultureInfo.InvariantCulture, $"PAGE {pages}\n");
        }

        var page = fpdfview.FPDF_LoadPage(document, numero - 1);
        if (page is null || page.__Instance == IntPtr.Zero)
        {
            return "ILLISIBLE\n";
        }

        try
        {
            // Dimensions après la rotation propre à la page, en points (1/72 de pouce).
            double largeurPt = fpdfview.FPDF_GetPageWidthF(page);
            double hauteurPt = fpdfview.FPDF_GetPageHeightF(page);
            if (!double.IsFinite(largeurPt) || !double.IsFinite(hauteurPt) || largeurPt < 1 || hauteurPt < 1)
            {
                return "ILLISIBLE\n";
            }

            var (largeur, hauteur) = Dimensions(largeurPt, hauteurPt, cote, surface);
            var image = fpdfview.FPDFBitmapCreateEx(largeur, hauteur, FormatBgr, IntPtr.Zero, 0);
            if (image is null || image.__Instance == IntPtr.Zero)
            {
                return "ILLISIBLE\n";
            }

            try
            {
                fpdfview.FPDFBitmapFillRect(image, 0, 0, largeur, hauteur, Blanc);
                fpdfview.FPDF_RenderPageBitmap(image, page, 0, 0, largeur, hauteur, 0, RenduAnnotations);
                using var png = Png.Encoder(fpdfview.FPDFBitmapGetBuffer(image), largeur, hauteur, fpdfview.FPDFBitmapGetStride(image));
                var entete = string.Create(
                    CultureInfo.InvariantCulture, $"OK {pages} {largeurPt:R} {hauteurPt:R} {largeur} {hauteur} {png.Length}\n");
                sortie.Write(Encoding.ASCII.GetBytes(entete));
                png.EcrireVers(sortie);
                return null;
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(image);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }
}

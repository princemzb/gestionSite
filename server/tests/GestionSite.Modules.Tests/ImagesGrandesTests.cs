using GestionSite.Modules.Patrimoine.PlansBatiment;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using GestionSite.Tests;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Tests;

/// <summary>Collection jouée seule : la mémoire d'ImageMagick est commune au processus de test.</summary>
[CollectionDefinition(Nom, DisableParallelization = true)]
public sealed class DecodagesSeuls
{
    public const string Nom = "Décodages d'images de 50 Mpx";
}

/// <summary>
/// Images de 50 Mpx (SOC-04) : juste sous la borne, les formes les plus coûteuses en mémoire mesurées le 01/11/2026 sont
/// acceptées (fond seul, deux photos à la fois, page de PDF carrée) ; mémoire épuisée : « Image trop grande », jamais
/// « illisible ». Juste au-dessus de la borne : TraitementFondsTests et TraitementPhotosTests. Images synthétiques.
/// </summary>
[Collection(DecodagesSeuls.Nom)]
public sealed class ImagesGrandesTests
{
    [Theory]
    [InlineData(7_071, 7_071, 6, 8, 7_071, 7_071)] // RVBA carré, non réduit : 668 Mio mesurés (défaut d'origine dès 48 Mpx en RVB)
    [InlineData(7_071, 7_071, 6, 16, 7_071, 7_071)] // RVBA 16 bits : 668 Mio
    [InlineData(7_071, 7_071, 2, 8, 7_071, 7_071)] // RVB carré : 525 Mio
    [InlineData(8_409, 5_946, 3, 8, 8_000, 5_657)] // palette, réduit : 681 Mio
    [InlineData(10_000, 5_000, 6, 8, 8_000, 4_000)] // 50 000 000 px exactement
    public async Task AFiftyMegapixelPngIsAcceptedAsAFond(int largeur, int hauteur, byte typeCouleur, byte profondeur, int largeurMaitre, int hauteurMaitre)
    {
        var fond = await TraitementFonds.PreparerAsync(EssaisImages.PngRaye(largeur, hauteur, typeCouleur, profondeur), CancellationToken.None);

        Assert.Equal(TraitementFonds.Png, fond.TypeContenu);
        Assert.Equal((largeurMaitre, hauteurMaitre), (fond.Largeur, fond.Hauteur));
        Assert.Equal(TraitementFonds.CoteAffichage, (uint)Math.Max(fond.LargeurAffichage, fond.HauteurAffichage));
    }

    [Fact]
    public async Task TwoFiftyMegapixelPhotosAreProcessedAtTheSameTime()
    {
        // Pire photo mesurée : JPEG CMJN avec profil, à tourner (382 Mio) ; deux à la fois occupent les deux places.
        var jpeg = JpegCmjnTourne(8_164, 6_124); // 49 996 336 px
        using var depart = new Barrier(2);

        var photos = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            depart.SignalAndWait();
            return TraitementPhotos.Preparer(jpeg);
        })));

        foreach (var photo in photos)
        {
            using var relue = new MagickImage(photo);
            Assert.Equal((1_920u, 2_560u), (relue.Width, relue.Height)); // tournée puis réduite
            Assert.Equal(ColorSpace.sRGB, relue.ColorSpace);
        }
    }

    [Fact]
    public async Task ASquarePdfPageIsRenderedAtFiftyMegapixels()
    {
        var pdf = PdfSynthetique.Pages((300, 300, (0, 0, 1)));
        var rendu = new ProcessusRenduPdf(Options.Create(new FondsPlanOptions()), NullLogger<ProcessusRenduPdf>.Instance);

        var (fond, page) = await TraitementFonds.PreparerPdfAsync(pdf, 1, rendu, CancellationToken.None);

        Assert.Equal((7_071, 7_071), (fond.Largeur, fond.Hauteur)); // 49 999 041 px, sous la borne mémoire de l'enfant (Linux)
        Assert.Equal(TraitementFonds.Png, fond.TypeContenu);
        Assert.Equal(1, page.Pages);
    }

    [Theory]
    [InlineData(8, false, 2_000, false)] // les pixels décodés (11,4 Mio) ne tiennent pas
    [InlineData(8, true, 2_000, false)]
    [InlineData(16, false, 2_000, true)] // fond décodé, mais pas réencodé en PNG
    [InlineData(30, true, 3_000, true)] // photo décodée (25,7 Mio), mais pas réduite à 2 560 px
    public async Task ExhaustedMemoryIsReportedAsTooLargeNeverUnreadable(ulong memoireMio, bool photo, int cote, bool decodable)
    {
        var png = EssaisImages.PngRaye(cote, cote, 2);
        LimitesImages.Poser();
        ResourceLimits.Memory = memoireMio * 1024 * 1024;
        try
        {
            // Phase visée vérifiée : une mise à jour d'ImageMagick qui la déplacerait fait échouer l'essai.
            Assert.Equal(decodable, Decodable(png));
            var refus = photo
                ? Assert.Throws<SaisieInvalideException>(() => TraitementPhotos.Preparer(png))
                : await Assert.ThrowsAsync<SaisieInvalideException>(() => TraitementFonds.PreparerAsync(png, CancellationToken.None));

            Assert.Equal(photo ? "photos" : "fichier", refus.Champ);
            Assert.StartsWith("Image trop grande pour être traitée", refus.Message, StringComparison.Ordinal);
        }
        finally
        {
            ResourceLimits.Memory = LimitesImages.MemoireMax;
        }
    }

    private static bool Decodable(byte[] png)
    {
        try
        {
            using var image = new MagickImage(png, new MagickReadSettings { Format = MagickFormat.Png });
            return true;
        }
        catch (MagickCacheErrorException)
        {
            return false;
        }
    }

    // JPEG en CMJN (profil USWebCoatedSWOP) dont l'EXIF demande une rotation d'un quart de tour.
    private static byte[] JpegCmjnTourne(uint largeur, uint hauteur)
    {
        using var source = new MagickImage(MagickColors.Orange, largeur, hauteur);
        source.SetProfile(ColorProfiles.SRGB);
        source.TransformColorSpace(ColorProfiles.USWebCoatedSWOP);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        source.SetProfile(exif);
        source.Orientation = Orientation.RightTop;
        source.Format = MagickFormat.Jpeg;
        source.Quality = 90;
        var jpeg = source.ToByteArray();
        Assert.True(jpeg.Length <= TraitementPhotos.TailleMax);
        return jpeg;
    }
}

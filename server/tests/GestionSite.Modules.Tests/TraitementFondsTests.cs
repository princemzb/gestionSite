using System.Buffers.Binary;
using GestionSite.Modules.Patrimoine.PlansBatiment;
using GestionSite.SharedKernel;
using ImageMagick;

namespace GestionSite.Modules.Tests;

/// <summary>
/// Fonds de plan de bâtiment (S21-04) : formats reconnus par leurs octets, dimensions refusées avant décodage,
/// métadonnées retirées, maître de 8 000 px et dérivé de 4 096 px. Images synthétiques seulement.
/// </summary>
public sealed class TraitementFondsTests
{
    [Fact]
    public async Task AJpegCarryingALocationIsStoredWithoutAnyMetadata()
    {
        var fond = await TraitementFonds.PreparerAsync(EssaisImages.JpegAvecPosition(1200, 800), CancellationToken.None);

        Assert.Equal(TraitementFonds.Jpeg, fond.TypeContenu);
        foreach (var sortie in new[] { fond.Maitre, fond.Affichage })
        {
            using var relue = new MagickImage(sortie);
            Assert.Equal(MagickFormat.Jpeg, relue.Format);
            Assert.Null(relue.GetExifProfile());
            Assert.Null(relue.GetXmpProfile());
            Assert.Null(relue.GetIptcProfile());
            Assert.Equal(-1, sortie.AsSpan().IndexOf("Exif"u8));
            Assert.Equal(-1, sortie.AsSpan().IndexOf("Plan synthétique"u8));
        }

        Assert.Equal((1200, 800, 1200, 800), (fond.Largeur, fond.Hauteur, fond.LargeurAffichage, fond.HauteurAffichage));
    }

    [Fact]
    public async Task ALargeImageGivesAMasterOf8000AndADisplayCopyOf4096()
    {
        using var source = new MagickImage(MagickColors.White, 9_000, 3_000) { Format = MagickFormat.Jpeg };

        var fond = await TraitementFonds.PreparerAsync(source.ToByteArray(), CancellationToken.None);

        Assert.Equal((8_000, 2_667), (fond.Largeur, fond.Hauteur));
        Assert.Equal((4_096, 1_366), (fond.LargeurAffichage, fond.HauteurAffichage));
        using var affichage = new MagickImage(fond.Affichage);
        Assert.Equal((4_096u, 1_366u), (affichage.Width, affichage.Height));
    }

    [Fact]
    public async Task APngStaysAPngWithoutAncillaryChunksAndKeepsItsOrientation()
    {
        using var source = new MagickImage(MagickColors.Black, 300, 200) { Format = MagickFormat.Png };
        source.SetAttribute("comment", "Plan synthétique — Paroisse fictive");

        var fond = await TraitementFonds.PreparerAsync(source.ToByteArray(), CancellationToken.None);

        Assert.Equal(TraitementFonds.Png, fond.TypeContenu);
        using var relue = new MagickImage(fond.Maitre);
        Assert.Equal(MagickFormat.Png, relue.Format);
        Assert.Equal((300u, 200u), (relue.Width, relue.Height));
        foreach (var bloc in new[] { "tEXt"u8.ToArray(), "zTXt"u8.ToArray(), "iTXt"u8.ToArray(), "tIME"u8.ToArray(), "eXIf"u8.ToArray() })
        {
            Assert.Equal(-1, fond.Maitre.AsSpan().IndexOf(bloc));
        }
    }

    [Fact]
    public async Task AMonochromeScanWhosePngWouldGrowPastTheLimitBecomesAJpeg()
    {
        // Scan noir et blanc compressé à 1 bit par pixel : réduit, il passe en nuances de gris et son PNG dépasserait 25 Mo.
        using var scan = new MagickImage(MagickColors.White, 8_200, 3_600);
        scan.AddNoise(NoiseType.Random);
        scan.Threshold(new Percentage(50));
        scan.ColorType = ColorType.Bilevel;
        scan.Format = MagickFormat.Png;
        var contenu = scan.ToByteArray();
        Assert.True(contenu.Length < TraitementFonds.TailleMax);

        var fond = await TraitementFonds.PreparerAsync(contenu, CancellationToken.None);

        Assert.Equal(TraitementFonds.Jpeg, fond.TypeContenu);
        Assert.True(fond.Maitre.Length <= TraitementFonds.TailleMax);
        using var affichage = new MagickImage(fond.Affichage);
        Assert.Equal(MagickFormat.Jpeg, affichage.Format);
        Assert.Equal((8_000, 3_512), (fond.Largeur, fond.Hauteur));
    }

    [Fact]
    public async Task ARotatedPhotoOfAPlanIsTurnedUpright()
    {
        using var source = new MagickImage(MagickColors.Gray, 40, 20);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        source.SetProfile(exif);
        source.Orientation = Orientation.RightTop; // à tourner de 90° à l'affichage
        source.Format = MagickFormat.Jpeg;

        var fond = await TraitementFonds.PreparerAsync(source.ToByteArray(), CancellationToken.None);

        Assert.Equal((20, 40), (fond.Largeur, fond.Hauteur));
    }

    [Fact]
    public async Task AnImageDeclaringTwentyThousandPixelsIsRefusedWithoutDecoding()
    {
        var refus = await Assert.ThrowsAsync<SaisieInvalideException>(() => TraitementFonds.PreparerAsync(EssaisImages.PngAnnonce(20_000, 20_000), CancellationToken.None));

        Assert.Equal("fichier", refus.Champ);
        Assert.StartsWith("Image trop grande", refus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJpegDeclaringTwentyThousandPixelsIsRefusedWithoutDecoding()
    {
        var refus = await Assert.ThrowsAsync<SaisieInvalideException>(() => TraitementFonds.PreparerAsync(EssaisImages.JpegAnnonce(20_000, 600), CancellationToken.None));

        Assert.StartsWith("Image trop grande", refus.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(11_000, 11_000)]
    [InlineData(7_072, 7_071)] // 50 006 112 px : juste au-dessus des 50 Mpx (7 071 × 7 071 est accepté, ImagesGrandesTests)
    [InlineData(10_000, 5_001)]
    public async Task TooManyPixelsAreRefusedEvenUnderTheSideLimit(uint largeur, uint hauteur)
    {
        var refus = await Assert.ThrowsAsync<SaisieInvalideException>(() => TraitementFonds.PreparerAsync(EssaisImages.PngAnnonce(largeur, hauteur), CancellationToken.None));

        Assert.StartsWith("Image trop grande", refus.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("dwg")]
    [InlineData("pdf")]
    [InlineData("vide")]
    [InlineData("trop-gros")]
    [InlineData("jpeg-tronque")]
    public async Task AnythingElseIsRefusedOnTheFile(string cas)
    {
        byte[] contenu = cas switch
        {
            "svg" => "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><rect width=\"10\" height=\"10\"/></svg>"u8.ToArray(),
            "dwg" => [.. "AC1032"u8, .. new byte[64]],
            "pdf" => "%PDF-1.7\n%%EOF\n"u8.ToArray(),
            "vide" => [],
            "trop-gros" => [0xFF, 0xD8, 0xFF, .. new byte[TraitementFonds.TailleMax]],
            _ => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10],
        };

        var refus = await Assert.ThrowsAsync<SaisieInvalideException>(() => TraitementFonds.PreparerAsync(contenu, CancellationToken.None));

        Assert.Equal("fichier", refus.Champ);
    }
}

/// <summary>Images d'essai synthétiques (jamais un plan réel).</summary>
internal static class EssaisImages
{
    /// <summary>JPEG portant une position GPS, une description et un commentaire.</summary>
    internal static byte[] JpegAvecPosition(uint largeur, uint hauteur)
    {
        using var source = new MagickImage(MagickColors.LightGray, largeur, hauteur);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(48, 1), new Rational(51, 1), new Rational(24, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(2, 1), new Rational(21, 1), new Rational(3, 1)]);
        exif.SetValue(ExifTag.ImageDescription, "Plan synthétique");
        source.SetProfile(exif);
        source.SetAttribute("comment", "Plan synthétique");
        source.Format = MagickFormat.Jpeg;
        var jpeg = source.ToByteArray();
        using var relue = new MagickImage(jpeg);
        Assert.NotNull(relue.GetExifProfile()?.GetValue(ExifTag.GPSLatitude)); // l'essai porte bien une position
        return jpeg;
    }

    /// <summary>Vrai PNG de 8 × 8 px dont l'en-tête annonce d'autres dimensions (somme de contrôle recalculée).</summary>
    internal static byte[] PngAnnonce(uint largeur, uint hauteur)
    {
        using var petit = new MagickImage(MagickColors.White, 8, 8) { Format = MagickFormat.Png };
        var octets = petit.ToByteArray();
        // Signature (8), puis IHDR : longueur (4), type (4), largeur (4), hauteur (4), 5 octets, somme de contrôle.
        BinaryPrimitives.WriteUInt32BigEndian(octets.AsSpan(16), largeur);
        BinaryPrimitives.WriteUInt32BigEndian(octets.AsSpan(20), hauteur);
        BinaryPrimitives.WriteUInt32BigEndian(octets.AsSpan(29), Crc32(octets.AsSpan(12, 17)));
        return octets;
    }

    /// <summary>Vrai JPEG de 8 × 8 px dont l'en-tête de trame (SOF0) annonce d'autres dimensions.</summary>
    internal static byte[] JpegAnnonce(ushort largeur, ushort hauteur)
    {
        using var petit = new MagickImage(MagickColors.White, 8, 8) { Format = MagickFormat.Jpeg };
        petit.Settings.Interlace = Interlace.NoInterlace;
        var octets = petit.ToByteArray();
        var trame = octets.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xC0]);
        Assert.True(trame > 0);
        // Marqueur (2), longueur (2), précision (1), hauteur (2), largeur (2).
        BinaryPrimitives.WriteUInt16BigEndian(octets.AsSpan(trame + 5), hauteur);
        BinaryPrimitives.WriteUInt16BigEndian(octets.AsSpan(trame + 7), largeur);
        return octets;
    }

    /// <summary>
    /// PNG écrit sans ImageMagick (aucune mémoire d'ImageMagick prise pour le préparer) : fond blanc rayé de noir, en RVB,
    /// RVBA (8 ou 16 bits par composante) ou palette, données compressées en un seul bloc.
    /// </summary>
    internal static byte[] PngRaye(int largeur, int hauteur, byte typeCouleur, byte profondeur = 8)
    {
        var octetsParComposante = profondeur / 8;
        var octetsParPixel = typeCouleur switch { 2 => 3, 6 => 4, _ => 1 } * octetsParComposante;
        var blanche = new byte[1 + (largeur * octetsParPixel)]; // octet de filtre (0 : aucun) puis les pixels
        var noire = new byte[blanche.Length];
        if (typeCouleur != 3)
        {
            blanche.AsSpan(1).Fill(0xFF);
            for (var x = 1 + octetsParPixel - octetsParComposante; typeCouleur == 6 && x < noire.Length; x += octetsParPixel)
            {
                noire.AsSpan(x, octetsParComposante).Fill(0xFF); // noir opaque
            }
        }
        else
        {
            noire.AsSpan(1).Fill(1); // palette : 0 blanc, 1 noir
        }

        using var donnees = new MemoryStream();
        using (var compression = new System.IO.Compression.ZLibStream(donnees, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < hauteur; y++)
            {
                compression.Write(y % 100 < 2 ? noire : blanche);
            }
        }

        var entete = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(entete, largeur);
        BinaryPrimitives.WriteInt32BigEndian(entete.AsSpan(4), hauteur);
        (entete[8], entete[9]) = (profondeur, typeCouleur);
        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Bloc(png, "IHDR"u8, entete);
        if (typeCouleur == 3)
        {
            Bloc(png, "PLTE"u8, [0xFF, 0xFF, 0xFF, 0, 0, 0]);
        }

        Bloc(png, "IDAT"u8, donnees.GetBuffer().AsSpan(0, (int)donnees.Length));
        Bloc(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Bloc(Stream sortie, ReadOnlySpan<byte> type, ReadOnlySpan<byte> contenu)
    {
        Span<byte> quatre = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(quatre, contenu.Length);
        sortie.Write(quatre);
        byte[] typeEtContenu = [.. type, .. contenu];
        sortie.Write(typeEtContenu);
        BinaryPrimitives.WriteUInt32BigEndian(quatre, Crc32(typeEtContenu));
        sortie.Write(quatre);
    }

    private static uint Crc32(ReadOnlySpan<byte> octets)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var octet in octets)
        {
            crc ^= octet;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}

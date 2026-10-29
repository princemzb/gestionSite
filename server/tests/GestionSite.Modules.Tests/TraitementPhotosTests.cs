using GestionSite.SharedKernel.Photos;
using GestionSite.SharedKernel;
using ImageMagick;

namespace GestionSite.Modules.Tests;

/// <summary>Photos du dépôt public (S08-02) : métadonnées retirées, orientation appliquée, formats et tailles bornés.</summary>
public sealed class TraitementPhotosTests
{
    public TraitementPhotosTests() => TraitementPhotos.Configurer(); // comme au démarrage du module

    [Fact]
    public void DecodingIsBoundedWithoutDiskFallback()
    {
        Assert.Equal(12_000u, ResourceLimits.Width);
        Assert.Equal(50_000_000ul, ResourceLimits.Area);
        Assert.Equal(0ul, ResourceLimits.Disk);
        Assert.True(ResourceLimits.Memory <= 512ul * 1024 * 1024);
    }

    [Fact]
    public void LocationAndOtherMetadataAreRemovedAndOrientationIsApplied()
    {
        using var source = new MagickImage(MagickColors.Red, 40, 20);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        source.SetProfile(exif);
        source.Orientation = Orientation.RightTop; // écrit dans l'EXIF : à tourner de 90° à l'affichage
        source.Format = MagickFormat.Jpeg;
        var photo = source.ToByteArray();
        using (var relue = new MagickImage(photo))
        {
            Assert.Equal(Orientation.RightTop, relue.Orientation); // la photo de départ porte bien l'orientation
        }

        using var resultat = new MagickImage(TraitementPhotos.Preparer(photo));

        Assert.Equal(MagickFormat.Jpeg, resultat.Format);
        Assert.Null(resultat.GetExifProfile());
        Assert.Equal((20u, 40u), (resultat.Width, resultat.Height));
    }

    [Fact]
    public void APngBecomesAJpegAndALargeImageIsReduced()
    {
        using var source = new MagickImage(MagickColors.Blue, 4000, 1000) { Format = MagickFormat.Png };

        using var resultat = new MagickImage(TraitementPhotos.Preparer(source.ToByteArray()));

        Assert.Equal(MagickFormat.Jpeg, resultat.Format);
        Assert.Equal((2560u, 640u), (resultat.Width, resultat.Height));
    }

    [Theory]
    [InlineData("texte")]
    [InlineData("vide")]
    [InlineData("heic-tronque")]
    [InlineData("gif")]
    public void AnythingElseIsAnInputError(string cas)
    {
        byte[] contenu = cas switch
        {
            "texte" => "<svg onload=alert(1)>"u8.ToArray(),
            "vide" => [],
            "heic-tronque" => [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0],
            _ => "GIF89a"u8.ToArray(),
        };

        var refus = Assert.Throws<SaisieInvalideException>(() => TraitementPhotos.Preparer(contenu));

        Assert.Equal("photos", refus.Champ);
    }

    [Fact]
    public void APhotoOverTenMegabytesIsRefusedBeforeDecoding()
    {
        var contenu = new byte[TraitementPhotos.TailleMax + 1];
        contenu[0] = 0xFF;
        contenu[1] = 0xD8;
        contenu[2] = 0xFF;

        Assert.Throws<SaisieInvalideException>(() => TraitementPhotos.Preparer(contenu));
    }

    [Theory]
    [InlineData("heic", true)]
    [InlineData("mif1", true)]
    [InlineData("avif", false)]
    public void HeicIsRecognisedByItsBrand(string marque, bool heic)
    {
        byte[] entete = [0, 0, 0, 24, .. "ftyp"u8, .. System.Text.Encoding.ASCII.GetBytes(marque)];

        Assert.Equal(heic, TraitementPhotos.Format(entete) == MagickFormat.Heic);
    }
}

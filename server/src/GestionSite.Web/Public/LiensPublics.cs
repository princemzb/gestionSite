using Microsoft.Extensions.Options;
using QRCoder;

namespace GestionSite.Web.Public;

/// <summary>Lien public d'un site (/s/{code}) et son QR code (US-08, RG-03).</summary>
internal sealed class LiensPublics(IOptions<WebOptions> options)
{
    /// <summary>Adresse publique du formulaire du site ; <paramref name="baseRequete"/> si aucune adresse publique n'est configurée.</summary>
    internal string Adresse(string code, string baseRequete) => $"{AdressePublique.Racine(options.Value, baseRequete)}s/{code}";

    /// <summary>QR en SVG (affichage, affiche) : contenu généré par l'application, sans saisie de l'utilisateur.</summary>
    internal static string Svg(string adresse)
    {
        using var donnees = QRCodeGenerator.GenerateQrCode(adresse, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(donnees).GetGraphic(10, "#000740", "#FFFFFF", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    /// <summary>QR en PNG (téléchargement, impression dans d'autres documents).</summary>
    internal static byte[] Png(string adresse)
    {
        using var donnees = QRCodeGenerator.GenerateQrCode(adresse, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(donnees).GetGraphic(20);
    }
}

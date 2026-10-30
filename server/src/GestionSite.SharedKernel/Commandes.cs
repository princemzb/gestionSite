using System.Security.Cryptography;
using System.Text;

namespace GestionSite.SharedKernel;

/// <summary>
/// Identifiants d'envoi dérivés (US-12) : une saisie qui écrit dans plusieurs modules ou plusieurs lignes (évaluation
/// d'une inspection, dossier d'un constat) donne à chaque écriture un identifiant déterministe tiré de l'envoi d'origine
/// et d'un usage. Un rejeu, même après un échec partiel, retrouve ainsi chaque écriture déjà faite au lieu de la doubler.
/// </summary>
public static class Commandes
{
    /// <summary>Identifiant stable (UUID version 8, RFC 9562) de l'usage pour cet envoi.</summary>
    public static Guid Derivee(Guid commande, string usage)
    {
        ArgumentException.ThrowIfNullOrEmpty(usage);
        Span<byte> empreinte = stackalloc byte[32];
        SHA256.HashData([.. commande.ToByteArray(bigEndian: true), .. Encoding.UTF8.GetBytes(usage)], empreinte);
        var octets = empreinte[..16];
        octets[6] = (byte)((octets[6] & 0x0F) | 0x80); // version 8
        octets[8] = (byte)((octets[8] & 0x3F) | 0x80); // variante RFC 9562
        return new Guid(octets, bigEndian: true);
    }
}

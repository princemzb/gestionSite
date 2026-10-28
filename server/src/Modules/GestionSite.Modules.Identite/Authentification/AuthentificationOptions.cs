using System.ComponentModel.DataAnnotations;

namespace GestionSite.Modules.Identite.Authentification;

/// <summary>Jetons de l'API (ADR-10, valeurs par défaut proposées) ; clé de signature hors dépôt hors local.</summary>
internal sealed class AuthentificationOptions
{
    internal const string Section = "Authentification";

    /// <summary>Clé HMAC-SHA256 en base64, au moins 32 octets.</summary>
    [Required]
    public string CleSignature { get; init; } = string.Empty;

    public string Emetteur { get; init; } = "gestionsite";

    public string Audience { get; init; } = "gestionsite-api";

    public TimeSpan DureeJetonAcces { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Glissante : chaque rotation repart pour cette durée.</summary>
    public TimeSpan DureeJetonRenouvellement { get; init; } = TimeSpan.FromDays(14);

    internal byte[] Cle() => Convert.FromBase64String(CleSignature);

    internal bool CleValide()
    {
        try
        {
            return Cle().Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

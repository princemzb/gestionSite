using System.ComponentModel.DataAnnotations;

namespace GestionSite.Web;

internal sealed class WebOptions
{
    internal const string Section = "Web";

    /// <summary>Adresse de l'API (réseau interne du conteneur en préproduction).</summary>
    [Required]
    public string ApiUrl { get; init; } = string.Empty;

    /// <summary>
    /// Adresse publique du site (https://…) pour les liens d'activation ; à fixer derrière un proxy, où
    /// l'adresse vue par le serveur est interne. Vide : l'adresse de la requête.
    /// </summary>
    public string? UrlPublique { get; init; }

    /// <summary>
    /// Derrière un relais TLS joignable seul (Worker Cloudflare en préproduction) : se fier à son en-tête
    /// X-Forwarded-Proto pour connaître le schéma d'origine (HSTS, cookies Secure). Faux par défaut.
    /// </summary>
    public bool RelaisHttps { get; init; }

    /// <summary>Clé de site Turnstile (publique) du formulaire de signalement ; vide : aucun widget (développement).</summary>
    public string? CleSiteTurnstile { get; init; }

    /// <summary>
    /// En-tête portant l'adresse du visiteur, posé par le relais (préproduction : CF-Connecting-IP, réécrit par Cloudflare).
    /// Vide : adresse de la connexion. Ne régler que derrière un relais qui écrase cet en-tête.
    /// </summary>
    public string? EnteteAdresseClient { get; init; }

    /// <summary>Signalements publics par adresse de visiteur et par fenêtre de 10 minutes (décision du 28/10).</summary>
    public int LimiteDepotsParAdresse { get; init; } = 5;
}

/// <summary>Stockage compatible S3 des clés de chiffrement (Web:Cles), R2 en préproduction.</summary>
internal sealed class StockageCles
{
    public string ServiceUrl { get; init; } = string.Empty;

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public string Compartiment { get; init; } = string.Empty;

    public string Region { get; init; } = "auto";
}

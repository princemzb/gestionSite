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
}

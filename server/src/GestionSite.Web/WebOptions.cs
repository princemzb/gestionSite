using System.ComponentModel.DataAnnotations;

namespace GestionSite.Web;

internal sealed class WebOptions
{
    internal const string Section = "Web";

    /// <summary>Adresse de l'API (réseau interne du conteneur en préproduction).</summary>
    [Required]
    public string ApiUrl { get; init; } = string.Empty;
}

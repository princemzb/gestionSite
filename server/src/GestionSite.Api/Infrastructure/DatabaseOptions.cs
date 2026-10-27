using System.ComponentModel.DataAnnotations;

namespace GestionSite.Api.Infrastructure;

/// <summary>Connexion PostgreSQL : conteneur local, Neon en préproduction (ADR-08).</summary>
public sealed class DatabaseOptions
{
    public const string Section = "ConnectionStrings";

    [Required]
    public string Main { get; init; } = string.Empty;
}

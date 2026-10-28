using System.ComponentModel.DataAnnotations;

namespace GestionSite.Api.Infrastructure;

/// <summary>Connexion PostgreSQL : conteneur local, Neon en préproduction (ADR-08).</summary>
public sealed class DatabaseOptions
{
    public const string Section = "ConnectionStrings";

    /// <summary>Rôle applicatif : sans droit sur le schéma, soumis à la RLS.</summary>
    [Required]
    public string Main { get; init; } = string.Empty;

    /// <summary>Rôle propriétaire du schéma, pour appliquer les migrations ; facultatif hors migration.</summary>
    public string? Migrations { get; init; }
}

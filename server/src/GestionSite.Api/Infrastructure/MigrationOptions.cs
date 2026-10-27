namespace GestionSite.Api.Infrastructure;

/// <summary>Application des migrations au démarrage : développement local et tests uniquement.</summary>
public sealed class MigrationOptions
{
    public const string Section = "Database";

    /// <summary>Valeur autre que « true » ou « false » : démarrage refusé (lecture validée au démarrage).</summary>
    public bool MigrateOnStartup { get; init; }
}

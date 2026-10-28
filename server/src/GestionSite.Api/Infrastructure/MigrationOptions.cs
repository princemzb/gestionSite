namespace GestionSite.Api.Infrastructure;

/// <summary>Application des migrations au démarrage : développement local et tests uniquement.</summary>
public sealed class MigrationOptions
{
    public const string Section = "Database";

    /// <summary>
    /// Valeur non booléenne (casse indifférente) : démarrage refusé. Vrai seulement avec une base
    /// locale (localhost, boucle locale, socket Unix), vérifié au démarrage.
    /// </summary>
    public bool MigrateOnStartup { get; init; }
}

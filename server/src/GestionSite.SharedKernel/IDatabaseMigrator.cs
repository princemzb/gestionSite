namespace GestionSite.SharedKernel;

/// <summary>
/// Applique les migrations de base de données d'un module. L'hôte les exécute au démarrage
/// quand la configuration le demande (développement, tests) ; en production, l'étape de
/// déploiement s'en charge.
/// </summary>
public interface IDatabaseMigrator
{
    /// <summary>Nom du module, pour les journaux.</summary>
    string ModuleName { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

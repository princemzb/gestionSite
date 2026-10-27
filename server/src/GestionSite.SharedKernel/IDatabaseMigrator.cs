namespace GestionSite.SharedKernel;

/// <summary>
/// Migrations de base de données d'un module. L'hôte les applique au démarrage quand la
/// configuration le demande (développement, tests) et vérifie dans sa sonde de disponibilité
/// qu'aucune ne reste en attente ; en production, l'étape de déploiement les applique.
/// </summary>
public interface IDatabaseMigrator
{
    /// <summary>Nom du module, pour les journaux.</summary>
    string ModuleName { get; }

    Task MigrateAsync(CancellationToken cancellationToken);

    Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken);
}

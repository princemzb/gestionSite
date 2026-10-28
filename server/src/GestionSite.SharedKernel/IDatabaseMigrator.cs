namespace GestionSite.SharedKernel;

/// <summary>
/// Migrations de base de données d'un module. L'hôte les applique au démarrage quand la
/// configuration le demande (développement, tests) et vérifie dans sa sonde de disponibilité
/// qu'aucune ne reste en attente ; en production, l'étape de déploiement les applique.
/// Chaque module possède son propre schéma, sans clé étrangère vers un autre module :
/// l'ordre d'application entre modules est donc indifférent. Une base en avance sur le code
/// (retour arrière après une migration) reste disponible : les migrations doivent rester
/// compatibles avec la version précédente du code.
/// </summary>
public interface IDatabaseMigrator
{
    /// <summary>Nom du module, pour les journaux.</summary>
    string ModuleName { get; }

    Task MigrateAsync(CancellationToken cancellationToken);

    Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken);
}

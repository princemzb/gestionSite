using GestionSite.SharedKernel;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// Applique les migrations de chaque module au démarrage si « Database:MigrateOnStartup » est vrai
/// (développement local, tests ; base locale exigée). En préproduction et production, l'étape de déploiement s'en charge.
/// Exécuté au tout début du démarrage de l'hôte : après la validation de la configuration
/// (ValidateOnStart), avant l'ouverture du serveur, et annulable par l'arrêt de l'hôte.
/// </summary>
internal sealed partial class DatabaseMigrations(
    IOptions<MigrationOptions> options, IOptions<DatabaseOptions> database,
    IServiceScopeFactory scopes, ILogger<DatabaseMigrations> logger)
    : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        // Garde-fou : une variable ConnectionStrings__Main oubliée dans un terminal ne doit pas
        // faire migrer une base partagée par un simple `dotnet run` (chaîne déjà validée ici).
        if (!LocalDatabase.IsLocal(database.Value.Main))
        {
            throw new InvalidOperationException(
                $"{MigrationOptions.Section}:{nameof(MigrationOptions.MigrateOnStartup)} n'est autorisé que pour une base PostgreSQL locale.");
        }

        await using var scope = scopes.CreateAsyncScope();
        foreach (var migrator in scope.ServiceProvider.GetServices<IDatabaseMigrator>())
        {
            // EF journalise lui-même chaque migration appliquée ou l'absence de migration,
            // et refuse un modèle modifié sans migration.
            await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
            Log.Checked(logger, migrator.ModuleName);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Schéma de la base vérifié pour le module {Module}")]
        internal static partial void Checked(ILogger logger, string module);
    }
}

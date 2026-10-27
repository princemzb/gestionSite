using GestionSite.SharedKernel;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// Applique les migrations de chaque module au démarrage si « Database:MigrateOnStartup » est vrai
/// (développement local, tests). En préproduction et production, l'étape de déploiement s'en charge.
/// Exécuté au tout début du démarrage de l'hôte : après la validation de la configuration
/// (ValidateOnStart), avant l'ouverture du serveur, et annulable par l'arrêt de l'hôte.
/// </summary>
internal sealed partial class DatabaseMigrations(IServiceScopeFactory scopes, ILogger<DatabaseMigrations> logger)
    : IHostedLifecycleService
{
    internal const string Setting = "Database:MigrateOnStartup";

    internal static bool IsRequested(IConfiguration configuration)
    {
        var value = configuration[Setting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var requested)
            ? requested
            : throw new InvalidOperationException($"{Setting} doit valoir « true » ou « false » (valeur reçue : « {value} »).");
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        foreach (var migrator in scope.ServiceProvider.GetServices<IDatabaseMigrator>())
        {
            Log.Applying(logger, migrator.ModuleName);
            await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
            Log.Applied(logger, migrator.ModuleName);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Application des migrations de la base pour le module {Module}")]
        internal static partial void Applying(ILogger logger, string module);

        [LoggerMessage(Level = LogLevel.Information, Message = "Migrations de la base appliquées pour le module {Module}")]
        internal static partial void Applied(ILogger logger, string module);
    }
}

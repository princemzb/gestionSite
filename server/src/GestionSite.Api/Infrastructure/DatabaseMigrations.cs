using GestionSite.SharedKernel;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// Applique les migrations de chaque module au démarrage si « Database:MigrateOnStartup » est vrai
/// (développement local, tests). En préproduction et production, l'étape de déploiement s'en charge.
/// </summary>
internal static partial class DatabaseMigrations
{
    internal const string Setting = "Database:MigrateOnStartup";

    internal static async Task MigrateDatabasesIfRequestedAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>(Setting))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        foreach (var migrator in scope.ServiceProvider.GetServices<IDatabaseMigrator>())
        {
            Log.Migrating(app.Logger, migrator.ModuleName);
            await migrator.MigrateAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Migrations de la base appliquées pour le module {Module}")]
        internal static partial void Migrating(ILogger logger, string module);
    }
}

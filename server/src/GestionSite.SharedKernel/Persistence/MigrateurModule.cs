using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GestionSite.SharedKernel.Persistence;

/// <summary>Migrations d'un module avec le rôle propriétaire ; état lu avec le rôle applicatif (sonde de disponibilité).</summary>
public abstract class MigrateurModule<TContext>(TContext context, IServiceProvider services, ILoggerFactory loggerFactory) : IDatabaseMigrator
    where TContext : DbContext
{
    public abstract string ModuleName { get; }

    protected abstract string Schema { get; }

    protected abstract string TableMigrations { get; }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var proprietaire = services.GetRequiredKeyedService<NpgsqlDataSource>(DatabaseConnections.Migrations);
        var options = new DbContextOptionsBuilder<TContext>().UseLoggerFactory(loggerFactory);
        PersistanceModule.Configurer(options, proprietaire, Schema, TableMigrations, requete: null);
        await using var migration = Creer(options.Options);
        await migration.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();

    /// <summary>Contexte de migration, sans organisation courante.</summary>
    protected abstract TContext Creer(DbContextOptions<TContext> options);
}

using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GestionSite.Modules.Maintenance.Persistence;

/// <summary>Comme IdentiteMigrator : migrations avec le rôle propriétaire, état lu avec le rôle applicatif.</summary>
internal sealed class MaintenanceMigrator(MaintenanceDbContext context, IServiceProvider services, ILoggerFactory loggerFactory) : IDatabaseMigrator
{
    public string ModuleName => "Maintenance";

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var proprietaire = services.GetRequiredKeyedService<NpgsqlDataSource>(DatabaseConnections.Migrations);
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>().UseLoggerFactory(loggerFactory);
        MaintenancePersistence.Configure(options, proprietaire, requete: null);
        await using var migration = new MaintenanceDbContext(options.Options, new OrganisationCourante());
        await migration.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();
}

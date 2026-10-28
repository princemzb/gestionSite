using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Applique les migrations avec le rôle propriétaire du schéma ; l'état des migrations est lu
/// avec le rôle applicatif (droit de lecture de l'historique accordé par la migration).
/// </summary>
internal sealed class IdentiteMigrator(IdentiteDbContext context, IServiceProvider services, ILoggerFactory loggerFactory) : IDatabaseMigrator
{
    public string ModuleName => "Identite";

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        // Résolue ici seulement : la sonde de disponibilité utilise aussi ce migrateur, sans chaîne propriétaire.
        var proprietaire = services.GetRequiredKeyedService<NpgsqlDataSource>(DatabaseConnections.Migrations);
        var sansOrganisation = new OrganisationCourante(); // migrations : aucune donnée d'organisation lue
        var options = new DbContextOptionsBuilder<IdentiteDbContext>()
            .UseLoggerFactory(loggerFactory); // journal des migrations appliquées
        IdentitePersistence.Configure(options, proprietaire, sansOrganisation);
        await using var migration = new IdentiteDbContext(options.Options, sansOrganisation);
        await migration.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();
}

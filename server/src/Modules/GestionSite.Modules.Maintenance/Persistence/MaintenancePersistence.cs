using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace GestionSite.Modules.Maintenance.Persistence;

internal static class MaintenancePersistence
{
    internal static IServiceCollection AddMaintenancePersistence(this IServiceCollection services)
    {
        services.AddDbContext<MaintenanceDbContext>((provider, options) => Configure(
            options,
            provider.GetRequiredService<NpgsqlDataSource>(),
            new ContexteRequete(provider.GetRequiredService<OrganisationCourante>(), provider.GetRequiredService<CompteCourant>())));
        services.AddScoped<IDatabaseMigrator, MaintenanceMigrator>();
        return services;
    }

    /// <summary>Sans contexte de requête (migrations) : aucun intercepteur, les commandes partent telles quelles.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource, ContexteRequete? requete)
    {
        options.UseNpgsql(dataSource, ConfigureNpgsql).UseSnakeCaseNamingConvention();
        return requete is null ? options : options.AddInterceptors(requete.Intercepteurs());
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(MaintenanceDbContext.MigrationsTable, MaintenanceDbContext.Schema);
}

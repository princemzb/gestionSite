using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace GestionSite.Modules.Identite.Persistence;

internal static class IdentitePersistence
{
    internal static DbContextOptionsBuilder<IdentiteDbContext> Configure(
        DbContextOptionsBuilder<IdentiteDbContext> builder, string connectionString) =>
        builder.UseNpgsql(connectionString, ConfigureNpgsql).UseSnakeCaseNamingConvention();

    internal static IServiceCollection AddIdentitePersistence(this IServiceCollection services)
    {
        // La source de données PostgreSQL est fournie par l'hôte (configuration validée au démarrage).
        services.AddDbContext<IdentiteDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), ConfigureNpgsql).UseSnakeCaseNamingConvention());
        services.AddScoped<IDatabaseMigrator, IdentiteMigrator>();
        return services;
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(IdentiteDbContext.MigrationsTable, IdentiteDbContext.Schema);
}

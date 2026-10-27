using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace GestionSite.Modules.Identite.Persistence;

internal static class IdentitePersistence
{
    internal static IServiceCollection AddIdentitePersistence(this IServiceCollection services)
    {
        // La source de données PostgreSQL est fournie par l'hôte (configuration validée au démarrage).
        services.AddDbContext<IdentiteDbContext>((provider, options) =>
            Configure(options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IDatabaseMigrator, IdentiteMigrator>();
        return services;
    }

    /// <summary>Configuration unique du contexte, à l'exécution comme pour l'outil de migrations.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options.UseNpgsql(dataSource, ConfigureNpgsql).UseSnakeCaseNamingConvention();

    /// <summary>Variante sans source de données partagée, pour `dotnet ef` : rien à libérer.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, ConfigureNpgsql).UseSnakeCaseNamingConvention();

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(IdentiteDbContext.MigrationsTable, IdentiteDbContext.Schema);
}

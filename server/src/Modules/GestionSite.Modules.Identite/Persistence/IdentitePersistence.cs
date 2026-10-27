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
        // La source de données PostgreSQL est fournie par l'hôte (configuration validée au démarrage) ;
        // `dotnet ef` passe aussi par l'hôte de l'API : une seule configuration du contexte.
        services.AddDbContext<IdentiteDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), ConfigureNpgsql)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IDatabaseMigrator, IdentiteMigrator>();
        return services;
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(IdentiteDbContext.MigrationsTable, IdentiteDbContext.Schema);
}

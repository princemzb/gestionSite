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
        // Sources de données et organisation courante fournies par l'hôte (configuration validée au
        // démarrage) ; `dotnet ef` passe aussi par l'hôte de l'API : une seule configuration du contexte.
        services.AddDbContext<IdentiteDbContext>((provider, options) => Configure(
            options,
            provider.GetRequiredService<NpgsqlDataSource>(),
            provider.GetRequiredService<OrganisationCourante>()));
        services.AddScoped<IDatabaseMigrator, IdentiteMigrator>();
        return services;
    }

    /// <summary>Sans organisation courante (migrations) : aucun intercepteur, les commandes partent telles quelles.</summary>
    internal static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options, NpgsqlDataSource dataSource, OrganisationCourante? organisationCourante)
    {
        options.UseNpgsql(dataSource, ConfigureNpgsql).UseSnakeCaseNamingConvention();
        return organisationCourante is null
            ? options
            : options.AddInterceptors(
                new OrganisationCommandInterceptor(organisationCourante),
                new OrganisationTransactionInterceptor(organisationCourante));
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(IdentiteDbContext.MigrationsTable, IdentiteDbContext.Schema);
}

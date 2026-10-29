using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace GestionSite.Modules.Patrimoine.Persistence;

internal static class PatrimoinePersistence
{
    internal static IServiceCollection AddPatrimoinePersistence(this IServiceCollection services)
    {
        services.AddDbContext<PatrimoineDbContext>((provider, options) => Configure(
            options,
            provider.GetRequiredService<NpgsqlDataSource>(),
            new ContexteRequete(provider.GetRequiredService<OrganisationCourante>(), provider.GetRequiredService<CompteCourant>())));
        services.AddScoped<IDatabaseMigrator, PatrimoineMigrator>();
        return services;
    }

    /// <summary>Sans contexte de requête (migrations) : aucun intercepteur, les commandes partent telles quelles.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource, ContexteRequete? requete)
    {
        options.UseNpgsql(dataSource, ConfigureNpgsql).UseSnakeCaseNamingConvention();
        return requete is null ? options : options.AddInterceptors(requete.Intercepteurs());
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(PatrimoineDbContext.MigrationsTable, PatrimoineDbContext.Schema);
}

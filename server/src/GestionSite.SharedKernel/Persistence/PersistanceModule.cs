using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionSite.SharedKernel.Persistence;

/// <summary>Contexte EF d'un module : PostgreSQL, noms en snake_case, historique des migrations dans le schéma du module.</summary>
public static class PersistanceModule
{
    public static IServiceCollection AjouterContexteModule<TContext>(this IServiceCollection services, string schema, string tableMigrations)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((provider, options) => Configurer(
            options,
            provider.GetRequiredService<NpgsqlDataSource>(),
            schema,
            tableMigrations,
            new ContexteRequete(provider.GetRequiredService<OrganisationCourante>(), provider.GetRequiredService<CompteCourant>())));

    /// <summary>Sans contexte de requête (migrations) : aucun intercepteur, les commandes partent telles quelles.</summary>
    public static DbContextOptionsBuilder Configurer(
        DbContextOptionsBuilder options, NpgsqlDataSource dataSource, string schema, string tableMigrations, ContexteRequete? requete)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable(tableMigrations, schema)).UseSnakeCaseNamingConvention();
        return requete is null ? options : options.AddInterceptors(requete.Intercepteurs());
    }
}

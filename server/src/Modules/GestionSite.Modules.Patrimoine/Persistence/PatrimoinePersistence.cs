using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Patrimoine.Persistence;

internal static class PatrimoinePersistence
{
    internal static IServiceCollection AddPatrimoinePersistence(this IServiceCollection services) =>
        services.AjouterContexteModule<PatrimoineDbContext>(PatrimoineDbContext.Schema, PatrimoineDbContext.MigrationsTable)
            .AddScoped<IDatabaseMigrator, PatrimoineMigrator>();
}

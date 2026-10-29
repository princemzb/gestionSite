using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Maintenance.Persistence;

internal static class MaintenancePersistence
{
    internal static IServiceCollection AddMaintenancePersistence(this IServiceCollection services) =>
        services.AjouterContexteModule<MaintenanceDbContext>(MaintenanceDbContext.Schema, MaintenanceDbContext.MigrationsTable)
            .AddScoped<IDatabaseMigrator, MaintenanceMigrator>();
}

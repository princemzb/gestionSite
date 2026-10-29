using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionSite.Modules.Maintenance.Persistence;

internal sealed class MaintenanceMigrator(MaintenanceDbContext context, IServiceProvider services, ILoggerFactory loggerFactory)
    : MigrateurModule<MaintenanceDbContext>(context, services, loggerFactory)
{
    public override string ModuleName => "Maintenance";

    protected override string Schema => MaintenanceDbContext.Schema;

    protected override string TableMigrations => MaintenanceDbContext.MigrationsTable;

    protected override MaintenanceDbContext Creer(DbContextOptions<MaintenanceDbContext> options) => new(options, new OrganisationCourante());
}

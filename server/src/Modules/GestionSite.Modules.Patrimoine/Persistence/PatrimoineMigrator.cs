using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionSite.Modules.Patrimoine.Persistence;

internal sealed class PatrimoineMigrator(PatrimoineDbContext context, IServiceProvider services, ILoggerFactory loggerFactory)
    : MigrateurModule<PatrimoineDbContext>(context, services, loggerFactory)
{
    public override string ModuleName => "Patrimoine";

    protected override string Schema => PatrimoineDbContext.Schema;

    protected override string TableMigrations => PatrimoineDbContext.MigrationsTable;

    protected override PatrimoineDbContext Creer(DbContextOptions<PatrimoineDbContext> options) => new(options, new OrganisationCourante());
}

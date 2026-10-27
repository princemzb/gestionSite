using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Persistence;

internal sealed class IdentiteMigrator(IdentiteDbContext context) : IDatabaseMigrator
{
    public string ModuleName => "Identite";

    public Task MigrateAsync(CancellationToken cancellationToken) => context.Database.MigrateAsync(cancellationToken);
}

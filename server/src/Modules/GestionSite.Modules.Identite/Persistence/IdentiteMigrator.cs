using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Persistence;

internal sealed class IdentiteMigrator(IdentiteDbContext context) : IDatabaseMigrator
{
    public string ModuleName => "Identite";

    public Task MigrateAsync(CancellationToken cancellationToken) => context.Database.MigrateAsync(cancellationToken);

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();
}

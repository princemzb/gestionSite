using GestionSite.SharedKernel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// PostgreSQL joignable, puis aucune migration de module en attente (pas de service sur un schéma
/// périmé). Un seul contrôle : base injoignable, une seule tentative de connexion par sonde.
/// </summary>
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource, IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using (var command = dataSource.CreateCommand("SELECT 1"))
            {
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Toute défaillance rend la base indisponible ; seul un message fixe est exposé.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL injoignable.", exception);
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            foreach (var migrator in scope.ServiceProvider.GetServices<IDatabaseMigrator>())
            {
                if (await migrator.HasPendingMigrationsAsync(cancellationToken).ConfigureAwait(false))
                {
                    return HealthCheckResult.Unhealthy("Migrations de base en attente.");
                }
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Schéma de base invérifiable.", exception);
        }
    }
}

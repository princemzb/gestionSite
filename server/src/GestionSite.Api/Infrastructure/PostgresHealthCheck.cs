using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace GestionSite.Api.Infrastructure;

/// <summary>Vérifie qu'une connexion à PostgreSQL s'ouvre et répond à une requête.</summary>
internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // Toute défaillance rend la base indisponible ; seul un message fixe est exposé.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("PostgreSQL injoignable.", exception);
        }
    }
}

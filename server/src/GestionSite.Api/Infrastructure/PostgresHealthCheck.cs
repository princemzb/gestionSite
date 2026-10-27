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
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            // Pas de détail de connexion dans le résultat : il peut être exposé.
            return HealthCheckResult.Unhealthy("PostgreSQL injoignable.", exception);
        }
    }
}

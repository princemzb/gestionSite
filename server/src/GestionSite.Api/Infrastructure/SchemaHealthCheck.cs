using GestionSite.SharedKernel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// Indisponible tant qu'une migration d'un module n'est pas appliquée : évite de servir sur un schéma périmé.
/// Les migrations embarquées ne changent pas pendant la vie du processus : une fois le schéma à jour,
/// le résultat est mémorisé et la sonde n'ouvre plus de connexion supplémentaire.
/// </summary>
internal sealed class SchemaHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    private volatile bool _upToDate;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_upToDate)
        {
            return HealthCheckResult.Healthy();
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

            _upToDate = true;
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // Toute défaillance rend le schéma invérifiable ; seul un message fixe est exposé.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Schéma de base invérifiable.", exception);
        }
    }
}

using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// Vérifie que le compartiment de fichiers existe et que les clés sont acceptées. HeadBucket est
/// une opération de classe B sur Cloudflare R2, moins coûteuse qu'une liste d'objets.
/// </summary>
internal sealed class ObjectStorageHealthCheck(IAmazonS3 client, IOptions<ObjectStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.HeadBucketAsync(new HeadBucketRequest { BucketName = options.Value.Bucket }, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // Toute défaillance rend le stockage indisponible ; seul un message fixe est exposé.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Stockage de fichiers injoignable.", exception);
        }
    }
}

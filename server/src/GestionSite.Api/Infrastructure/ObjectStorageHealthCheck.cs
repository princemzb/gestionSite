using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Infrastructure;

/// <summary>Vérifie que le compartiment de fichiers existe et répond.</summary>
internal sealed class ObjectStorageHealthCheck(IAmazonS3 client, IOptions<ObjectStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ListObjectsV2Request { BucketName = options.Value.Bucket, MaxKeys = 1 };
            await client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is AmazonServiceException or HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Stockage de fichiers injoignable.", exception);
        }
    }
}

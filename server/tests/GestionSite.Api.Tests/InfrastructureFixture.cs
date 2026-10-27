using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Conteneurs réels PostgreSQL et SeaweedFS (compatible S3), partagés par les tests d'intégration
/// de disponibilité. Mêmes images que server/compose.yaml.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    internal const string AccessKey = "test-access-key";
    internal const string SecretKey = "test-secret-key";
    internal const string Bucket = "gestionsite-test";
    private const int S3Port = 8333;

    private static readonly string S3Config = $$"""
        {"identities":[{"name":"test","credentials":[{"accessKey":"{{AccessKey}}","secretKey":"{{SecretKey}}"}],
        "actions":["Admin","Read","Write","List","Tagging"]}]}
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine")
        .Build();

    private readonly IContainer _storage = new ContainerBuilder("chrislusf/seaweedfs:3.97")
        .WithCommand("server", "-dir=/data", "-s3", $"-s3.port={S3Port}", "-s3.config=/etc/seaweedfs/s3.json")
        .WithResourceMapping(Encoding.UTF8.GetBytes(S3Config), "/etc/seaweedfs/s3.json")
        .WithPortBinding(S3Port, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(S3Port).ForPath("/healthz")))
        .Build();

    internal string ConnectionString => _postgres.GetConnectionString();

    internal string StorageUrl => $"http://{_storage.Hostname}:{_storage.GetMappedPublicPort(S3Port)}";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _storage.StartAsync()).ConfigureAwait(false);
        await CreateBucketAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync().ConfigureAwait(false);
        await _storage.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CreateBucketAsync()
    {
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config { ServiceURL = StorageUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

        // Le service de fichiers interne de SeaweedFS peut démarrer quelques secondes après le point S3.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await client.PutBucketAsync(Bucket).ConfigureAwait(false);
                return;
            }
            catch (AmazonS3Exception) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }
    }
}

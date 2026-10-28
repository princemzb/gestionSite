using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using GestionSite.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Conteneurs réels PostgreSQL et SeaweedFS (compatible S3), partagés par les tests d'intégration
/// (collection « Infrastructure », démarrés une seule fois). Mêmes images et même configuration S3
/// que server/compose.yaml.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    // Identité définie dans server/docker/seaweedfs-s3.json (valeurs locales non sensibles).
    internal const string AccessKey = "local-dev-access-key";
    internal const string SecretKey = "local-dev-secret-key-not-sensitive";
    internal const string Region = "us-east-1";
    internal const string Bucket = "gestionsite-test";
    private const int S3Port = 8333;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine").Build();

    private readonly IContainer _storage = new ContainerBuilder("chrislusf/seaweedfs:3.97")
        .WithCommand("server", "-dir=/data", "-s3", $"-s3.port={S3Port}", "-s3.config=/etc/seaweedfs/seaweedfs-s3.json")
        .WithResourceMapping(new FileInfo(Path.Combine(ServerDirectory(), "docker", "seaweedfs-s3.json")), "/etc/seaweedfs/")
        .WithPortBinding(S3Port, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(S3Port).ForPath("/healthz")))
        .Build();

    internal string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Base partagée des tests qui ne vérifient pas eux-mêmes le contenu de la base.</summary>
    internal TestDatabase Default { get; private set; } = null!;

    internal string StorageUrl => $"http://{_storage.Hostname}:{_storage.GetMappedPublicPort(S3Port)}";

    /// <summary>
    /// API réelle branchée sur ces conteneurs ; chaque paramètre remplace la valeur par défaut.
    /// Migration au démarrage par défaut : suppose un démon Docker local (hôte de boucle locale).
    /// </summary>
    internal WebApplicationFactory<Program> CreateApi(
        TestDatabase? database = null, string? connectionString = null, string? migrationsConnectionString = null,
        string migrateOnStartup = "true", string? bucket = null, string? storageUrl = null, ILoggerProvider? logs = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging =>
            {
                if (logs is not null)
                {
                    logging.AddProvider(logs);
                }
            })
            .UseSetting("ConnectionStrings:Main", connectionString ?? (database ?? Default).ApplicationConnectionString)
            .UseSetting("ConnectionStrings:Migrations", migrationsConnectionString ?? (database ?? Default).MigrationsConnectionString)
            .UseSetting("Database:MigrateOnStartup", migrateOnStartup)
            .UseSetting("ObjectStorage:ServiceUrl", storageUrl ?? StorageUrl)
            .UseSetting("ObjectStorage:AccessKey", AccessKey)
            .UseSetting("ObjectStorage:SecretKey", SecretKey)
            .UseSetting("ObjectStorage:Bucket", bucket ?? Bucket)
            .UseSetting("ObjectStorage:Region", Region));

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _storage.StartAsync()).ConfigureAwait(false);
        await CreateBucketAsync().ConfigureAwait(false);
        Default = await TestDatabase.CreateAsync(ConnectionString).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await Default.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _storage.DisposeAsync().AsTask()).ConfigureAwait(false);
    }

    private async Task CreateBucketAsync()
    {
        using var client = ObjectStorageClientFactory.Create(StorageUrl, AccessKey, SecretKey, Region);

        // Le service de fichiers interne de SeaweedFS peut démarrer quelques secondes après le point S3.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await client.PutBucketAsync(Bucket).ConfigureAwait(false);
                return;
            }
            catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                return; // une tentative précédente a abouti malgré une réponse perdue
            }
            // SeaweedFS en démarrage peut renvoyer des erreurs variées (4xx, coupures) : essais bornés.
            catch (Exception) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }
    }

    internal static string ServerDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.GetFiles("GestionSite.slnx").Length == 0)
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Répertoire server/ introuvable.");
    }
}

[CollectionDefinition(Name)]
public sealed class SharedInfrastructure : ICollectionFixture<InfrastructureFixture>
{
    internal const string Name = "Infrastructure";
}

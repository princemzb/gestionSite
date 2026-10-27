using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Tests;

/// <summary>Disponibilité (/health/ready) contre de vrais PostgreSQL et stockage S3.</summary>
public sealed class ReadinessEndpointTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task ReadyWhenDatabaseAndStorageAreReachable()
    {
        await using var factory = CreateFactory();

        var (status, body) = await GetReadyAsync(factory);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task NotReadyWhenDatabaseIsUnreachable()
    {
        await using var factory = CreateFactory(connectionString: "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");

        var (status, body) = await GetReadyAsync(factory);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("Unhealthy", body); // aucun détail de connexion exposé
    }

    [Fact]
    public async Task NotReadyWhenBucketDoesNotExist()
    {
        await using var factory = CreateFactory(bucket: "compartiment-absent");

        var (status, _) = await GetReadyAsync(factory);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    [Fact]
    public async Task StartupFailsWithoutStorageConfiguration()
    {
        await using var factory = CreateFactory(bucket: string.Empty);

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task StartupFailsWithoutConnectionString()
    {
        await using var factory = CreateFactory(connectionString: string.Empty);

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private WebApplicationFactory<Program> CreateFactory(string? connectionString = null, string? bucket = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:Main", connectionString ?? infrastructure.ConnectionString)
            .UseSetting("ObjectStorage:ServiceUrl", infrastructure.StorageUrl)
            .UseSetting("ObjectStorage:AccessKey", InfrastructureFixture.AccessKey)
            .UseSetting("ObjectStorage:SecretKey", InfrastructureFixture.SecretKey)
            .UseSetting("ObjectStorage:Bucket", bucket ?? InfrastructureFixture.Bucket));

    private static async Task<(HttpStatusCode Status, string Body)> GetReadyAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}

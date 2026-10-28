using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Tests;

/// <summary>Disponibilité (/health/ready) contre de vrais PostgreSQL et stockage S3.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class ReadinessEndpointTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task ReadyWhenDatabaseAndStorageAreReachable()
    {
        await using var factory = infrastructure.CreateApi();

        var (status, body) = await GetReadyAsync(factory);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task NotReadyWhileMigrationsArePending()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database, migrateOnStartup: "false");

        var (status, body) = await GetReadyAsync(factory);
        var report = await factory.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(c => c.Name == "postgresql");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("Unhealthy", body);
        Assert.Equal("Migrations de base en attente.", report.Entries["postgresql"].Description);
    }

    [Fact]
    public async Task StartupFailsWithAnInvalidMigrationSetting()
    {
        await using var factory = infrastructure.CreateApi(migrateOnStartup: "1");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Database:MigrateOnStartup", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotReadyWhenDatabaseIsUnreachable()
    {
        await using var factory = infrastructure.CreateApi(connectionString: "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2", migrateOnStartup: "false");

        var (status, body) = await GetReadyAsync(factory);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("Unhealthy", body); // aucun détail de connexion exposé
    }

    [Fact]
    public async Task NotReadyWhenBucketDoesNotExist()
    {
        await using var factory = infrastructure.CreateApi(bucket: "compartiment-absent");

        var (status, _) = await GetReadyAsync(factory);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    [Fact]
    public async Task StartupFailsWithoutStorageConfiguration()
    {
        await using var factory = infrastructure.CreateApi(bucket: string.Empty);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(typeof(GestionSite.Api.Infrastructure.ObjectStorageOptions), exception.OptionsType);
    }

    [Theory]
    [InlineData("Database=gestionsite;Username=x;Password=y")]
    [InlineData("pas une chaîne de connexion")]
    public async Task StartupFailsWithUnusableConnectionString(string connectionString)
    {
        await using var factory = infrastructure.CreateApi(connectionString: connectionString);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(typeof(GestionSite.Api.Infrastructure.DatabaseOptions), exception.OptionsType);
    }

    [Fact]
    public async Task StartupFailsWithNonHttpStorageUrl()
    {
        await using var factory = infrastructure.CreateApi(storageUrl: "ftp://stockage.example");

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(typeof(GestionSite.Api.Infrastructure.ObjectStorageOptions), exception.OptionsType);
    }

    [Fact]
    public async Task StartupFailsWithoutConnectionString()
    {
        await using var factory = infrastructure.CreateApi(connectionString: string.Empty);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(typeof(GestionSite.Api.Infrastructure.DatabaseOptions), exception.OptionsType);
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetReadyAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}

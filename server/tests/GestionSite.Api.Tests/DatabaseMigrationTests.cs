using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Tests;

/// <summary>Migrations appliquées au démarrage sur un PostgreSQL réel, base dédiée à chaque test.</summary>
public sealed class DatabaseMigrationTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    private const string CountMigrations = "SELECT count(*) FROM identite.__ef_migrations";

    [Fact]
    public async Task MigrationsCreateTheIdentiteSchemaAtStartup()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = CreateFactory(database.ConnectionString, migrate: true);

        using var client = factory.CreateClient();

        Assert.Equal(1L, await database.CountTablesAsync("identite", "organisations"));
        Assert.Equal(1L, await database.CountAsync(CountMigrations));
    }

    [Fact]
    public async Task RestartingDoesNotReapplyMigrations()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using (var first = CreateFactory(database.ConnectionString, migrate: true))
        {
            using var client = first.CreateClient();
        }

        await using var second = CreateFactory(database.ConnectionString, migrate: true);
        using var secondClient = second.CreateClient();

        Assert.Equal(1L, await database.CountAsync(CountMigrations));
    }

    [Fact]
    public async Task DatabaseIsUntouchedWhenMigrationIsNotRequested()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = CreateFactory(database.ConnectionString, migrate: false);

        using var client = factory.CreateClient();

        Assert.Equal(0L, await database.CountTablesAsync("identite", "organisations"));
    }

    [Fact]
    public async Task InvalidConfigurationStopsStartupBeforeAnyMigration()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = CreateFactory(database.ConnectionString, migrate: true, bucket: string.Empty);

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(0L, await database.CountTablesAsync("identite", "organisations"));
    }

    private WebApplicationFactory<Program> CreateFactory(string connectionString, bool migrate, string? bucket = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:Main", connectionString)
            .UseSetting("Database:MigrateOnStartup", migrate ? "true" : "false")
            .UseSetting("ObjectStorage:ServiceUrl", infrastructure.StorageUrl)
            .UseSetting("ObjectStorage:AccessKey", InfrastructureFixture.AccessKey)
            .UseSetting("ObjectStorage:SecretKey", InfrastructureFixture.SecretKey)
            .UseSetting("ObjectStorage:Bucket", bucket ?? InfrastructureFixture.Bucket));
}

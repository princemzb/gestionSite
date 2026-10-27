using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Tests;

/// <summary>Migrations appliquées au démarrage sur un PostgreSQL réel, base dédiée à chaque test.</summary>
public sealed class DatabaseMigrationTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    private const string CountMigrations = "SELECT count(*) FROM identite.__ef_migrations";

    [Fact]
    public async Task SchemaCheckRemembersAnUpToDateSchema()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString, migrate: true);
        var health = factory.Services.GetRequiredService<HealthCheckService>();

        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync(c => c.Name == "schema-a-jour")).Status);
        await database.ExecuteAsync("DELETE FROM identite.__ef_migrations");

        // Les migrations embarquées ne changent pas sans redémarrage : plus de requête après le premier succès.
        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync(c => c.Name == "schema-a-jour")).Status);
    }

    [Fact]
    public async Task MigrationsCreateTheIdentiteSchemaAtStartup()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString, migrate: true);

        using var client = factory.CreateClient();

        Assert.Equal(1L, await database.CountTablesAsync("identite", "organisations"));
        Assert.Equal(1L, await database.CountAsync(CountMigrations));
    }

    [Fact]
    public async Task RestartingDoesNotReapplyMigrations()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using (var first = infrastructure.CreateApi(database.ConnectionString, migrate: true))
        {
            using var client = first.CreateClient();
        }

        await using var second = infrastructure.CreateApi(database.ConnectionString, migrate: true);
        using var secondClient = second.CreateClient();

        Assert.Equal(1L, await database.CountAsync(CountMigrations));
    }

    [Fact]
    public async Task DatabaseIsUntouchedWhenMigrationIsNotRequested()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString, migrate: false);

        using var client = factory.CreateClient();

        Assert.Equal(0L, await database.CountTablesAsync("identite", "organisations"));
    }

    [Fact]
    public async Task InvalidConfigurationStopsStartupBeforeAnyMigration()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString, migrate: true, bucket: string.Empty);

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(0L, await database.CountTablesAsync("identite", "organisations"));
    }
}

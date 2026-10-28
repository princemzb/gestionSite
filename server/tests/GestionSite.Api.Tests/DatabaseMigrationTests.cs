using Microsoft.Extensions.Options;

namespace GestionSite.Api.Tests;

/// <summary>Migrations appliquées au démarrage sur un PostgreSQL réel, base dédiée à chaque test.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class DatabaseMigrationTests(InfrastructureFixture infrastructure)
{
    private const string CountMigrations = "SELECT count(*) FROM identite.__ef_migrations";

    [Fact]
    public async Task MigrationsCreateTheIdentiteSchemaAtStartup()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString, migrateOnStartup: "true");

        using var client = factory.CreateClient();

        Assert.Equal(1L, await database.CountTablesAsync("identite", "organisations"));
        Assert.Equal(1L, await database.CountAsync(CountMigrations));
    }

    [Fact]
    public async Task RestartingDoesNotReapplyMigrations()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using (var first = infrastructure.CreateApi(database.ConnectionString, migrateOnStartup: "true"))
        {
            using var client = first.CreateClient();
        }

        using var logs = new RecordingLoggerProvider();
        await using var second = infrastructure.CreateApi(database.ConnectionString, migrateOnStartup: "true", logs: logs);
        using var secondClient = second.CreateClient();

        Assert.Equal(1L, await database.CountAsync(CountMigrations));
        Assert.Contains("Schéma de la base à jour pour le module Identite", logs.Messages);
        Assert.DoesNotContain(logs.Messages, m => m.StartsWith("Application des migrations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DatabaseIsUntouchedWhenMigrationIsNotRequested()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString, migrateOnStartup: "false");

        using var client = factory.CreateClient();

        Assert.Equal(0L, await database.CountTablesAsync("identite", "organisations"));
    }

    [Theory]
    [InlineData("Host=ep-exemple.eu-central-1.aws.neon.tech;Database=gestionsite;Username=x;Password=x")]
    [InlineData("Host=localhost,db.example.org;Database=gestionsite;Username=x;Password=x")]
    public async Task StartupMigrationIsRefusedForANonLocalDatabase(string connectionString)
    {
        await using var factory = infrastructure.CreateApi(connectionString, migrateOnStartup: "true");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("base PostgreSQL locale", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidConfigurationStopsStartupBeforeAnyMigration()
    {
        await using var database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        await using var factory = infrastructure.CreateApi(database.ConnectionString, migrateOnStartup: "true", bucket: string.Empty);

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal(0L, await database.CountTablesAsync("identite", "organisations"));
    }
}

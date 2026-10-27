using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>Migrations appliquées au démarrage sur un PostgreSQL réel, base dédiée à chaque test.</summary>
public sealed class DatabaseMigrationTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    [Fact]
    public async Task MigrationsCreateTheIdentiteSchemaAtStartup()
    {
        var connectionString = await infrastructure.CreateDatabaseAsync();
        await using var factory = CreateFactory(connectionString, migrate: true);

        using var client = factory.CreateClient();

        Assert.True(await TableExistsAsync(connectionString, "identite", "organisations"));
        Assert.Equal(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM identite.__ef_migrations"));
    }

    [Fact]
    public async Task RestartingDoesNotReapplyMigrations()
    {
        var connectionString = await infrastructure.CreateDatabaseAsync();
        await using (var first = CreateFactory(connectionString, migrate: true))
        {
            using var client = first.CreateClient();
        }

        await using var second = CreateFactory(connectionString, migrate: true);
        using var secondClient = second.CreateClient();

        Assert.Equal(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM identite.__ef_migrations"));
    }

    [Fact]
    public async Task DatabaseIsUntouchedWhenMigrationIsNotRequested()
    {
        var connectionString = await infrastructure.CreateDatabaseAsync();
        await using var factory = CreateFactory(connectionString, migrate: false);

        using var client = factory.CreateClient();

        Assert.False(await TableExistsAsync(connectionString, "identite", "organisations"));
    }

    private WebApplicationFactory<Program> CreateFactory(string connectionString, bool migrate) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:Main", connectionString)
            .UseSetting("Database:MigrateOnStartup", migrate ? "true" : "false")
            .UseSetting("ObjectStorage:ServiceUrl", infrastructure.StorageUrl)
            .UseSetting("ObjectStorage:AccessKey", InfrastructureFixture.AccessKey)
            .UseSetting("ObjectStorage:SecretKey", InfrastructureFixture.SecretKey)
            .UseSetting("ObjectStorage:Bucket", InfrastructureFixture.Bucket));

    private static async Task<bool> TableExistsAsync(string connectionString, string schema, string table) =>
        await ScalarAsync(connectionString,
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{schema}' AND table_name = '{table}'") == 1L;

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}

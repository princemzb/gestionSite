using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>Base PostgreSQL vide et dédiée à un test, supprimée à la fin du test.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;
    private readonly string _name;

    private TestDatabase(string serverConnectionString, string name)
    {
        _serverConnectionString = serverConnectionString;
        _name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
    }

    internal string ConnectionString { get; }

    private NpgsqlDataSource DataSource { get; set; } = null!;

    internal static async Task<TestDatabase> CreateAsync(string serverConnectionString)
    {
        var database = new TestDatabase(serverConnectionString, $"test_{Guid.NewGuid():N}");
        await database.ExecuteOnServerAsync($"CREATE DATABASE {database.QuotedName}").ConfigureAwait(false);
        database.DataSource = NpgsqlDataSource.Create(database.ConnectionString); // créée une fois la base existante
        return database;
    }

    internal async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    internal Task<long> CountTablesAsync(string schema, string table) =>
        CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table",
            ("schema", schema), ("table", table));

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync().ConfigureAwait(false);
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS {QuotedName} WITH (FORCE)").ConfigureAwait(false);
    }

    // Nom généré localement (préfixe + GUID hexadécimal) puis cité : jamais de donnée externe.
    private string QuotedName => $"\"{_name}\"";

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

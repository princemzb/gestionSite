using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>Base PostgreSQL vide et dédiée à un test, supprimée à la fin du test.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;
    private readonly string _name;
    private readonly NpgsqlDataSource _dataSource;

    private TestDatabase(string serverConnectionString, string name, string connectionString)
    {
        _serverConnectionString = serverConnectionString;
        _name = name;
        ConnectionString = connectionString;
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    internal string ConnectionString { get; }

    internal static async Task<TestDatabase> CreateAsync(string serverConnectionString)
    {
        var name = $"test_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync(serverConnectionString, $"CREATE DATABASE {Quote(name)}").ConfigureAwait(false);

        // Source de données créée seulement une fois la base existante : rien à libérer si CREATE échoue.
        var connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
        return new TestDatabase(serverConnectionString, name, connectionString);
    }

    internal async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = _dataSource.CreateCommand(sql);
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
        try
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await ExecuteOnServerAsync(_serverConnectionString, $"DROP DATABASE IF EXISTS {Quote(_name)} WITH (FORCE)").ConfigureAwait(false);
        }
    }

    // Nom généré localement (préfixe + GUID hexadécimal) puis cité : jamais de donnée externe.
    private static string Quote(string name) => $"\"{name}\"";

    private static async Task ExecuteOnServerAsync(string serverConnectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(serverConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

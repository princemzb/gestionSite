using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Base PostgreSQL vide et dédiée à un test, supprimée à la fin du test, avec les rôles locaux
/// (server/docker/postgres/roles-local.sql). ConnectionString : superutilisateur, pour les
/// vérifications (ignore la RLS) ; l'API utilise les rôles applicatif et propriétaire.
/// </summary>
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

    internal string ApplicationConnectionString => WithRole("gestionsite_app", "local-dev-app-password");

    internal string MigrationsConnectionString => WithRole("gestionsite_migrator", "local-dev-migrator-password");

    internal static async Task<TestDatabase> CreateAsync(string serverConnectionString)
    {
        var name = $"test_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync(serverConnectionString, $"CREATE DATABASE {Quote(name)}").ConfigureAwait(false);

        // Source de données créée seulement une fois la base existante : rien à libérer si CREATE échoue.
        var connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
        var database = new TestDatabase(serverConnectionString, name, connectionString);
        try
        {
            await ExecuteOnServerAsync(connectionString, await File.ReadAllTextAsync(RolesScript).ConfigureAwait(false)).ConfigureAwait(false);
            return database;
        }
        catch
        {
            await database.DisposeAsync().ConfigureAwait(false);
            throw;
        }
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

    private static string RolesScript => Path.Combine(InfrastructureFixture.ServerDirectory(), "docker", "postgres", "roles-local.sql");

    private string WithRole(string username, string password) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Username = username, Password = password }.ConnectionString;

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

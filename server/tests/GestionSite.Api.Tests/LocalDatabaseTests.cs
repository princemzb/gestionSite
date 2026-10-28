using GestionSite.Api.Infrastructure;

namespace GestionSite.Api.Tests;

/// <summary>Garde-fou de la migration au démarrage : seules les bases locales sont migrées.</summary>
public sealed class LocalDatabaseTests
{
    [Theory]
    [InlineData("Host=localhost;Port=55433;Database=gestionsite")]
    [InlineData("Host=LOCALHOST:55433;Database=gestionsite")]
    [InlineData("Host=127.0.0.1;Database=gestionsite")]
    [InlineData("Host=127.0.0.1:5432;Database=gestionsite")]
    [InlineData("Host=::1;Database=gestionsite")]
    [InlineData("Host=[::1]:5432,localhost;Database=gestionsite")]
    [InlineData("Host=/var/run/postgresql;Database=gestionsite")]
    public void LocalDatabasesAreRecognised(string connectionString)
    {
        Assert.True(LocalDatabase.IsLocal(connectionString));
    }

    [Theory]
    [InlineData("Host=ep-exemple.eu-central-1.aws.neon.tech;Database=gestionsite")]
    [InlineData("Host=localhost,db.example.org;Database=gestionsite")]
    [InlineData("Host=10.0.0.5;Database=gestionsite")]
    [InlineData("Host=[2001:db8::1]:5432;Database=gestionsite")]
    [InlineData("Host=localhost.example.org;Database=gestionsite")]
    public void RemoteDatabasesAreRefused(string connectionString)
    {
        Assert.False(LocalDatabase.IsLocal(connectionString));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas une chaîne de connexion")]
    [InlineData("Database=gestionsite;Username=x")]
    public void UndecidableStringsAreRefused(string connectionString)
    {
        Assert.False(LocalDatabase.IsLocal(connectionString));
    }
}

using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Tests;

public sealed class IdentitePersistenceTests
{
    [Fact]
    public void ModelHasNoChangeMissingFromMigrations()
    {
        using var context = new IdentiteDesignTimeFactory().CreateDbContext([]);

        // Échoue si le modèle a changé sans migration : `dotnet ef migrations add <Nom>` manquant.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void ModelUsesTheIdentiteSchemaAndSnakeCaseColumns()
    {
        using var context = new IdentiteDesignTimeFactory().CreateDbContext([]);
        var organisation = context.Model.FindEntityType(typeof(Organisation))!;

        Assert.Equal("identite", organisation.GetSchema());
        Assert.Equal("organisations", organisation.GetTableName());
        Assert.Equal(["cree_le", "id", "nom"], organisation.GetProperties().Select(p => p.GetColumnName()).Order());
        Assert.Equal("pk_organisations", organisation.FindPrimaryKey()!.GetName());
    }

    [Fact]
    public void RuntimeContextUsesTheSameModelAsMigrations()
    {
        var services = new ServiceCollection()
            .AddSingleton(Npgsql.NpgsqlDataSource.Create("Host=localhost"))
            .AddIdentiteModule();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal("pk_organisations", context.Model.FindEntityType(typeof(Organisation))!.FindPrimaryKey()!.GetName());
    }

    [Fact]
    public void OrganisationRequiresACreationDate()
    {
        Assert.Throws<ArgumentException>(() => new Organisation(Guid.NewGuid(), "Saint-Paul", default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OrganisationRequiresAName(string nom)
    {
        Assert.Throws<ArgumentException>(() => new Organisation(Guid.NewGuid(), nom, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void OrganisationRejectsAnEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new Organisation(Guid.Empty, "Saint-Paul", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void OrganisationNameIsLimitedToTheColumnLength()
    {
        _ = new Organisation(Guid.NewGuid(), new string('a', Organisation.NomMaxLength), DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentException>(() =>
            new Organisation(Guid.NewGuid(), new string('a', Organisation.NomMaxLength + 1), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void CreationDateIsStoredInUtc()
    {
        var paris = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(1));

        var organisation = new Organisation(Guid.NewGuid(), "Saint-Paul", paris);

        Assert.Equal(TimeSpan.Zero, organisation.CreeLe.Offset);
        Assert.Equal(paris.UtcDateTime, organisation.CreeLe.UtcDateTime);
    }

    [Fact]
    public void OrganisationNameIsTrimmed()
    {
        var organisation = new Organisation(Guid.NewGuid(), "  Saint-Paul  ", DateTimeOffset.UnixEpoch);

        Assert.Equal("Saint-Paul", organisation.Nom);
    }
}

using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.EntityFrameworkCore;

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
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OrganisationRequiresAName(string nom)
    {
        Assert.Throws<ArgumentException>(() => new Organisation(Guid.NewGuid(), nom, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void OrganisationNameIsTrimmed()
    {
        var organisation = new Organisation(Guid.NewGuid(), "  Saint-Paul  ", DateTimeOffset.UnixEpoch);

        Assert.Equal("Saint-Paul", organisation.Nom);
    }
}

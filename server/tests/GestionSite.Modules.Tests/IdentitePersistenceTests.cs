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
        using var module = new RuntimeContext();

        // Échoue si le modèle a changé sans migration : `dotnet ef migrations add <Nom>` manquant.
        Assert.False(module.Context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void ModelUsesTheIdentiteSchemaAndSnakeCaseColumns()
    {
        using var module = new RuntimeContext();
        var organisation = module.Context.Model.FindEntityType(typeof(Organisation))!;

        Assert.Equal("identite", organisation.GetSchema());
        Assert.Equal("organisations", organisation.GetTableName());
        Assert.Equal(["cree_le", "id", "nom"], organisation.GetProperties().Select(p => p.GetColumnName()).Order());
        Assert.Equal("pk_organisations", organisation.FindPrimaryKey()!.GetName());
    }


    [Fact]
    public void OrganisationRequiresARealCreationDate()
    {
        DateTimeOffset[] dates = [default, DateTimeOffset.MinValue, DateTimeOffset.MaxValue];

        Assert.All(dates, date =>
            Assert.Throws<ArgumentException>(() => new Organisation(Guid.NewGuid(), "Saint-Paul", date)));
    }

    [Theory]
    [InlineData("Saint\0Paul")]
    [InlineData("Saint\nPaul")]
    [InlineData("Saint\u2028Paul")]
    [InlineData("\u200B")]
    [InlineData("\u200B \u2060")]
    [InlineData("\u200BSaint-Paul")]
    [InlineData("Saint-\u202EluaP")]
    [InlineData("\u200D")]
    public void OrganisationRejectsControlCharacters(string nom)
    {
        Assert.Throws<ArgumentException>(() => new Organisation(Guid.NewGuid(), nom, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void OrganisationRejectsLoneSurrogates()
    {
        // Pas de [InlineData] : la sérialisation des données de test remplace les demi-paires par U+FFFD.
        string[] noms = ["Saint\uD800Paul", "Saint Paul\uD800", "\uDC00Saint Paul"];

        Assert.All(noms, nom =>
            Assert.Throws<ArgumentException>(() => new Organisation(Guid.NewGuid(), nom, DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void CreationDateIsKeptToTheMicrosecondLikePostgreSql()
    {
        var creeLe = new DateTimeOffset(2026, 10, 28, 9, 30, 15, TimeSpan.Zero).AddTicks(1_234_567);

        var organisation = new Organisation(Guid.NewGuid(), "Saint-Paul", creeLe);

        Assert.Equal(creeLe.AddTicks(-7), organisation.CreeLe);
    }

    [Fact]
    public void OrganisationAcceptsCharactersOutsideTheBasicPlane()
    {
        const string Nom = "Saint-Paul ⛪ 🕊️ 👨‍👩‍👧"; // le dernier émoji est composé avec U+200D

        Assert.Equal(Nom, new Organisation(Guid.NewGuid(), Nom, DateTimeOffset.UnixEpoch).Nom);
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

    /// <summary>Contexte tel que l'API l'enregistre (même configuration que `dotnet ef` via l'hôte), sans connexion.</summary>
    private sealed class RuntimeContext : IDisposable
    {
        private readonly ServiceProvider _provider = new ServiceCollection()
            .AddSingleton(_ => Npgsql.NpgsqlDataSource.Create("Host=localhost")) // fabrique : libérée avec le conteneur
            .AddIdentiteModule()
            .BuildServiceProvider();

        private readonly IServiceScope _scope;

        internal RuntimeContext()
        {
            _scope = _provider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        }

        internal IdentiteDbContext Context { get; }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}

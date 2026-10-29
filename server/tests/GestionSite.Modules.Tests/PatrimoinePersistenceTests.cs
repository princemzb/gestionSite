using GestionSite.Modules.Patrimoine;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Tests;

/// <summary>Modèle du module Patrimoine et règles de la structure de l'inventaire (US-07, S07-01).</summary>
public sealed class PatrimoinePersistenceTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ModelHasNoChangeMissingFromMigrations()
    {
        using var module = new RuntimeContext();

        Assert.False(module.Context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void EveryEntityIsOrganisationDataFilteredByOrganisation()
    {
        using var module = new RuntimeContext();
        var entites = module.Context.Model.GetEntityTypes().ToList();

        Assert.Equal(4, entites.Count); // bâtiments, niveaux, zones, journal
        Assert.All(entites, t => Assert.True(typeof(IAppartientAOrganisation).IsAssignableFrom(t.ClrType)));
        Assert.All(entites, t => Assert.NotEmpty(t.GetDeclaredQueryFilters()));
        Assert.All(entites, t => Assert.Equal("patrimoine", t.GetSchema()));
    }

    [Fact]
    public void CommonAreasBelongToTheirBuildingAndKeepTheirName()
    {
        var batiment = new Batiment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  Église ", Instant);
        var communes = Zone.PartiesCommunesDe(Guid.NewGuid(), batiment);

        var refus = Assert.Throws<SaisieInvalideException>(() => communes.Renommer("Hall"));

        Assert.Equal("Église", batiment.Nom);
        Assert.True(communes.PartiesCommunes);
        Assert.Null(communes.NiveauId);
        Assert.Equal((batiment.Id, batiment.SiteId, batiment.OrganisationId), (communes.BatimentId, communes.SiteId, communes.OrganisationId));
        Assert.Equal("nom", refus.Champ);
    }

    [Theory]
    [InlineData(-11, false)]
    [InlineData(-1, true)] // sous-sol
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void LevelOrderIsBounded(int ordre, bool permis)
    {
        var batiment = new Batiment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Église", Instant);

        var refus = Record.Exception(() => new Niveau(Guid.NewGuid(), batiment, "Niveau", ordre));

        Assert.Equal(permis, refus is null);
    }

    [Fact]
    public void AZoneCannotUseALevelOfAnotherBuilding()
    {
        var organisation = Guid.NewGuid();
        var site = Guid.NewGuid();
        var eglise = new Batiment(Guid.NewGuid(), organisation, site, "Église", Instant);
        var annexe = new Batiment(Guid.NewGuid(), organisation, site, "Annexe", Instant);
        var etage = new Niveau(Guid.NewGuid(), annexe, "1er étage", 1);

        var refus = Assert.Throws<SaisieInvalideException>(() => new Zone(Guid.NewGuid(), eglise, etage, "Sacristie"));

        Assert.Equal("niveauId", refus.Champ);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Salle\u202Eetuoc")]
    [InlineData("Ligne\nsuivante")]
    public void NamesFollowTheSharedDisplayRules(string nom)
    {
        Assert.Throws<SaisieInvalideException>(() => new Batiment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), nom, Instant));
    }

    private sealed class RuntimeContext : IDisposable
    {
        private readonly ServiceProvider _provider = new ServiceCollection()
            .AddSingleton(_ => Npgsql.NpgsqlDataSource.Create("Host=localhost"))
            .AddScoped<OrganisationCourante>()
            .AddScoped<CompteCourant>()
            .AddPatrimoineModule()
            .BuildServiceProvider();

        private readonly IServiceScope _scope;

        internal RuntimeContext()
        {
            _scope = _provider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<PatrimoineDbContext>();
        }

        internal PatrimoineDbContext Context { get; }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}

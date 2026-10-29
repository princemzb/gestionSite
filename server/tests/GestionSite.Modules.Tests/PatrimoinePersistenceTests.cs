using GestionSite.Modules.Patrimoine;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Tests;

/// <summary>Modèle du module Patrimoine et règles de l'inventaire : structure (S07-01), composants et lots (S07-02).</summary>
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

        Assert.Equal(6, entites.Count); // bâtiments, niveaux, zones, composants, journal, grilles
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

    [Theory]
    [InlineData("SecuriteIncendie", true)]
    [InlineData("Campanaire", true)]
    [InlineData("Mobilier", false)]
    [InlineData("RevetementsFinitions", false)]
    public void PeriodicCheckIsProposedForRegulatedTrades(string corpsEtat, bool proposee)
    {
        Assert.Equal(proposee, CorpsEtats.VerificationProposee(Enum.Parse<CorpsEtat>(corpsEtat)));
    }

    [Fact]
    public void ALotKeepsItsDistributionAndAWithdrawnComponentIsFrozen()
    {
        var zone = Zone.PartiesCommunesDe(Guid.NewGuid(), new Batiment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Église", Instant));
        var lot = new Lot(UniteLot.Unite, 40, new Repartition(30, 0, 8, 0, 2));
        var chaises = new Composant(Guid.NewGuid(), zone, Fiche(lot), Instant);

        chaises.Retirer("Cédées à la paroisse voisine", Instant);

        Assert.Equal(lot, chaises.Lot);
        Assert.Throws<ComposantRetireException>(() => chaises.Modifier(zone, Fiche(lot: null), Instant));
        Assert.Throws<ComposantRetireException>(() => chaises.Retirer("Encore", Instant));
    }

    [Fact]
    public void ADistributionIsNeverNegativeAndAZoneOfAnotherSiteIsRefused()
    {
        var eglise = new Batiment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Église", Instant);
        var zone = Zone.PartiesCommunesDe(Guid.NewGuid(), eglise);
        var ailleurs = Zone.PartiesCommunesDe(Guid.NewGuid(), new Batiment(Guid.NewGuid(), eglise.OrganisationId, Guid.NewGuid(), "Presbytère", Instant));
        var composant = new Composant(Guid.NewGuid(), zone, Fiche(lot: null), Instant);

        var negative = Assert.Throws<SaisieInvalideException>(() => new Composant(
            Guid.NewGuid(), zone, Fiche(new Lot(UniteLot.Unite, 2, new Repartition(4, 0, 0, -2, 0))), Instant));
        var autreSite = Assert.Throws<SaisieInvalideException>(() => composant.Modifier(ailleurs, Fiche(lot: null), Instant));

        Assert.Equal("lot.repartition", negative.Champ);
        Assert.Equal("zoneId", autreSite.Champ);
    }

    [Fact]
    public void ARefusedChangeLeavesTheComponentIntactAndTheCheckFlagIsThenRequired()
    {
        var zone = Zone.PartiesCommunesDe(Guid.NewGuid(), new Batiment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Église", Instant));
        var composant = new Composant(Guid.NewGuid(), zone, Fiche(lot: null) with { SoumisAVerification = null, CorpsEtat = CorpsEtat.Gaz }, Instant);

        var marque = Assert.Throws<SaisieInvalideException>(() => composant.Modifier(zone, Fiche(lot: null) with { Nom = "Autre", Marque = new string('m', 121) }, Instant));
        var verification = Assert.Throws<SaisieInvalideException>(() => composant.Modifier(zone, Fiche(lot: null) with { SoumisAVerification = null }, Instant));

        Assert.True(composant.SoumisAVerification); // proposé pour le gaz
        Assert.Equal(("marque", "Chaises"), (marque.Champ, composant.Nom));
        Assert.Equal("soumisAVerification", verification.Champ);
    }

    [Fact]
    public void AMonthAlreadyStartedSomewhereIsNotInTheFuture()
    {
        var zone = Zone.PartiesCommunesDe(Guid.NewGuid(), new Batiment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Église", Instant));
        var veilleUtc = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero); // déjà le 1er novembre à Paris

        var novembre = new Composant(Guid.NewGuid(), zone, Fiche(lot: null) with { AnneeMiseEnService = 2026, MoisMiseEnService = 11 }, veilleUtc);
        var futur = Assert.Throws<SaisieInvalideException>(() =>
            new Composant(Guid.NewGuid(), zone, Fiche(lot: null) with { AnneeMiseEnService = 2026, MoisMiseEnService = 12 }, veilleUtc));

        Assert.Equal(11, novembre.MoisMiseEnService);
        Assert.Equal("moisMiseEnService", futur.Champ);
    }

    private static FicheComposant Fiche(Lot? lot) => new(
        "Chaises", CorpsEtat.Mobilier, Emplacement: null, Marque: null, Modele: null, NumeroSerie: null,
        AnneeMiseEnService: 2019, MoisMiseEnService: null, Remarques: "Empilables\nhêtre", SoumisAVerification: false, lot);

    [Fact]
    public void EveryTradeHasADefaultGridWithObservablePoints()
    {
        Assert.Equal(Enum.GetValues<CorpsEtat>().Order(), GrillesParDefaut.CorpsEtats.Order());
        Assert.All(GrillesParDefaut.CorpsEtats, c => Assert.InRange(GrillesParDefaut.Pour(c).Criteres.Count, 1, VersionGrille.CriteresMax));
    }

    [Fact]
    public void AnAdaptedGridIsCleanedAndKeepsItsContent()
    {
        var contenu = VersionGrille.Valider(["  Assise intacte ", "", "\u200B", "Pieds stables"], [("Hauteur", "cm"), (" ", null)]);
        var version = new VersionGrille(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CorpsEtat.Mobilier, 2, contenu, null, Instant);

        Assert.Equal(["Assise intacte", "Pieds stables"], version.Contenu.Criteres);
        Assert.Equal([new MesureGrille("Hauteur", "cm")], version.Contenu.Mesures);
        Assert.Throws<SaisieInvalideException>(() => VersionGrille.Valider([.. Enumerable.Range(1, 16).Select(i => $"Point {i}")], null));
        Assert.Throws<SaisieInvalideException>(() => VersionGrille.Valider(["Fuite"], [(" ", "bar")]));
        Assert.Throws<SaisieInvalideException>(() => VersionGrille.Valider(["Fuite"], [("Pression", "bar"), ("PRESSION", "bar")]));
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

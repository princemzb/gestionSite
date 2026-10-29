using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Maintenance;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Tests;

/// <summary>Modèle du module Maintenance et règles de saisie du dépôt public (S08-01).</summary>
public sealed class MaintenancePersistenceTests
{
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

        Assert.Equal(2, entites.Count); // signalements, alertes
        Assert.All(entites, t => Assert.True(typeof(IAppartientAOrganisation).IsAssignableFrom(t.ClrType)));
        Assert.All(entites, t => Assert.NotEmpty(t.GetDeclaredQueryFilters()));
        Assert.All(entites, t => Assert.Equal("maintenance", t.GetSchema()));
    }

    [Fact]
    public async Task WritingWithoutTheReportsOrganisationIsRefusedBeforeReachingTheDatabase()
    {
        using var module = new RuntimeContext();
        module.Context.Signalements.Add(Signalement(Guid.NewGuid()));

        var refus = await Assert.ThrowsAsync<InvalidOperationException>(() => module.Context.SaveChangesAsync());

        Assert.Contains("autre organisation", refus.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("k7p2q-m8x3z", "K7P2QM8X3Z")]
    [InlineData(" K7P2QM8X3Z ", "K7P2QM8X3Z")]
    [InlineData("K7P2QM8X3", null)] // trop court
    [InlineData("K7P2QM8X3ZZ", null)] // trop long
    [InlineData("K7P2QM8X3O", null)] // O ambigu, hors alphabet
    [InlineData("K7P2QM8X1Z", null)] // 1 ambigu, hors alphabet
    [InlineData(null, null)]
    public void PublicSiteCodesAreNormalisedOrRejected(string? saisi, string? attendu)
    {
        Assert.Equal(attendu, CodeSitePublic.Normaliser(saisi));
    }

    [Fact]
    public void NewPublicSiteCodesAreValidAndDistinct()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => CodeSitePublic.Nouveau(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow).Code).ToList();

        Assert.All(codes, c => Assert.Equal(c, CodeSitePublic.Normaliser(c)));
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ReportTextIsTrimmedAndADangerAlertFollowsItsReport()
    {
        var signalement = Signalement(Guid.NewGuid());
        var alerte = new AlerteDanger(Guid.NewGuid(), signalement);

        Assert.Equal("Grande salle", signalement.Emplacement);
        Assert.Equal((signalement.Id, signalement.OrganisationId, signalement.SiteId, signalement.RecuLe),
            (alerte.SignalementId, alerte.OrganisationId, alerte.SiteId, alerte.CreeLe));
    }

    [Fact]
    public void AnUnknownNatureIsRefused()
    {
        var refus = Assert.Throws<SaisieInvalideException>(() => new Signalement(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (NatureSignalement)9, "d", "e", false, false, "n", "c", DateTimeOffset.UtcNow));

        Assert.Equal("nature", refus.Champ);
    }

    [Theory]
    [InlineData("Ligne 1\nLigne 2\tsuite", true, "Ligne 1\nLigne 2\tsuite")]
    [InlineData("Ligne 1\nLigne 2", false, null)]
    [InlineData("Nul\u0000", true, null)]
    [InlineData("a\u202Ebc", true, null)] // en bordure, il serait seulement retiré
    [InlineData(" \u200B ", true, null)]
    [InlineData("  Cafe\u0301  ", false, "Café")]
    public void SharedDisplayTextRules(string saisi, bool multiligne, string? attendu)
    {
        Assert.Equal(attendu, TexteAffichable.Nettoyer(saisi, 200, multiligne));
    }

    [Fact]
    public void SameSubmissionMeansSameSiteAndContent()
    {
        var premier = Signalement(Guid.NewGuid());
        var copie = new Signalement(Guid.NewGuid(), premier.OrganisationId, premier.SiteId, premier.CommandeId, premier.Nature, premier.Description,
            premier.Emplacement, premier.Danger, premier.ActiviteBloquee, premier.NomDeclarant, premier.ContactDeclarant, DateTimeOffset.UtcNow);
        var autreSite = new Signalement(Guid.NewGuid(), premier.OrganisationId, Guid.NewGuid(), premier.CommandeId, premier.Nature, premier.Description,
            premier.Emplacement, premier.Danger, premier.ActiviteBloquee, premier.NomDeclarant, premier.ContactDeclarant, DateTimeOffset.UtcNow);

        Assert.True(premier.MemeEnvoi(copie));
        Assert.False(premier.MemeEnvoi(autreSite));
    }

    private static Signalement Signalement(Guid organisation) => new(
        Guid.NewGuid(), organisation, Guid.NewGuid(), Guid.NewGuid(), NatureSignalement.Incident, " Fuite ", "  Grande salle ",
        danger: true, activiteBloquee: false, "Jeanne", "06 00 00 00 00", DateTimeOffset.UtcNow);

    private sealed class RuntimeContext : IDisposable
    {
        private readonly ServiceProvider _provider = new ServiceCollection()
            .AddSingleton(_ => Npgsql.NpgsqlDataSource.Create("Host=localhost")) // aucune connexion ouverte par ces tests
            .AddScoped<OrganisationCourante>()
            .AddScoped<CompteCourant>()
            .AddMaintenanceModule()
            .BuildServiceProvider();

        private readonly IServiceScope _scope;

        internal RuntimeContext()
        {
            _scope = _provider.CreateScope();
            _scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(Guid.NewGuid());
            Context = _scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        }

        internal MaintenanceDbContext Context { get; }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}

using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Preventif;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

/// <summary>Classement ERP déclaré et catalogue des plans proposés (US-12, S12-01) : toujours « à confirmer », jamais « conforme ».</summary>
public sealed class PreventifCatalogueTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Aujourdhui = new(2026, 10, 30);
    private static readonly Guid Eglise = Guid.NewGuid();

    [Fact]
    public void AnErpHasACategoryAndTheChurchTypeByDefault()
    {
        var classement = ClassementErp.Valider(true, type: null, [TypeErp.L], 5, 120, false, true, ReponseConnue.Inconnu, null, Instant);
        var sansCategorie = Assert.Throws<SaisieInvalideException>(() =>
            ClassementErp.Valider(true, TypeErp.V, null, null, null, false, false, ReponseConnue.Non, null, Instant));
        var horsErp = Assert.Throws<SaisieInvalideException>(() =>
            ClassementErp.Valider(false, TypeErp.V, null, null, null, false, false, ReponseConnue.Non, null, Instant));
        var memeType = Assert.Throws<SaisieInvalideException>(() =>
            ClassementErp.Valider(true, TypeErp.V, [TypeErp.V], 5, null, false, false, ReponseConnue.Non, null, Instant));
        var future = Assert.Throws<SaisieInvalideException>(() =>
            ClassementErp.Valider(true, TypeErp.V, null, 3, null, false, false, ReponseConnue.Non, new DateOnly(2026, 11, 2), Instant));
        var sansPermis = Assert.Throws<SaisieInvalideException>(() =>
            ClassementErp.Valider(false, null, null, null, null, null, false, null, null, Instant));

        Assert.Equal((TypeErp.V, 5, "L"), (classement.Type!.Value, classement.Categorie!.Value, ClassementErp.Joindre(classement.TypesSecondaires)));
        Assert.Equal(["categorie", "erp", "typesSecondaires", "derniereVisiteCommission", "permisAvant1997"],
            new[] { sansCategorie.Champ, horsErp.Champ, memeType.Champ, future.Champ, sansPermis.Champ });
        Assert.Equal([TypeErp.L, TypeErp.N], ClassementErp.Separer("L,N"));
        Assert.Equal("5", classement.Releve()["categorie"]!.ToString());
    }

    [Fact]
    public void AFifthCategoryChurchGetsNeitherAnApprovedBodyNorTheCommission()
    {
        var propositions = Catalogue.Proposer(Etablissement(Erp(5)), Composants("CourantsForts", "EclairageSecurite", "SecuriteIncendie"), Aujourdhui);

        Assert.DoesNotContain(propositions, p => p.Modele.Intervenant == TypeIntervenant.OrganismeAgree);
        Assert.DoesNotContain(propositions, p => p.Modele.Code == "commission-visite");
        var electricite = propositions.Single(p => p.Modele.Code == "electricite-verification");
        Assert.Equal((TypeIntervenant.TechnicienCompetent, BasePlan.Reglementaire), (electricite.Modele.Intervenant, electricite.Modele.Base));
        Assert.All(propositions, p => Assert.EndsWith(Catalogue.AConfirmer, p.Modele.Reference, StringComparison.Ordinal));
        Assert.All(propositions, p => Assert.DoesNotContain("conforme", p.Modele.Libelle, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AThirdCategoryChurchGetsTheApprovedBodyAndTheCommissionFromItsLastVisit()
    {
        var propositions = Catalogue.Proposer(Etablissement(Erp(3, visite: new DateOnly(2023, 3, 31))), Composants("CourantsForts"), Aujourdhui);

        Assert.Equal(TypeIntervenant.OrganismeAgree, propositions.Single(p => p.Modele.Code == "electricite-verification").Modele.Intervenant);
        var commission = propositions.Single(p => p.Modele.Code == "commission-visite");
        Assert.Equal((5, UnitePeriode.Ans), (commission.Modele.Periode.Valeur, commission.Modele.Periode.Unite));
        Assert.Equal(new DateOnly(2028, 3, 31), commission.Echeance);
        Assert.Equal(CiblePlan.Batiment, commission.Cible);
        Assert.Equal($"commission-visite:batiment:{Eglise:N}", commission.Cle);
        Assert.Contains(propositions, p => p.Modele.Code == "dae-maintenance");
    }

    [Fact]
    public void SleepingPremisesBringTheCommissionToAFifthCategoryAndSmallCategoriesComeEveryThreeYears()
    {
        var sommeil = Catalogue.Proposer(Etablissement(Erp(5, sommeil: true)), [], Aujourdhui);
        var premiere = Catalogue.Proposer(Etablissement(Erp(1)), [], Aujourdhui);

        Assert.Contains(sommeil, p => p.Modele.Code == "commission-visite");
        Assert.Equal(3, premiere.Single(p => p.Modele.Code == "commission-visite").Modele.Periode.Valeur);
        Assert.Equal(Aujourdhui, premiere.Single(p => p.Modele.Code == "commission-visite").Echeance); // dernière visite inconnue
    }

    [Fact]
    public void EmployeesBringLabourCodeChecksToANonErpBuilding()
    {
        var sansSalaries = Catalogue.Proposer(Etablissement(NonErp("Non")), Composants("CourantsForts", "SecuriteIncendie", "MenuiseriesExterieures"), Aujourdhui);
        var avecSalaries = Catalogue.Proposer(
            Etablissement(NonErp("Non"), salaries: true), Composants("CourantsForts", "SecuriteIncendie", "MenuiseriesExterieures"), Aujourdhui);

        Assert.DoesNotContain(sansSalaries, p => p.Modele.Code is "electricite-verification" or "portes-automatiques");
        Assert.Equal(BasePlan.BonnePratique, sansSalaries.Single(p => p.Modele.Code == "incendie-verification").Modele.Base);
        Assert.Contains("R.4226-16", avecSalaries.Single(p => p.Modele.Code == "electricite-verification").Modele.Reference, StringComparison.Ordinal);
        Assert.Equal(BasePlan.Reglementaire, avecSalaries.Single(p => p.Modele.Code == "incendie-verification").Modele.Base);
        Assert.Equal(6, avecSalaries.Single(p => p.Modele.Code == "portes-automatiques").Modele.Periode.Valeur);
    }

    [Theory]
    [InlineData("Oui", true)]
    [InlineData("Inconnu", true)]
    [InlineData("Non", false)]
    public void AsbestosIsProposedUnlessThePermitIsKnownToBeRecent(string permis, bool propose)
    {
        Assert.Equal(propose, Catalogue.Proposer(Etablissement(NonErp(permis)), [], Aujourdhui).Any(p => p.Modele.Code == "amiante-surveillance"));
    }

    [Fact]
    public void GuttersAreASeasonalTaskAtTheEndOfOctober()
    {
        var cheneaux = Catalogue.Proposer(Etablissement(classement: null), [], new DateOnly(2026, 11, 2)).Single();
        var avant = Catalogue.Proposer(Etablissement(classement: null), [], new DateOnly(2026, 10, 31)).Single();

        Assert.Equal(("cheneaux-nettoyage", ModeEcheance.Fixe), (cheneaux.Modele.Code, cheneaux.Modele.Mode));
        Assert.Equal(new DateOnly(2027, 10, 31), cheneaux.Echeance);
        Assert.Equal(new DateOnly(2026, 10, 31), avant.Echeance);
    }

    [Theory]
    [InlineData("EclairageSecurite", new[] { "eclairage-essai" })]
    [InlineData("Ascenseurs", new[] { "inspection-interne", "ascenseur-entretien", "ascenseur-controle-technique" })]
    [InlineData("ChauffageVentilation", new[] { "inspection-interne", "chaudiere-entretien" })]
    [InlineData("Gaz", new[] { "inspection-interne", "gaz-verification" })]
    [InlineData("Paratonnerre", new[] { "inspection-interne", "paratonnerre-verification" })]
    [InlineData("Campanaire", new[] { "inspection-interne", "campanaire-entretien" })]
    [InlineData("Mobilier", new[] { "inspection-interne" })]
    public void EveryCheckedComponentReceivesTheModelsOfItsTrade(string corpsEtat, string[] codes)
    {
        var propositions = Catalogue.Proposer(Etablissement(NonErp("Non")), Composants(corpsEtat), Aujourdhui).Where(p => p.ComposantId is not null);

        Assert.Equal(codes, propositions.Select(p => p.Modele.Code));
    }

    [Fact]
    public void ErpTradesGetTheirRegulatoryVerification()
    {
        var propositions = Catalogue.Proposer(Etablissement(Erp(4)), Composants("EclairageSecurite", "ChauffageVentilation", "Gaz"), Aujourdhui);
        var cinquieme = Catalogue.Proposer(Etablissement(Erp(5)), Composants("EclairageSecurite"), Aujourdhui);

        Assert.Equal(TypeIntervenant.OrganismeAgree, propositions.Single(p => p.Modele.Code == "eclairage-verification").Modele.Intervenant);
        Assert.Equal(BasePlan.Reglementaire, propositions.Single(p => p.Modele.Code == "eclairage-essai").Modele.Base);
        Assert.Contains(propositions, p => p.Modele.Code == "chauffage-verification");
        Assert.Contains("GZ 30", propositions.Single(p => p.Modele.Code == "gaz-verification").Modele.Reference, StringComparison.Ordinal);
        Assert.Equal(TypeIntervenant.TechnicienCompetent, cinquieme.Single(p => p.Modele.Code == "eclairage-verification").Modele.Intervenant);
    }

    [Fact]
    public void WithdrawnOrUncheckedComponentsAndUnknownBuildingsAreHandled()
    {
        var composants = new[]
        {
            new ComposantInventaire(Guid.NewGuid(), "Tableau", Guid.NewGuid(), "Église · Nef", Eglise, "CourantsForts", true, false, Retire: true),
            new ComposantInventaire(Guid.NewGuid(), "Prise", Guid.NewGuid(), "Église · Nef", Eglise, "CourantsForts", false, false, false),
            new ComposantInventaire(Guid.NewGuid(), "Chaudière", Guid.NewGuid(), "Annexe · Cave", Guid.NewGuid(), "ChauffageVentilation", true, false, false),
        };

        var propositions = Catalogue.Proposer(new EtablissementInventaire(null, []), composants, Aujourdhui);

        Assert.Equal(["inspection-interne", "chaudiere-entretien"], propositions.Select(p => p.Modele.Code));
        Assert.Equal("Chaudière (Annexe · Cave)", propositions[0].LibelleCible);
    }

    [Theory]
    [InlineData(2026, 1, 31, 1, "Mois", 1, 2026, 2, 28)]
    [InlineData(2026, 1, 31, 1, "Mois", 2, 2026, 3, 31)] // comptée depuis la date d'origine : pas de dérive
    [InlineData(2024, 2, 29, 1, "Ans", 1, 2025, 2, 28)]
    [InlineData(2024, 2, 29, 1, "Ans", 4, 2028, 2, 29)]
    [InlineData(2026, 10, 30, 6, "Semaines", 1, 2026, 12, 11)]
    [InlineData(2026, 10, 30, 1, "Ans", -1, 2025, 10, 30)]
    public void PeriodsKeepMonthEnds(int annee, int mois, int jour, int valeur, string unite, int fois, int a, int m, int j)
    {
        Assert.Equal(new DateOnly(a, m, j), new Periode(valeur, Enum.Parse<UnitePeriode>(unite)).Apres(new DateOnly(annee, mois, jour), fois));
    }

    [Theory]
    [InlineData(0, 2, "periode.valeur")]
    [InlineData(121, 3, "periode.valeur")]
    [InlineData(1, 9, "periode.unite")]
    public void PeriodsAreBounded(int valeur, int unite, string champ)
    {
        Assert.Equal(champ, Assert.Throws<SaisieInvalideException>(() => Periode.Valider(valeur, (UnitePeriode)unite)).Champ);
        Assert.Equal(new DateOnly(2026, 9, 30), Periode.Valider(1, UnitePeriode.Mois).Avant(new DateOnly(2026, 10, 30)));
    }

    private static ClassementBatiment Erp(int categorie, bool sommeil = false, DateOnly? visite = null) =>
        new(true, "V", [], categorie, sommeil, false, "Non", visite);

    private static ClassementBatiment NonErp(string permis) => new(false, null, [], null, false, false, permis, null);

    private static EtablissementInventaire Etablissement(ClassementBatiment? classement, bool salaries = false) =>
        new(salaries, [new BatimentInventaire(Eglise, "Église", classement, Guid.NewGuid())]);

    private static ComposantInventaire[] Composants(params string[] corpsEtats) =>
        [.. corpsEtats.Select(c => new ComposantInventaire(Guid.NewGuid(), c, Guid.NewGuid(), "Église · Nef", Eglise, c, true, false, false))];
}

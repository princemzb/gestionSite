using GestionSite.Web.Preventif;
using GestionSite.Web.Session;

namespace GestionSite.Api.Tests;

/// <summary>Calendrier du préventif (S12-06) : échéances suivantes prévues par la périodicité, sans base de données.</summary>
public sealed class EcheancesProjeteesTests
{
    private static readonly DateOnly Aujourdhui = new(2027, 1, 1);

    [Fact]
    public void AMonthlyPlanAppearsEveryMonthAfterItsNextDueDateWithoutEndOfMonthDrift()
    {
        var plan = Plan(new PeriodeApi(1, "Mois"), new DateOnly(2027, 1, 31), "EnRetard");

        var fevrierMars = EcheancesProjetees.Projeter([plan], new DateOnly(2027, 2, 1), new DateOnly(2027, 3, 31), Aujourdhui);
        var janvier = EcheancesProjetees.Projeter([plan], new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), Aujourdhui);

        Assert.Equal([new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31)], fevrierMars.Keys.Order());
        Assert.All(fevrierMars.Values.SelectMany(e => e), e => Assert.True(e.Prevue && e.Etat == "AVenir"));
        Assert.False(Assert.Single(janvier[new DateOnly(2027, 1, 31)]).Prevue);
        Assert.Equal("EnRetard", janvier[new DateOnly(2027, 1, 31)][0].Etat);
    }

    [Fact]
    public void ASuspendedOrOneOffPlanIsNotProjectedAndFarDatesStayBounded()
    {
        var suspendu = Plan(new PeriodeApi(1, "Semaines"), new DateOnly(2027, 1, 4), "AVenir") with { Suspendu = true };
        var ponctuel = Plan(periode: null, new DateOnly(2027, 1, 4), "AVenir");
        var hebdomadaire = Plan(new PeriodeApi(1, "Semaines"), new DateOnly(2027, 1, 4), "AVenir");

        var projete = EcheancesProjetees.Projeter([suspendu, ponctuel, hebdomadaire], new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), Aujourdhui);
        var lointain = EcheancesProjetees.Projeter([hebdomadaire], new DateOnly(2100, 12, 1), new DateOnly(2100, 12, 31), Aujourdhui);

        Assert.Equal(4, projete.Values.Sum(l => l.Count(e => e.Plan == hebdomadaire))); // 4, 11, 18 et 25 janvier
        Assert.Single(projete[new DateOnly(2027, 1, 4)], e => e.Plan == ponctuel);
        Assert.DoesNotContain(projete.Values.SelectMany(l => l), e => e.Plan == suspendu);
        Assert.NotEmpty(lointain);
        Assert.Equal(DateOnly.MaxValue, EcheancesProjetees.Apres(new DateOnly(2027, 1, 1), new PeriodeApi(5, "Ans"), 5000));
    }

    [Fact]
    public void AFixedPlanIsCountedFromItsFirstDueDateLikeTheApi()
    {
        // Première échéance le 31 janvier, prochaine raccourcie au 28 février : mars et avril reviennent au 31 et au 30.
        var plan = Plan(new PeriodeApi(1, "Mois"), new DateOnly(2027, 2, 28), "AFaire") with { Mode = "Fixe", PremiereEcheance = new DateOnly(2027, 1, 31) };

        var marsAvril = EcheancesProjetees.Projeter([plan], new DateOnly(2027, 3, 1), new DateOnly(2027, 4, 30), new DateOnly(2027, 2, 20));

        Assert.Equal([new DateOnly(2027, 3, 31), new DateOnly(2027, 4, 30)], marsAvril.Keys.Order());
    }

    [Fact]
    public void NoPlannedDateIsShownInThePastOnlyTheOverdueOne()
    {
        var plan = Plan(new PeriodeApi(1, "Mois"), new DateOnly(2027, 8, 15), "EnRetard");

        var octobre = EcheancesProjetees.Projeter([plan], new DateOnly(2027, 8, 1), new DateOnly(2027, 11, 30), new DateOnly(2027, 10, 20));

        Assert.Equal([new DateOnly(2027, 8, 15), new DateOnly(2027, 11, 15)], octobre.Keys.Order()); // ni le 15 septembre ni le 15 octobre
        Assert.False(octobre[new DateOnly(2027, 8, 15)][0].Prevue);
    }

    private static PlanApi Plan(PeriodeApi? periode, DateOnly echeance, string etat) => new(
        Guid.NewGuid(), "Plan", "Entretien", "BonnePratique", Reference: null, Modele: null, PropositionCle: null, TypeIntervenant: null,
        new CibleApi("Batiment", Guid.NewGuid(), Guid.NewGuid(), ComposantId: null, CorpsEtat: null, "Église"), Intervenant: null, DonneurOrdre: null,
        periode, "Glissante", echeance, Suspendu: false, MotifSuspension: null, SuspensionAutomatique: false,
        new EcheanceApi(echeance, echeance.AddDays(-7), etat, Statut: null, OccurrenceId: null, Intervenant: null, Version: 1), Version: 1);
}

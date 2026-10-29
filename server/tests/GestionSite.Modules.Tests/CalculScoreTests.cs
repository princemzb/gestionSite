using GestionSite.Modules.Patrimoine.Domain;

namespace GestionSite.Modules.Tests;

/// <summary>Score du site recalculé depuis les évaluations datées (DEC-01, décisions US-11 du 29/10).</summary>
public sealed class CalculScoreTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Salle = Guid.NewGuid();
    private static readonly Guid Communes = Guid.NewGuid();
    private static readonly Guid Parking = Guid.NewGuid();
    private static readonly Guid[] Zones = [Salle, Communes, Parking];

    [Fact]
    public void TheSiteIsTheMeanOfEvaluatedZonesAndAnUnevaluatedComponentOnlyLowersCoverage()
    {
        var (chaises, tableau, sono, extincteur) = (Composant(Salle), Composant(Salle), Composant(Salle), Composant(Communes));
        var notes = new[]
        {
            Note(chaises, Maintenant.AddDays(-3), Echelle.Note(new Repartition(30, 0, 8, 0, 2)), critique: true), // lot : 7,8
            Note(tableau, Maintenant.AddDays(-2), 5),
            Note(extincteur, Maintenant.AddDays(-1), 9),
        };

        var etat = CalculScore.Calculer(Zones, [chaises, tableau, sono, extincteur], notes, Maintenant);
        var salle = etat.Zones.Single(z => z.ZoneId == Salle);

        Assert.Equal(6.4m, salle.Note); // (7,8 + 5) / 2 : le lot compte pour un composant, « sono » non évalué
        Assert.Equal((3, 2), (salle.Actifs, salle.Evalues));
        Assert.Equal(7.7m, etat.Note); // (6,4 + 9) / 2 : Parties communes comptées comme une zone
        Assert.Null(etat.Zones.Single(z => z.ZoneId == Parking).Note); // zone vide : ni note ni effet sur le site
        Assert.Equal((4, 3, 1), (etat.Actifs, etat.Evalues, etat.Critiques));
        Assert.Equal(0m, etat.PartAncienne);
    }

    [Fact]
    public void OnlyTheLatestEvaluationAtTheDateCountsAndRetiredComponentsLeaveTheScore()
    {
        var tableau = Composant(Salle);
        var chaises = Composant(Salle) with { RetireLe = Maintenant.AddDays(-5) };
        var notes = new[]
        {
            Note(tableau, Maintenant.AddDays(-20), 9),
            Note(tableau, Maintenant.AddDays(-2), 3),
            Note(chaises, Maintenant.AddDays(-20), 1, critique: true),
        };

        var aujourdhui = CalculScore.Calculer(Zones, [tableau, chaises], notes, Maintenant);
        var avant = CalculScore.Calculer(Zones, [tableau, chaises], notes, Maintenant.AddDays(-10));

        Assert.Equal(3m, aujourdhui.Note);
        Assert.Equal(0, aujourdhui.Critiques);
        Assert.Equal(5m, avant.Note); // (9 + 1) / 2 : chaises encore en service, dernière évaluation du tableau à 9
        Assert.Equal(1, avant.Critiques);
    }

    [Fact]
    public void AnOldEvaluationStillCountsAndItsWeightInTheSiteScoreIsReported()
    {
        var (ancien, recent, extincteur) = (Composant(Salle), Composant(Salle), Composant(Communes));
        var notes = new[]
        {
            Note(ancien, Maintenant.AddMonths(-7), 5),
            Note(recent, Maintenant.AddMonths(-1), 9),
            Note(extincteur, Maintenant.AddMonths(-6).AddDays(1), 7), // moins de 6 mois : récente
        };

        var etat = CalculScore.Calculer(Zones, [ancien, recent, extincteur], notes, Maintenant);
        var salle = etat.Zones.Single(z => z.ZoneId == Salle);

        Assert.Equal(7m, salle.Note);
        Assert.Equal(1, salle.Anciennes);
        Assert.Equal(Maintenant.AddMonths(-7), salle.PlusAncienne);
        Assert.Equal(0.25m, etat.PartAncienne); // la moitié de la salle, qui pèse la moitié du site
    }

    [Fact]
    public void TheHistoryGivesTwelveMonthEndsInParisTime()
    {
        var tableau = Composant(Salle) with { CreeLe = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var notes = new[]
        {
            Note(tableau, new DateTimeOffset(2026, 3, 31, 21, 30, 0, TimeSpan.Zero), 5), // 31/03 23 h 30 à Paris : mars
            Note(tableau, new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero), 9), // 1er/05 0 h 30 à Paris : mai
            Note(tableau, Maintenant.AddHours(-1), 3),
        };

        var historique = CalculScore.Historique(Zones, [tableau], notes, Maintenant);

        Assert.Equal(12, historique.Count);
        Assert.Equal(new DateOnly(2025, 11, 1), historique[0].Mois);
        Assert.Equal(new DateOnly(2026, 10, 1), historique[^1].Mois);
        Assert.Null(historique.Single(p => p.Mois == new DateOnly(2026, 2, 1)).Note);
        Assert.Equal(5m, historique.Single(p => p.Mois == new DateOnly(2026, 3, 1)).Note);
        Assert.Equal(5m, historique.Single(p => p.Mois == new DateOnly(2026, 4, 1)).Note);
        Assert.Equal(9m, historique.Single(p => p.Mois == new DateOnly(2026, 5, 1)).Note);
        Assert.Equal(3m, historique[^1].Note); // mois en cours arrêté à maintenant
    }

    private static ComposantSuivi Composant(Guid zone) => new(Guid.NewGuid(), zone, Maintenant.AddYears(-2), null);

    private static NoteDatee Note(ComposantSuivi composant, DateTimeOffset le, decimal note, bool critique = false) =>
        new(Guid.CreateVersion7(le), composant.Id, le, note, critique);
}

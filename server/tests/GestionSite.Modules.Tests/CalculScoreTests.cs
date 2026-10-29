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
            Lot(chaises, Maintenant.AddDays(-3), new Repartition(30, 0, 8, 0, 2)), // 7,8, avec 2 unités critiques
            Note(tableau, Maintenant.AddDays(-2), EtatComposant.Degrade),
            Note(extincteur, Maintenant.AddDays(-1), EtatComposant.Bon),
        };

        var etat = new CalculScore(Zones, [chaises, tableau, sono, extincteur], notes).Calculer(Maintenant);
        var salle = etat.Zones.Single(z => z.ZoneId == Salle);

        Assert.Equal(6.4m, Echelle.Arrondie(salle.Note!.Value)); // (7,8 + 5) / 2 : le lot compte pour un composant, « sono » non évalué
        Assert.Equal((3, 2), (salle.Actifs, salle.Evalues));
        Assert.Equal(7.7m, Echelle.Arrondie(etat.Note!.Value)); // (6,4 + 9) / 2 : Parties communes comptées comme une zone
        Assert.Null(etat.Zones.Single(z => z.ZoneId == Parking).Note); // zone vide : ni note ni effet sur le site
        Assert.Equal((4, 3), (etat.Actifs, etat.Evalues));
        Assert.Equal([chaises.Id], etat.Critiques);
        Assert.Equal(Rationnel.Entier(0), etat.PartAncienne);
    }

    [Fact]
    public void AverageOfAveragesStaysExactSoHalfTenthsRoundUp()
    {
        // Salle : un lot à 19/3 ; Parties communes : lots à 25/3 et 8 → 49/6 ; site exact (19/3 + 49/6) / 2 = 7,25.
        var (a, b, c) = (Composant(Salle), Composant(Communes), Composant(Communes));
        var notes = new[]
        {
            Lot(a, Maintenant.AddDays(-1), new Repartition(1, 1, 0, 1, 0)),
            Lot(b, Maintenant.AddDays(-1), new Repartition(2, 1, 0, 0, 0)),
            Lot(c, Maintenant.AddDays(-1), new Repartition(1, 1, 0, 0, 0)),
        };

        var etat = new CalculScore(Zones, [a, b, c], notes).Calculer(Maintenant);

        Assert.Equal(Rationnel.Fraction(29, 4), etat.Note);
        Assert.Equal(7.3m, Echelle.Arrondie(etat.Note!.Value));
    }

    [Fact]
    public void ALabelBoundaryIsNeverLostToRounding()
    {
        // Site exact 3,95 : affiché 4,0 « Dégradé », jamais 3,9 « Mauvais ».
        var (a, b, c, d, e) = (Composant(Salle), Composant(Salle), Composant(Communes), Composant(Communes), Composant(Communes));
        var notes = new[]
        {
            Lot(a, Maintenant.AddDays(-1), new Repartition(0, 1, 1, 2, 1)),
            Lot(b, Maintenant.AddDays(-1), new Repartition(1, 2, 1, 1, 1)),
            Lot(c, Maintenant.AddDays(-1), new Repartition(0, 1, 1, 0, 0)),
            Note(d, Maintenant.AddDays(-1), EtatComposant.Mauvais),
            Note(e, Maintenant.AddDays(-1), EtatComposant.Critique),
        };

        var etat = new CalculScore(Zones, [a, b, c, d, e], notes).Calculer(Maintenant);

        Assert.Equal(Rationnel.Fraction(79, 20), etat.Note);
        Assert.Equal(EtatComposant.Degrade, Echelle.Libelle(etat.Note!.Value));
    }

    [Fact]
    public void OnlyTheLatestEvaluationAtTheDateCountsAndRetiredComponentsLeaveTheScore()
    {
        var tableau = Composant(Salle);
        var chaises = Composant(Salle) with { RetireLe = Maintenant.AddDays(-5) };
        var notes = new[]
        {
            Note(tableau, Maintenant.AddDays(-20), EtatComposant.Bon),
            Note(tableau, Maintenant.AddDays(-2), EtatComposant.Mauvais),
            Note(chaises, Maintenant.AddDays(-20), EtatComposant.Critique),
        };
        var calcul = new CalculScore(Zones, [tableau, chaises], notes);

        var aujourdhui = calcul.Calculer(Maintenant);
        var avant = calcul.Calculer(Maintenant.AddDays(-10));

        Assert.Equal(Rationnel.Entier(3), aujourdhui.Note);
        Assert.Empty(aujourdhui.Critiques);
        Assert.Equal(Rationnel.Entier(5), avant.Note); // (9 + 1) / 2 : chaises encore en service, dernière note du tableau 9
        Assert.Equal([chaises.Id], avant.Critiques);
    }

    [Fact]
    public void AnOldEvaluationStillCountsAndItsWeightInTheSiteScoreIsReported()
    {
        var (ancien, recent, extincteur) = (Composant(Salle), Composant(Salle), Composant(Communes));
        var notes = new[]
        {
            Note(ancien, Maintenant.AddMonths(-7), EtatComposant.Degrade),
            Note(recent, Maintenant.AddMonths(-1), EtatComposant.Bon),
            Note(extincteur, Maintenant.AddMonths(-6).AddDays(1), EtatComposant.Satisfaisant), // moins de 6 mois : récente
        };

        var etat = new CalculScore(Zones, [ancien, recent, extincteur], notes).Calculer(Maintenant);
        var salle = etat.Zones.Single(z => z.ZoneId == Salle);

        Assert.Equal(Rationnel.Entier(7), salle.Note);
        Assert.Equal(1, salle.Anciennes);
        Assert.Equal(Maintenant.AddMonths(-7), salle.PlusAncienne);
        Assert.Equal(Rationnel.Fraction(1, 4), etat.PartAncienne); // la moitié de la salle, qui pèse la moitié du site
    }

    [Fact]
    public void TheHistoryGivesTwelveMonthEndsInParisTimeForTheSiteAndEachZone()
    {
        var tableau = Composant(Salle) with { CreeLe = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var notes = new[]
        {
            Note(tableau, new DateTimeOffset(2026, 3, 31, 21, 30, 0, TimeSpan.Zero), EtatComposant.Degrade), // 31/03 23 h 30 à Paris : mars
            Note(tableau, new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero), EtatComposant.Bon), // 1er/05 0 h 30 à Paris : mai
            Note(tableau, Maintenant.AddHours(-1), EtatComposant.Mauvais),
        };

        var historique = new CalculScore(Zones, [tableau], notes).Historique(Maintenant);
        Rationnel? Au(int annee, int mois) => historique.Single(p => p.Mois == new DateOnly(annee, mois, 1)).Etat.Note;

        Assert.Equal(12, historique.Count);
        Assert.Equal(new DateOnly(2025, 11, 1), historique[0].Mois);
        Assert.Equal(new DateOnly(2026, 10, 1), historique[^1].Mois);
        Assert.Null(Au(2026, 2));
        Assert.Equal(Rationnel.Entier(5), Au(2026, 3));
        Assert.Equal(Rationnel.Entier(5), Au(2026, 4));
        Assert.Equal(Rationnel.Entier(9), Au(2026, 5));
        Assert.Equal(Rationnel.Entier(3), historique[^1].Etat.Note); // mois en cours arrêté à maintenant
        Assert.Equal(Rationnel.Entier(9), historique.Single(p => p.Mois == new DateOnly(2026, 5, 1)).Etat.Zones.Single(z => z.ZoneId == Salle).Note);
        Assert.Equal(new DateTimeOffset(2025, 10, 31, 23, 0, 0, TimeSpan.Zero), CalculScore.DebutHistorique(Maintenant)); // 1er/11 0 h à Paris
    }

    private static ComposantSuivi Composant(Guid zone) => new(Guid.NewGuid(), zone, Maintenant.AddYears(-2), null);

    private static NoteDatee Note(ComposantSuivi composant, DateTimeOffset le, EtatComposant etat) =>
        new(Guid.CreateVersion7(le), composant.Id, le, Echelle.Note(etat, null), Echelle.Critique(etat, null));

    private static NoteDatee Lot(ComposantSuivi composant, DateTimeOffset le, Repartition repartition) =>
        new(Guid.CreateVersion7(le), composant.Id, le, Echelle.Note(null, repartition), Echelle.Critique(null, repartition));
}

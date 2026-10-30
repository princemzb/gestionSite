using GestionSite.Modules.Maintenance.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

/// <summary>Échéances calculées des plans préventifs (US-12, S12-02, T-06) : glissant, fixe, fins de mois, fenêtre, retard.</summary>
public sealed class EcheancierTests
{
    private static readonly Periode Annuelle = new(1, UnitePeriode.Ans);
    private static readonly Periode Mensuelle = new(1, UnitePeriode.Mois);
    private static readonly DateOnly Aujourdhui = new(2026, 10, 30);

    [Fact]
    public void ASlidingPlanRestartsFromTheRealDateEvenWhenDoneEarlyInTheWindow()
    {
        var calendrier = new CalendrierPlan(Annuelle, ModeEcheance.Glissant, new DateOnly(2026, 12, 1));

        var premiere = Echeancier.Prochaine(calendrier, []);
        var suivante = Echeancier.Prochaine(calendrier, [Realisee(new DateOnly(2026, 12, 1), new DateOnly(2026, 11, 10))]);
        var apresSaut = Echeancier.Prochaine(calendrier, [new OccurrenceConnue(new DateOnly(2026, 12, 1), StatutOccurrence.Sautee, new DateOnly(2026, 11, 20))]);

        Assert.Equal(new DateOnly(2026, 12, 1), premiere);
        Assert.Equal(new DateOnly(2027, 11, 10), suivante); // jamais plus de 12 mois entre deux vérifications
        Assert.Equal(new DateOnly(2027, 11, 20), apresSaut); // repart de la date du saut
    }

    [Fact]
    public void AFixedPlanKeepsItsDateEveryYearEvenAfterADelay()
    {
        var calendrier = new CalendrierPlan(Annuelle, ModeEcheance.Fixe, new DateOnly(2026, 10, 31));

        var apresRetard = Echeancier.Prochaine(calendrier, [Realisee(new DateOnly(2026, 10, 31), new DateOnly(2026, 12, 15))]);
        var plusieurs = Echeancier.Prochaine(calendrier, [
            Realisee(new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 20)),
            Realisee(new DateOnly(2027, 10, 31), new DateOnly(2027, 11, 3))]);

        Assert.Equal(new DateOnly(2027, 10, 31), apresRetard);
        Assert.Equal(new DateOnly(2028, 10, 31), plusieurs);
    }

    [Fact]
    public void MonthEndsAndLeapDaysFallOnTheLastDayOfTheMonth()
    {
        var mensuelle = new CalendrierPlan(Mensuelle, ModeEcheance.Fixe, new DateOnly(2026, 1, 31));
        var bissextile = new CalendrierPlan(Annuelle, ModeEcheance.Glissant, new DateOnly(2028, 2, 29));

        Assert.Equal(new DateOnly(2026, 2, 28), Echeancier.Prochaine(mensuelle, [Realisee(new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31))]));
        Assert.Equal(new DateOnly(2026, 3, 31), Echeancier.Prochaine(mensuelle, [Realisee(new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 27))]));
        Assert.Equal(new DateOnly(2029, 2, 28), Echeancier.Prochaine(bissextile, [Realisee(new DateOnly(2028, 2, 29), new DateOnly(2028, 2, 29))]));
    }

    [Fact]
    public void AnAssignedOccurrenceStaysTheCurrentOneWhateverTheCalendar()
    {
        var avant = new CalendrierPlan(Annuelle, ModeEcheance.Glissant, new DateOnly(2026, 11, 15));
        var apres = new CalendrierPlan(new Periode(6, UnitePeriode.Mois), ModeEcheance.Fixe, new DateOnly(2027, 1, 1));
        OccurrenceConnue[] occurrences = [Realisee(new DateOnly(2025, 11, 15), new DateOnly(2025, 11, 15)), new(new DateOnly(2026, 11, 15), StatutOccurrence.Affectee, null)];

        Assert.Equal(new DateOnly(2026, 11, 15), Echeancier.Prochaine(avant, occurrences));
        Assert.Equal(new DateOnly(2026, 11, 15), Echeancier.Prochaine(apres, occurrences)); // matérialisée : inchangée
        Assert.Equal(new DateOnly(2027, 1, 1), Echeancier.Prochaine(apres, [occurrences[0]])); // non matérialisée : recalculée
    }

    [Fact]
    public void AOneOffPlanEndsOnceResolvedAndAVeryEarlyRealisationNeverGoesBackwards()
    {
        var ponctuel = new CalendrierPlan(null, ModeEcheance.Glissant, new DateOnly(2027, 3, 1));
        var hebdomadaire = new CalendrierPlan(new Periode(1, UnitePeriode.Semaines), ModeEcheance.Glissant, new DateOnly(2026, 10, 1));

        Assert.Equal(new DateOnly(2027, 3, 1), Echeancier.Prochaine(ponctuel, []));
        Assert.Null(Echeancier.Prochaine(ponctuel, [Realisee(new DateOnly(2027, 3, 1), new DateOnly(2027, 2, 20))]));
        Assert.Equal(new DateOnly(2026, 11, 2), Echeancier.Prochaine(hebdomadaire, [Realisee(new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1))]));
    }

    [Theory]
    [InlineData(1, "Ans", 2026, 12, 31, 30)] // 365 jours → 36,5 → borné à 30
    [InlineData(6, "Semaines", 2026, 12, 31, 7)] // 42 jours → 4,2 → au moins 7
    [InlineData(3, "Mois", 2026, 12, 31, 9)] // 92 jours → 9,2
    [InlineData(6, "Mois", 2026, 12, 31, 18)] // 184 jours → 18,4
    [InlineData(5, "Mois", 2026, 12, 31, 15)] // 153 jours → 15,3
    public void TheToDoWindowIsTenPercentOfThePeriodBetweenSevenAndThirtyDays(int valeur, string unite, int a, int m, int j, int jours)
    {
        Assert.Equal(jours, Echeancier.Fenetre(new Periode(valeur, Enum.Parse<UnitePeriode>(unite)), new DateOnly(a, m, j)));
    }

    [Fact]
    public void ThereIsNoToleranceAfterTheDueDate()
    {
        var echeance = new DateOnly(2026, 11, 29);

        Assert.Equal(30, Echeancier.Fenetre(null, echeance)); // ponctuel
        Assert.Equal(EtatEcheance.AVenir, Echeancier.Etat(echeance, 30, new DateOnly(2026, 10, 29)));
        Assert.Equal(EtatEcheance.AFaire, Echeancier.Etat(echeance, 30, Aujourdhui));
        Assert.Equal(EtatEcheance.AFaire, Echeancier.Etat(echeance, 30, echeance)); // le jour même
        Assert.Equal(EtatEcheance.EnRetard, Echeancier.Etat(echeance, 30, echeance.AddDays(1)));
    }

    [Fact]
    public void ACalendarIsValidatedAroundToday()
    {
        var fixeSansPeriode = Assert.Throws<SaisieInvalideException>(() => CalendrierPlan.Valider(null, ModeEcheance.Fixe, Aujourdhui, Aujourdhui));
        var sansDate = Assert.Throws<SaisieInvalideException>(() => CalendrierPlan.Valider(Annuelle, null, null, Aujourdhui));
        var tropLoin = Assert.Throws<SaisieInvalideException>(() => CalendrierPlan.Valider(Annuelle, null, Aujourdhui.AddYears(11), Aujourdhui));
        var modeInconnu = Assert.Throws<SaisieInvalideException>(() => CalendrierPlan.Valider(Annuelle, (ModeEcheance)7, Aujourdhui, Aujourdhui));

        Assert.Equal(["mode", "premiereEcheance", "premiereEcheance", "mode"], new[] { fixeSansPeriode.Champ, sansDate.Champ, tropLoin.Champ, modeInconnu.Champ });
        Assert.Equal(ModeEcheance.Glissant, CalendrierPlan.Valider(Annuelle, null, Aujourdhui, Aujourdhui).Mode);
        Assert.Equal(ModeEcheance.Glissant, CalendrierPlan.Valider(null, ModeEcheance.Glissant, Aujourdhui.AddYears(-5), Aujourdhui).Mode);
        var ancienne = new DateOnly(2018, 1, 31); // plan ancien modifié sans toucher à sa première échéance
        Assert.Equal(ancienne, CalendrierPlan.Valider(Annuelle, null, ancienne, Aujourdhui, actuelle: ancienne).PremiereEcheance);
    }

    [Fact]
    public void APlanFollowsItsTargetAndIntervenerRules()
    {
        var cibleBatiment = new CibleDuPlan(CiblePlan.Batiment, Guid.NewGuid(), Guid.NewGuid(), null, null);
        var cibleZone = new CibleDuPlan(CiblePlan.ZoneCorpsEtat, Guid.NewGuid(), Guid.NewGuid(), null, "CourantsForts");

        var inspectionBatiment = Assert.Throws<SaisieInvalideException>(() => Plan(cibleBatiment, NaturePlan.InspectionInterne));
        var sansLibelle = Assert.Throws<SaisieInvalideException>(() => Plan(cibleZone, NaturePlan.InspectionInterne, libelle: " "));
        var deux = Assert.Throws<SaisieInvalideException>(() => IntervenantPlan.Valider(Guid.NewGuid(), "Entreprise", Guid.NewGuid()));
        var sansReferent = Assert.Throws<SaisieInvalideException>(() => IntervenantPlan.Valider(null, "Entreprise", null));
        var sansCommande = Assert.Throws<SaisieInvalideException>(() => Plan(cibleZone, NaturePlan.Entretien, commande: Guid.Empty));
        var plan = Plan(cibleZone, NaturePlan.InspectionInterne);

        plan.Suspendre("Installation déposée pendant les travaux");
        var deuxFois = Assert.Throws<PlanIncompatibleException>(() => plan.Suspendre("Encore"));
        plan.Reprendre();

        Assert.Equal(["nature", "libelle", "intervenantCompteId", "referentCompteId", "commandeId"],
            new[] { inspectionBatiment.Champ, sansLibelle.Champ, deux.Champ, sansReferent.Champ, sansCommande.Champ });
        Assert.NotNull(deuxFois);
        Assert.False(plan.Suspendu);
        Assert.Throws<PlanIncompatibleException>(plan.Reprendre);
        Assert.Equal("2026-10-30", plan.Releve()["premiereEcheance"]);
    }

    [Fact]
    public void ADismissedProposalKeepsItsReasonAndOtherNeedsAComment()
    {
        var sansCommentaire = Assert.Throws<SaisieInvalideException>(() =>
            new PropositionEcartee(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cle", MotifEcart.Autre, null, Guid.NewGuid(), DateTimeOffset.UtcNow));
        var inconnu = Assert.Throws<SaisieInvalideException>(() =>
            new PropositionEcartee(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cle", (MotifEcart)9, null, Guid.NewGuid(), DateTimeOffset.UtcNow));
        var ecartee = new PropositionEcartee(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cle", MotifEcart.SuiviAilleurs, " Contrat ", Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(("commentaire", "motif"), (sansCommentaire.Champ, inconnu.Champ));
        Assert.Equal("Contrat", ecartee.Commentaire);
    }

    private static OccurrenceConnue Realisee(DateOnly echeance, DateOnly le) => new(echeance, StatutOccurrence.Realisee, le);

    private static PlanPreventif Plan(CibleDuPlan cible, NaturePlan nature, string libelle = "Vérification", Guid? commande = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), commande ?? Guid.NewGuid(), null, cible, nature, null,
        new DefinitionPlan(libelle, BasePlan.BonnePratique, null, IntervenantPlan.Aucun, null, new CalendrierPlan(Annuelle, ModeEcheance.Glissant, Aujourdhui)),
        Guid.NewGuid(), DateTimeOffset.UtcNow);
}

using GestionSite.Modules.Maintenance.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

/// <summary>Exécution d'une occurrence (US-12, S12-03) : occurrence, contrôle réalisé, commandes dérivées.</summary>
public sealed class ControlePreventifTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Aujourdhui = new(2026, 10, 30);
    private static readonly GrilleControle Grille = new(2, ["Capot fermé", "Pas d'échauffement", "Étiquetage"], [("Température", "°C")]);

    [Fact]
    public void AnInspectionFreezesTheGridAndCountsItsDefects()
    {
        var (plan, occurrence) = Occurrence(NaturePlan.InspectionInterne);

        var controle = Creer(plan, occurrence, Saisie(points: [(true, false), (false, true)], mesures: [48.500m]));

        Assert.Equal(2, controle.GrilleVersion);
        Assert.Equal(["Capot fermé", "Pas d'échauffement", "Étiquetage"], controle.PointsLus.Select(p => p.Texte));
        Assert.Equal([true, false, null], controle.PointsLus.Select(p => p.Conforme));
        Assert.True(controle.PointsLus[1].Danger);
        Assert.Equal(1, controle.Defauts);
        Assert.Equal(48.5m, controle.MesuresLues.Single().Valeur);
        Assert.Equal(plan.ZoneId, controle.ZoneId);
        Assert.NotNull(controle.IntervenantCompteId); // compte interne qui saisit
    }

    [Theory]
    [InlineData(1, "grille")]
    [InlineData(null, "grilleVersion")]
    public void AnInspectionUsesTheGridInForce(int? version, string attendu)
    {
        var (plan, occurrence) = Occurrence(NaturePlan.InspectionInterne);

        var refus = Record.Exception(() => Creer(plan, occurrence, Saisie(grilleVersion: version)));

        Assert.Equal(attendu, refus switch
        {
            GrilleControleModifieeException => "grille",
            SaisieInvalideException saisie => saisie.Champ,
            _ => null,
        });
    }

    [Fact]
    public void InconsistentEntriesAreRefused()
    {
        var (inspection, occurrence) = Occurrence(NaturePlan.InspectionInterne);
        var (entretien, autre) = Occurrence(NaturePlan.Entretien);

        Assert.Equal("points", Champ(() => Creer(inspection, occurrence, Saisie(points: [(true, false), (true, false), (true, false), (true, false)]))));
        Assert.Equal("points", Champ(() => Creer(inspection, occurrence, Saisie(points: [(true, true)])))); // danger sur un point conforme
        Assert.Equal("resultat", Champ(() => Creer(inspection, occurrence, Saisie(points: [(false, false)], resultat: ResultatControle.Conforme))));
        Assert.Equal("mesures", Champ(() => Creer(inspection, occurrence, Saisie(mesures: [1.2345m]))));
        Assert.Equal("mesures", Champ(() => Creer(inspection, occurrence, Saisie(mesures: [1m, 2m]))));
        Assert.Equal("releves", Champ(() => Creer(inspection, occurrence, Saisie(releves: [("Pression", "bar", 1.5m)]))));
        Assert.Equal("realiseLe", Champ(() => Creer(inspection, occurrence, Saisie(realiseLe: Aujourdhui.AddDays(1)))));
        Assert.Equal("realiseLe", Champ(() => Creer(inspection, occurrence, Saisie(realiseLe: Aujourdhui.AddYears(-6)))));
        Assert.Equal("resultat", Champ(() => Creer(inspection, occurrence, Saisie(resultat: (ResultatControle)9))));
        Assert.Equal("numeroAgrement", Champ(() => Creer(inspection, occurrence, Saisie(agrement: "AG-12"))));
        Assert.Equal("compteRendu", Champ(() => Creer(entretien, autre, Saisie(grilleVersion: null, compteRendu: " "))));
        Assert.Equal("points", Champ(() => Creer(entretien, autre, Saisie(grilleVersion: null, compteRendu: "Fait", points: [(true, false)]))));
        Assert.Equal("releves", Champ(() => Creer(entretien, autre, Saisie(grilleVersion: null, compteRendu: "Fait", releves: [(" ", "bar", 1m)]))));
    }

    [Fact]
    public void AMaintenanceKeepsItsFreeReadingsAndTheContractor()
    {
        var (plan, occurrence) = Occurrence(NaturePlan.Entretien);

        var controle = Creer(plan, occurrence, Saisie(
            grilleVersion: null, compteRendu: "Ramonage et nettoyage du brûleur", releves: [("Pression", "bar", 1.500m)], prestataire: "Chauffage Durand",
            agrement: "QB-4521"));

        Assert.Equal("Pression", controle.MesuresLues.Single().Libelle);
        Assert.Equal(1.5m, controle.MesuresLues.Single().Valeur);
        Assert.Null(controle.IntervenantCompteId);
        Assert.Equal(("Chauffage Durand", "QB-4521"), (controle.Prestataire, controle.NumeroAgrement));
        Assert.Equal(0, controle.Defauts);
        Assert.True(controle.MemeEnvoi(controle.AuteurCompteId, plan.Id, occurrence.Echeance,
            Saisie(grilleVersion: null, commande: controle.CommandeId, compteRendu: "autre texte")));
        Assert.False(controle.MemeEnvoi(controle.AuteurCompteId, plan.Id, occurrence.Echeance, Saisie(resultat: ResultatControle.NonConforme)));
    }

    [Fact]
    public void AnOccurrenceIsAssignedUntilItIsResolved()
    {
        var (plan, occurrence) = Occurrence(NaturePlan.Entretien);
        var technicien = Guid.NewGuid();

        var aucun = Champ(() => occurrence.Affecter(IntervenantPlan.Aucun, Instant));
        var premiere = occurrence.Affecter(new IntervenantPlan(technicien, null, null), Instant);
        var memeIntervenant = occurrence.Affecter(new IntervenantPlan(technicien, null, null), Instant);
        var sansCommentaire = Champ(() => occurrence.Sauter(MotifSaut.Autre, null, Aujourdhui));
        var motifInconnu = Champ(() => occurrence.Sauter((MotifSaut)8, null, Aujourdhui));
        occurrence.Sauter(MotifSaut.Inaccessible, null, Aujourdhui);

        Assert.Equal("intervenantCompteId", aucun);
        Assert.True(premiere);
        Assert.False(memeIntervenant);
        Assert.True(occurrence.EnCharge(technicien));
        Assert.False(occurrence.EnCharge(null));
        Assert.Equal(("commentaire", "motif"), (sansCommentaire, motifInconnu));
        Assert.Equal(new OccurrenceConnue(occurrence.Echeance, StatutOccurrence.Sautee, Aujourdhui), occurrence.Connue);
        Assert.Throws<PlanIncompatibleException>(() => occurrence.Affecter(new IntervenantPlan(Guid.NewGuid(), null, null), Instant));
        Assert.Throws<PlanIncompatibleException>(() => occurrence.Realiser(Creer(plan, occurrence, Saisie(grilleVersion: null, compteRendu: "Fait")), reserves: false));
    }

    [Fact]
    public void ARealisationWithDefectsHasReservations()
    {
        var (plan, occurrence) = Occurrence(NaturePlan.InspectionInterne);
        var controle = Creer(plan, occurrence, Saisie(points: [(false, false)], resultat: ResultatControle.NonConforme, realiseLe: Aujourdhui.AddDays(-3)));

        occurrence.Realiser(controle, reserves: controle.Defauts > 0);
        controle.Evaluer(Guid.NewGuid(), 3m, "Mauvais");

        Assert.Equal(StatutOccurrence.RealiseeAvecReserves, occurrence.Statut);
        Assert.Equal((Aujourdhui.AddDays(-3), controle.Id), (occurrence.RealiseeLe!.Value, occurrence.ControleId!.Value));
        Assert.Equal((3m, "Mauvais"), (controle.Note!.Value, controle.LibelleNote));
    }

    [Fact]
    public void TheAssignedIntervenerAndTheDeclaredResultAreKept()
    {
        var (plan, occurrence) = Occurrence(NaturePlan.Entretien);
        var technicien = Guid.NewGuid();

        var parPrestataire = Creer(plan, occurrence, Saisie(grilleVersion: null, compteRendu: "Fait", resultat: ResultatControle.NonConforme),
            new IntervenantPlan(null, "Socotec", Guid.NewGuid()));
        var parTechnicien = Creer(plan, occurrence, Saisie(grilleVersion: null, compteRendu: "Fait", resultat: ResultatControle.Conforme),
            new IntervenantPlan(technicien, null, null));

        Assert.Equal(("Socotec", (Guid?)null), (parPrestataire.Prestataire, parPrestataire.IntervenantCompteId));
        Assert.True(parPrestataire.Reserves); // « non conforme » déclaré, sans point
        Assert.Equal(technicien, parTechnicien.IntervenantCompteId);
        Assert.False(parTechnicien.Reserves);
        Assert.Equal("releves", Champ(() => Creer(plan, occurrence, Saisie(grilleVersion: null, compteRendu: "Fait", releves: [("Pression", "bar", 2_000_000m)]))));
        Assert.Equal("grilleVersion", Champ(() => Creer(plan, occurrence, Saisie(grilleVersion: 1, compteRendu: "Fait"))));
    }

    [Fact]
    public void AReportIsRecognisedByItsSignatureAndRequiredForRegulatoryContractorChecks()
    {
        var pdf = "%PDF-1.4\n%%EOF"u8.ToArray();

        Assert.Equal(64, RapportControle.Valider(pdf).Length);
        Assert.Equal("rapport", Champ(() => RapportControle.Valider([])));
        Assert.Equal("rapport", Champ(() => RapportControle.Valider("PK\u0003\u0004 pas un PDF"u8.ToArray())));
        Assert.Equal("rapport", Champ(() => RapportControle.Valider(new byte[(10 * 1024 * 1024) + 1])));
        Assert.True(RapportControle.Exige(NaturePlan.VerificationExterieure, BasePlan.Reglementaire, "Socotec"));
        Assert.False(RapportControle.Exige(NaturePlan.VerificationExterieure, BasePlan.Reglementaire, prestataire: null));
        Assert.False(RapportControle.Exige(NaturePlan.VerificationExterieure, BasePlan.BonnePratique, "Socotec"));
        Assert.False(RapportControle.Exige(NaturePlan.Entretien, BasePlan.Reglementaire, "Socotec"));
        Assert.StartsWith("organisations/", RapportControle.Cle(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), StringComparison.Ordinal);
    }

    [Fact]
    public void ObservationsBelongToExternalVerificationsAndEachIsAReservation()
    {
        var (verification, occurrence) = Occurrence(NaturePlan.VerificationExterieure);
        var (entretien, autre) = Occurrence(NaturePlan.Entretien);
        var zone = Guid.NewGuid();

        var controle = Creer(verification, occurrence, Saisie(grilleVersion: null, observations: [("Prise arrachée", zone, true), ("Repérage absent", null, false)]));

        Assert.Equal(2, controle.Defauts);
        Assert.Equal([zone, verification.ZoneId], controle.ObservationsLues.Select(o => o.ZoneId));
        Assert.True(controle.ObservationsLues[0].Danger);
        Assert.Equal("observations", Champ(() => Creer(entretien, autre, Saisie(grilleVersion: null, compteRendu: "Fait", observations: [("Tuile", null, false)]))));
        Assert.Equal("observations", Champ(() => Creer(verification, occurrence, Saisie(grilleVersion: null, observations: [(" ", null, false)]))));
        Assert.Equal("observations", Champ(() => Creer(verification, occurrence, Saisie(grilleVersion: null,
            observations: [.. Enumerable.Range(0, 51).Select(i => ((string?)$"Écart {i}", (Guid?)null, false))]))));
        Assert.Equal("resultat", Champ(() => Creer(verification, occurrence, Saisie(grilleVersion: null, resultat: ResultatControle.Conforme,
            observations: [("Écart", null, false)]))));
        controle.Joindre(Guid.NewGuid(), 120, new string('a', 64));
        Assert.Equal((120, 64), (controle.RapportTaille!.Value, controle.RapportEmpreinte!.Length));
    }

    [Fact]
    public void DerivedCommandsAreStableAndDistinctPerUse()
    {
        var commande = Guid.NewGuid();

        var evaluation = Commandes.Derivee(commande, "evaluation");

        Assert.Equal(evaluation, Commandes.Derivee(commande, "evaluation"));
        Assert.NotEqual(evaluation, Commandes.Derivee(commande, "dossier:1"));
        Assert.NotEqual(evaluation, Commandes.Derivee(Guid.NewGuid(), "evaluation"));
        Assert.Equal(8, evaluation.Version);
        Assert.Throws<ArgumentException>(() => Commandes.Derivee(commande, ""));
    }

    private static string? Champ(Action action) => Assert.Throws<SaisieInvalideException>(action).Champ;

    private static (PlanPreventif Plan, OccurrencePreventive Occurrence) Occurrence(NaturePlan nature)
    {
        var cible = new CibleDuPlan(CiblePlan.Composant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "CourantsForts");
        var plan = new PlanPreventif(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, cible, nature, null,
            new DefinitionPlan("Contrôle", BasePlan.BonnePratique, null, IntervenantPlan.Aucun, null,
                new CalendrierPlan(new Periode(1, UnitePeriode.Ans), ModeEcheance.Glissant, Aujourdhui)),
            Guid.NewGuid(), Instant);
        return (plan, new OccurrencePreventive(Guid.NewGuid(), plan, Aujourdhui, Instant));
    }

    private static ControlePreventif Creer(PlanPreventif plan, OccurrencePreventive occurrence, SaisieControle saisie, IntervenantPlan? affecte = null) =>
        ControlePreventif.Creer(
            Guid.NewGuid(), plan, new CibleDuPlan(plan.Cible, plan.BatimentId, plan.ZoneId, plan.ComposantId, plan.CorpsEtat), occurrence,
            plan.Nature == NaturePlan.InspectionInterne ? Grille : null, saisie, Guid.NewGuid(), Instant, affecte);

    private static SaisieControle Saisie(
        int? grilleVersion = 2, (bool? Conforme, bool Danger)[]? points = null, decimal?[]? mesures = null, (string?, string?, decimal?)[]? releves = null,
        ResultatControle? resultat = ResultatControle.AvecObservations, DateOnly? realiseLe = null, string? compteRendu = null, string? prestataire = null,
        string? agrement = null, Guid? commande = null, (string?, Guid?, bool)[]? observations = null) => new(
        commande ?? Guid.NewGuid(), realiseLe ?? Aujourdhui, resultat, compteRendu, grilleVersion, points, mesures, releves, prestataire, agrement, observations);
}

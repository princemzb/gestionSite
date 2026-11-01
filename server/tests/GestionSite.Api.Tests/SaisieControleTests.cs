using System.Text.Json;
using GestionSite.Web.Preventif;
using GestionSite.Web.Session;

namespace GestionSite.Api.Tests;

/// <summary>Saisie d'un contrôle préventif (US-12, S12-07) : étapes selon la nature, contrôles d'étape, corps envoyé à l'API.</summary>
public sealed class SaisieControleTests
{
    private static readonly Guid Zone = Guid.NewGuid();
    private static readonly DateOnly Echeance = new(2027, 10, 31);

    [Theory]
    [InlineData("InspectionInterne", true, "points,etat,resultat")]
    [InlineData("InspectionInterne", false, "points,resultat")]
    [InlineData("VerificationExterieure", false, "observations,resultat")]
    [InlineData("Entretien", false, "releves,resultat")]
    public void TheStepsFollowTheNatureOfThePlan(string nature, bool composant, string etapes) =>
        Assert.Equal(etapes, string.Join(',', SaisieControle.Etapes(nature, composant).Select(e => e.Cle)));

    [Fact]
    public void ADefectOrAnObservationExcludesTheResultWithoutObservation()
    {
        var sansEcart = new SaisieControle { Constats = ["oui", null, ""] };
        var defaut = new SaisieControle { Constats = ["oui", "danger"] };
        var observation = new SaisieControle { Observations = [new() { Libelle = "  " }, new() { Libelle = "Bloc autonome hors service" }] };

        Assert.False(sansEcart.Ecart);
        Assert.Contains(sansEcart.Resultats(), r => r.Code == "Conforme");
        Assert.True(defaut.Ecart);
        Assert.DoesNotContain(defaut.Resultats(), r => r.Code == "Conforme");
        Assert.True(observation.Ecart);
        Assert.Single(observation.ObservationsRemplies);
        Assert.False(new SaisieControle { Constats = null, Observations = null }.Ecart);
    }

    [Fact]
    public void RowsAreAddedWithinTheLimitsOfTheApi()
    {
        var saisie = new SaisieControle { Releves = null, Observations = null };
        saisie.Ajouter("releve");
        saisie.Ajouter("observation");
        Assert.Equal(2, saisie.Releves!.Count);
        Assert.Equal(2, saisie.Observations!.Count);

        for (var i = 0; i < 20; i++)
        {
            saisie.Ajouter("releve");
        }

        for (var i = 0; i < 60; i++)
        {
            saisie.Ajouter("observation");
        }

        Assert.Equal(SaisieControle.RelevesMax, saisie.Releves.Count);
        Assert.Equal(SaisieControle.ObservationsMax, saisie.Observations.Count);
        Assert.Single(SaisieControle.AuMoinsUne<ReleveSaisi>(null));
    }

    [Fact]
    public void EachStepIsCheckedBeforeTheNextOne()
    {
        var grille = new GrilleApi("CourantsForts", 1, false, ["Pas d'échauffement"], [new MesureGrilleApi("Température", "°C")]);
        var lot = new LotApi("Unite", 40, null);

        Assert.NotNull(new SaisieControle { Mesures = ["chaud"] }.ErreurEtape("points", grille, null));
        Assert.Null(new SaisieControle { Mesures = ["48,5"] }.ErreurEtape("points", grille, null));
        Assert.Null(new SaisieControle().ErreurEtape("points", null, null));
        Assert.Equal("Répartition : 30 pour une quantité de 40.", new SaisieControle { Bon = 30 }.ErreurEtape("etat", grille, lot));
        Assert.Equal("Des quantités positives ou nulles.", new SaisieControle { Bon = 41, Critique = -1 }.ErreurEtape("etat", grille, lot));
        Assert.Equal("Choisissez l'état constaté.", new SaisieControle().ErreurEtape("etat", grille, null));
        Assert.Null(new SaisieControle { Etat = "Bon" }.ErreurEtape("etat", grille, null));
        Assert.NotNull(new SaisieControle { Releves = [new() { Libelle = "Pression", Valeur = "haute" }] }.ErreurEtape("releves", null, null));
        Assert.NotNull(new SaisieControle { Releves = [new() { Valeur = "12" }] }.ErreurEtape("observations", null, null));
        Assert.NotNull(new SaisieControle { Releves = [new() { Libelle = "Pression" }] }.ErreurEtape("releves", null, null));
        Assert.NotNull(new SaisieControle { Releves = [new() { Libelle = "Pression", Valeur = "1,25" }] }.ErreurEtape("releves", null, null)); // unité exigée par l'API
        Assert.NotNull(new SaisieControle { Releves = [new() { Unite = "bar" }] }.ErreurEtape("releves", null, null));
        Assert.Null(new SaisieControle { Releves = [new() { Libelle = "Pression", Valeur = "1,25", Unite = "bar" }, new()] }.ErreurEtape("releves", null, null));
        Assert.Equal("Observation : décrivez l'écart signalé comme dangereux.",
            new SaisieControle { Observations = [new() { Danger = true }] }.ErreurEtape("observations", null, null));
        Assert.Null(new SaisieControle { Observations = [new() { Libelle = "Bloc hors service", Danger = true }, new()] }.ErreurEtape("observations", null, null));
        Assert.Null(new SaisieControle { Observations = null }.ErreurEtape("observations", null, null));
        Assert.Null(new SaisieControle().ErreurEtape("resultat", null, null));
    }

    [Fact]
    public void TheDueDateShownWhenTheScreenOpenedIsTheOneSent()
    {
        var plan = Plan("Entretien", "BonnePratique", null);
        Assert.Equal("2027-10-31", Json(new SaisieControle().Demande(plan, null, null)).GetProperty("echeance").GetString());
        Assert.Equal("2026-10-31", Json(new SaisieControle { Echeance = new DateOnly(2026, 10, 31) }.Demande(plan, null, null)).GetProperty("echeance").GetString());
    }

    [Fact]
    public void TheReportIsRequiredForARegulatoryVerificationByAContractor()
    {
        var verification = Plan("VerificationExterieure", "Reglementaire", prestataireAffecte: null);
        var affectee = Plan("VerificationExterieure", "Reglementaire", prestataireAffecte: "Bureau Alpha");

        Assert.False(new SaisieControle().RapportExige(verification)); // vérification faite par un compte interne
        Assert.True(new SaisieControle { Prestataire = "Bureau Alpha" }.RapportExige(verification));
        Assert.True(new SaisieControle().RapportExige(affectee));
        Assert.False(new SaisieControle { Prestataire = "Bureau Alpha" }.RapportExige(Plan("VerificationExterieure", "BonnePratique", null)));
        Assert.False(new SaisieControle { Prestataire = "Bureau Alpha" }.RapportExige(Plan("Entretien", "Reglementaire", null)));
    }

    [Fact]
    public void AnInspectionSendsItsPointsMeasuresAndStateButNoFreeReadings()
    {
        var grille = new GrilleApi("CourantsForts", 2, true, ["Point 1", "Point 2", "Point 3", "Point 4"], [new MesureGrilleApi("Température", "°C")]);
        var commande = Guid.NewGuid();
        var saisie = new SaisieControle
        {
            CommandeId = commande,
            GrilleVersion = 2,
            Constats = ["oui", "non", "danger"],
            Mesures = ["48,5"],
            Etat = "Mauvais",
            RealiseLe = new DateOnly(2027, 10, 30),
            Resultat = "AvecObservations",
            CompteRendu = "Bornier noirci",
            Prestataire = "Ignoré",
            Releves = [new() { Libelle = "Ignoré", Valeur = "1" }],
        };

        var individuel = Json(saisie.Demande(Plan("InspectionInterne", "BonnePratique", null, composant: true), grille, null));
        var lot = Json(new SaisieControle { Bon = 40 }.Demande(Plan("InspectionInterne", "BonnePratique", null, composant: true), grille, new LotApi("Unite", 40, null)));
        var zone = Json(new SaisieControle { Etat = "Bon" }.Demande(Plan("InspectionInterne", "BonnePratique", null, composant: false), grille, null));

        Assert.Equal(commande, individuel.GetProperty("commandeId").GetGuid());
        Assert.Equal("2027-10-31", individuel.GetProperty("echeance").GetString());
        Assert.Equal(2, individuel.GetProperty("grilleVersion").GetInt32());
        Assert.Equal("[{\"conforme\":true,\"danger\":false},{\"conforme\":false,\"danger\":false},{\"conforme\":false,\"danger\":true},{\"conforme\":null,\"danger\":false}]",
            individuel.GetProperty("points").GetRawText());
        Assert.Equal(48.5m, individuel.GetProperty("mesures")[0].GetDecimal());
        Assert.Equal("Mauvais", individuel.GetProperty("etat").GetString());
        Assert.Equal(JsonValueKind.Null, individuel.GetProperty("repartition").ValueKind);
        Assert.Equal(JsonValueKind.Null, individuel.GetProperty("prestataire").ValueKind);
        Assert.Equal(JsonValueKind.Null, individuel.GetProperty("releves").ValueKind);
        Assert.Equal(JsonValueKind.Null, individuel.GetProperty("observations").ValueKind);
        Assert.Equal(40, lot.GetProperty("repartition").GetProperty("bon").GetInt32());
        Assert.Equal(JsonValueKind.Null, lot.GetProperty("etat").ValueKind);
        Assert.Equal(JsonValueKind.Null, zone.GetProperty("etat").ValueKind); // inspection d'une zone : aucune évaluation
    }

    [Fact]
    public void AVerificationSendsItsContractorObservationsAndReadings()
    {
        var saisie = new SaisieControle
        {
            Prestataire = "Bureau Alpha",
            NumeroAgrement = "AG-1",
            Constats = ["non"],
            Resultat = "NonConforme",
            Observations = [new() { Libelle = "Bloc hors service", ZoneId = Zone, Danger = true }, new() { Libelle = "" }],
            Releves = [new() { Libelle = "Pression", Unite = "bar", Valeur = "15" }, new()],
        };

        var verification = Json(saisie.Demande(Plan("VerificationExterieure", "Reglementaire", null), null, null));
        var entretien = Json(saisie.Demande(Plan("Entretien", "BonnePratique", null), null, null));

        Assert.Equal("Bureau Alpha", verification.GetProperty("prestataire").GetString());
        Assert.Equal("AG-1", verification.GetProperty("numeroAgrement").GetString());
        Assert.Equal(JsonValueKind.Null, verification.GetProperty("points").ValueKind);
        Assert.Equal(JsonValueKind.Null, verification.GetProperty("grilleVersion").ValueKind);
        Assert.Equal(1, verification.GetProperty("observations").GetArrayLength());
        Assert.Equal(Zone, verification.GetProperty("observations")[0].GetProperty("zoneId").GetGuid());
        Assert.True(verification.GetProperty("observations")[0].GetProperty("danger").GetBoolean());
        Assert.Equal(1, verification.GetProperty("releves").GetArrayLength());
        Assert.Equal(15m, verification.GetProperty("releves")[0].GetProperty("valeur").GetDecimal());
        Assert.Equal(JsonValueKind.Null, entretien.GetProperty("observations").ValueKind);
        Assert.Equal(1, entretien.GetProperty("releves").GetArrayLength());
    }

    private static JsonElement Json(object demande) => JsonSerializer.SerializeToElement(demande);

    private static PlanApi Plan(string nature, string fondement, string? prestataireAffecte, bool composant = false)
    {
        var intervenant = prestataireAffecte is null ? null : new IntervenantPlanApi(null, null, prestataireAffecte, Guid.NewGuid(), "Rose L.");
        return new PlanApi(
            Guid.NewGuid(), "Plan", nature, fondement, null, null, null, null,
            new CibleApi(composant ? "Composant" : "Batiment", Guid.NewGuid(), Zone, composant ? Guid.NewGuid() : null, "CourantsForts", "Église"),
            null, null, new PeriodeApi(1, "Ans"), "Glissant", Echeance, false, null, false,
            new EcheanceApi(Echeance, Echeance.AddDays(-30), "AVenir", "Affectee", null, intervenant, 1), 3);
    }
}

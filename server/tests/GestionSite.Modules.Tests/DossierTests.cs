using GestionSite.Modules.Maintenance.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

/// <summary>Cycle du dossier (US-09, S09-01, DEC-03) : qualification, doublon, classement sans suite.</summary>
public sealed class DossierTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organisation = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();

    [Fact]
    public void ACaseIsQualifiedWhileOpenOnlyAndKeepsItsFirstQualificationDate()
    {
        var dossier = Public();
        var zone = Guid.NewGuid();

        dossier.Qualifier(PrioriteDossier.Normale, zone, composantId: null, Instant);
        dossier.Qualifier(PrioriteDossier.Haute, zone, composantId: null, Instant.AddHours(2));
        dossier.ClasserSansSuite(MotifClassement.DejaTraite, commentaire: null, aDesDoublons: false);

        Assert.Equal((PrioriteDossier.Haute, Instant), (dossier.Priorite!.Value, dossier.QualifieLe!.Value));
        Assert.Throws<StatutIncompatibleException>(() => dossier.Qualifier(PrioriteDossier.Basse, zone, composantId: null, Instant));
    }

    [Fact]
    public void ADuplicateIsAttachedToAnotherOpenCaseOfTheSameSite()
    {
        var principal = Public();
        var autreSite = new Signalement(Guid.NewGuid(), Organisation, Guid.NewGuid(), Guid.NewGuid(), NatureSignalement.Incident, "Fuite", "Nef", false, false, "Anne", "06", Instant);
        var classe = Public();
        classe.ClasserSansSuite(MotifClassement.SpamAbusif, commentaire: null, aDesDoublons: false);
        var doublon = Public();

        Assert.Throws<SaisieInvalideException>(() => doublon.RattacherA(autreSite, aDesDoublons: false));
        Assert.Throws<SaisieInvalideException>(() => doublon.RattacherA(classe, aDesDoublons: false));
        Assert.Throws<DoublonsRattachesException>(() => doublon.RattacherA(principal, aDesDoublons: true)); // pas de chaîne de doublons
        Assert.Throws<DoublonsRattachesException>(() => Public().ClasserSansSuite(MotifClassement.AucunProbleme, commentaire: null, aDesDoublons: true));
        doublon.RattacherA(principal, aDesDoublons: false);
        Assert.Equal((StatutDossier.RattacheDoublon, principal.Id), (doublon.Statut, doublon.DoublonDe!.Value));
        Assert.Throws<StatutIncompatibleException>(() => doublon.ClasserSansSuite(MotifClassement.AucunProbleme, commentaire: null, aDesDoublons: false));
    }

    [Fact]
    public void AnUnchangedQualificationWritesNothingAndALongZoneLabelIsShortened()
    {
        var dossier = Public();
        var zone = Guid.NewGuid();
        dossier.Qualifier(PrioriteDossier.Normale, zone, composantId: null, Instant);

        var interne = Signalement.Interne(Guid.NewGuid(), Organisation, Site, Guid.NewGuid(), NatureSignalement.Incident, "Panne", emplacement: null,
            new string('B', 120) + " · " + new string('Z', 120), zone, danger: false, activiteBloquee: false, Guid.NewGuid(), "Paul R.", Instant);

        Assert.False(dossier.Qualifier(PrioriteDossier.Normale, zone, composantId: null, Instant.AddHours(1)));
        Assert.Equal(Signalement.EmplacementMax, interne.Emplacement.Length);
        Assert.EndsWith("…", interne.Emplacement, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInternalDeclarationKnowsItsZoneAndItsAuthor()
    {
        var zone = Guid.NewGuid();
        var compte = Guid.NewGuid();

        var dossier = Signalement.Interne(Guid.NewGuid(), Organisation, Site, Guid.NewGuid(), NatureSignalement.Demande, "Prévoir 20 chaises",
            emplacement: " ", "Église · Nef", zone, danger: false, activiteBloquee: false, compte, "Paul R.", Instant);

        Assert.Equal((OrigineDossier.Interne, zone, compte, "Église · Nef"), (dossier.Origine, dossier.ZoneId!.Value, dossier.DeclarantCompteId!.Value, dossier.Emplacement));
        Assert.Null(dossier.ContactDeclarant);
        Assert.Equal(StatutDossier.AQualifier, dossier.Statut);
    }

    [Fact]
    public void AnInterventionGoesToAnInternalTechnicianOrToAContractorWithAReferent()
    {
        var dossier = Public();
        dossier.Qualifier(PrioriteDossier.Normale, Guid.NewGuid(), composantId: null, Instant);
        var technicien = Guid.NewGuid();

        var aucun = Assert.Throws<SaisieInvalideException>(() => new Intervention(Guid.NewGuid(), dossier, 1, "Réparer", null, null, null, Instant));
        var lesDeux = Assert.Throws<SaisieInvalideException>(() => new Intervention(Guid.NewGuid(), dossier, 1, "Réparer", technicien, "Martin", technicien, Instant));
        var sansReferent = Assert.Throws<SaisieInvalideException>(() => new Intervention(Guid.NewGuid(), dossier, 1, "Réparer", null, "Martin", null, Instant));
        var prestataire = new Intervention(Guid.NewGuid(), dossier, 2, "Couverture", null, " Couverture Martin ", technicien, Instant);

        Assert.Equal("intervenantCompteId", aucun.Champ);
        Assert.Equal("intervenantCompteId", lesDeux.Champ);
        Assert.Equal("referentCompteId", sansReferent.Champ);
        Assert.Equal(("Couverture Martin", technicien), (prestataire.Prestataire, prestataire.EnCharge));
    }

    [Fact]
    public void AssignmentNeedsAQualifiedCaseAndStartingFixesTheTakeOver()
    {
        var dossier = Public();
        Assert.Throws<DossierNonQualifieException>(() => dossier.Affecter(Instant));
        dossier.Qualifier(PrioriteDossier.Urgente, Guid.NewGuid(), composantId: null, Instant);
        Assert.Throws<StatutIncompatibleException>(() => dossier.Demarrer(Instant));

        dossier.Affecter(Instant);
        dossier.Affecter(Instant.AddHours(1)); // seconde intervention : la première affectation fait foi
        var intervention = new Intervention(Guid.NewGuid(), dossier, 1, "Bloc", Guid.NewGuid(), null, null, Instant);
        intervention.Demarrer(Instant.AddHours(2));
        dossier.Demarrer(Instant.AddHours(2));

        Assert.Equal((StatutDossier.EnCours, Instant, Instant.AddHours(2)), (dossier.Statut, dossier.AffecteLe!.Value, dossier.PrisEnChargeLe!.Value));
        Assert.Throws<InterventionIncompatibleException>(() => intervention.Demarrer(Instant));
        Assert.Equal(TimeSpan.FromHours(4), Delais.PriseEnCharge(PrioriteDossier.Urgente));
        Assert.Equal(TimeSpan.FromDays(90), Delais.Resolution(PrioriteDossier.Basse));
    }

    [Fact]
    public void WaitingTimeIsAddedToTheResolutionDeadlineButNotToTheAge()
    {
        var dossier = Public();
        dossier.Qualifier(PrioriteDossier.Haute, Guid.NewGuid(), composantId: null, Instant);
        dossier.Affecter(Instant);
        dossier.Demarrer(Instant);
        var intervention = new Intervention(Guid.NewGuid(), dossier, 1, "Pompe", Guid.NewGuid(), null, null, Instant);
        intervention.Demarrer(Instant);

        Assert.Throws<SaisieInvalideException>(() => intervention.MettreEnAttente(MotifAttente.Livraison, null, DateOnly.FromDateTime(Instant.UtcDateTime.AddYears(2)), Instant));
        intervention.MettreEnAttente(MotifAttente.Livraison, null, DateOnly.FromDateTime(Instant.UtcDateTime.AddDays(3)), Instant);
        dossier.Attendre(intervention.ReexamenLe!.Value, Instant.AddDays(1));
        var suspendue = dossier.EcheanceResolution;
        intervention.Reprendre();
        dossier.Reprendre(Instant.AddDays(4));

        Assert.Null(suspendue);
        Assert.Equal(TimeSpan.FromDays(3), dossier.DureeAttente);
        Assert.Equal(Instant.AddDays(7 + 3), dossier.EcheanceResolution);
        Assert.Equal(Instant, dossier.RecuLe); // ancienneté conservée
        Assert.Throws<InterventionIncompatibleException>(() => intervention.Reprendre());
    }

    private static Signalement Public() =>
        new(Guid.NewGuid(), Organisation, Site, Guid.NewGuid(), NatureSignalement.Incident, "Fuite au plafond", "Sacristie", false, false, "Anne", "06 12", Instant);
}

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

    private static Signalement Public() =>
        new(Guid.NewGuid(), Organisation, Site, Guid.NewGuid(), NatureSignalement.Incident, "Fuite au plafond", "Sacristie", false, false, "Anne", "06 12", Instant);
}

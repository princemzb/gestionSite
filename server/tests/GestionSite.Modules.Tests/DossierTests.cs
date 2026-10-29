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
        dossier.ClasserSansSuite(MotifClassement.DejaTraite, commentaire: null);

        Assert.Equal((PrioriteDossier.Haute, Instant), (dossier.Priorite!.Value, dossier.QualifieLe!.Value));
        Assert.Throws<StatutIncompatibleException>(() => dossier.Qualifier(PrioriteDossier.Basse, zone, composantId: null, Instant));
    }

    [Fact]
    public void ADuplicateIsAttachedToAnotherOpenCaseOfTheSameSite()
    {
        var principal = Public();
        var autreSite = new Signalement(Guid.NewGuid(), Organisation, Guid.NewGuid(), Guid.NewGuid(), NatureSignalement.Incident, "Fuite", "Nef", false, false, "Anne", "06", Instant);
        var classe = Public();
        classe.ClasserSansSuite(MotifClassement.SpamAbusif, commentaire: null);
        var doublon = Public();

        Assert.Throws<SaisieInvalideException>(() => doublon.RattacherA(autreSite));
        Assert.Throws<SaisieInvalideException>(() => doublon.RattacherA(classe));
        doublon.RattacherA(principal);
        Assert.Equal((StatutDossier.RattacheDoublon, principal.Id), (doublon.Statut, doublon.DoublonDe!.Value));
        Assert.Throws<StatutIncompatibleException>(() => doublon.ClasserSansSuite(MotifClassement.AucunProbleme, commentaire: null));
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

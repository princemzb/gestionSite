using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Notifications;
using GestionSite.Modules.Maintenance.Planificateur;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

/// <summary>
/// Cycle d'un danger (US-10, S10-04 : prise en compte, mise en sécurité déclarée dès l'alerte, vérifiée ou refusée, levée,
/// écartement) et rappels à horloge contrôlée (Q4-A : 15 min puis toutes les heures ; rappels horaires après la prise en
/// compte, autres R/D à 4 h ; cycle relancé par une mise en sécurité insuffisante).
/// </summary>
public sealed class DangerTests
{
    private static readonly DateTimeOffset Alerte = new(2026, 11, 3, 22, 0, 0, TimeSpan.Zero); // la nuit : le danger part quand même
    private static readonly Guid Responsable = Guid.NewGuid();
    private static readonly Guid Technicien = Guid.NewGuid();

    [Fact]
    public void TheStateGoesFromOpenToVerifiedAndAnInsufficientSecuringFallsBackToTakenIntoAccount()
    {
        var (_, alerte) = Nouvelle();

        alerte.DeclarerMiseEnSecurite(ActionMiseEnSecurite.AccesCondamne, null, Technicien, Alerte.AddMinutes(5));
        Assert.Equal((EtatDanger.Ouvert, false), (alerte.Etat, alerte.NonMaitrise)); // Q-M1 : déclarée, mais pas encore prise en compte
        Assert.Equal("non_pris_en_compte", Assert.Throws<DangerIncompatibleException>(() => alerte.VerifierMiseEnSecurite(true, null, Responsable, Alerte)).Code);
        alerte.PrendreEnCompte(Responsable, Alerte.AddMinutes(10));
        Assert.Equal(EtatDanger.MiseEnSecuriteDeclaree, alerte.Etat);
        Assert.Equal("deja_pris_en_compte", Assert.Throws<DangerIncompatibleException>(() => alerte.PrendreEnCompte(Technicien, Alerte)).Code);
        Assert.Equal("securite_deja_declaree",
            Assert.Throws<DangerIncompatibleException>(() => alerte.DeclarerMiseEnSecurite(ActionMiseEnSecurite.Evacuation, null, Technicien, Alerte)).Code);

        Assert.Equal("motif", Assert.Throws<SaisieInvalideException>(() => alerte.VerifierMiseEnSecurite(false, " ", Responsable, Alerte)).Champ);
        var (declarant, action, motif) = alerte.VerifierMiseEnSecurite(false, "Balisage arraché", Responsable, Alerte.AddMinutes(30));
        Assert.Equal((Technicien, ActionMiseEnSecurite.AccesCondamne, "Balisage arraché"), (declarant, action, motif));
        Assert.Equal((EtatDanger.PrisEnCompte, true, Alerte.AddMinutes(30)), (alerte.Etat, alerte.NonMaitrise, alerte.RappelsSecuriteDepuis));
        Assert.Equal("securite_non_declaree", Assert.Throws<DangerIncompatibleException>(() => alerte.VerifierMiseEnSecurite(true, null, Responsable, Alerte)).Code);

        Assert.Equal("commentaire",
            Assert.Throws<SaisieInvalideException>(() => alerte.DeclarerMiseEnSecurite(ActionMiseEnSecurite.Autre, null, Technicien, Alerte)).Champ);
        Assert.Equal("action",
            Assert.Throws<SaisieInvalideException>(() => alerte.DeclarerMiseEnSecurite((ActionMiseEnSecurite)99, null, Technicien, Alerte)).Champ);
        alerte.DeclarerMiseEnSecurite(ActionMiseEnSecurite.EnergieCoupee, "Disjoncteur coupé", Technicien, Alerte.AddMinutes(40));
        alerte.VerifierMiseEnSecurite(true, null, Responsable, Alerte.AddMinutes(45));
        Assert.Equal(EtatDanger.MiseEnSecuriteVerifiee, alerte.Etat);
        Assert.Equal("securite_declaree", Assert.Throws<DangerIncompatibleException>(() => alerte.Ecarter(MotifEcartement.DejaTraite, null, Responsable, Alerte)).Code);
        Assert.Equal("securite_non_declaree", Assert.Throws<DangerIncompatibleException>(() => alerte.VerifierMiseEnSecurite(true, null, Responsable, Alerte)).Code);
    }

    [Fact]
    public void LiftingNeedsTheDangerTakenIntoAccountAndAResolvedCaseForThatReasonAndEndsTheFollowUp()
    {
        var (_, alerte) = Nouvelle();

        Assert.Equal("non_pris_en_compte",
            Assert.Throws<DangerIncompatibleException>(() => alerte.Lever(MotifLevee.ReparationConstatee, null, false, Responsable, Alerte)).Code);
        alerte.PrendreEnCompte(Responsable, Alerte);
        Assert.Equal("dossier_non_resolu",
            Assert.Throws<DangerIncompatibleException>(() => alerte.Lever(MotifLevee.ReparationDossierResolu, null, false, Responsable, Alerte)).Code);
        Assert.Equal("motif", Assert.Throws<SaisieInvalideException>(() => alerte.Lever((MotifLevee)9, null, false, Responsable, Alerte)).Champ);
        Assert.Equal("commentaire", Assert.Throws<SaisieInvalideException>(() => alerte.Lever(MotifLevee.Autre, null, false, Responsable, Alerte)).Champ);
        alerte.Lever(MotifLevee.ReparationDossierResolu, null, true, Responsable, Alerte.AddDays(1));

        Assert.Equal((EtatDanger.Leve, false, "ReparationDossierResolu", Alerte.AddDays(1)), (alerte.Etat, alerte.NonMaitrise, alerte.FinMotif, alerte.FinLe));
        Assert.Equal("danger_clos", Assert.Throws<DangerIncompatibleException>(() => alerte.PrendreEnCompte(Responsable, Alerte)).Code);
        Assert.Equal("danger_clos",
            Assert.Throws<DangerIncompatibleException>(() => alerte.DeclarerMiseEnSecurite(ActionMiseEnSecurite.Evacuation, null, Technicien, Alerte)).Code);
        Assert.Empty(Rappels.Danger(alerte, Dossier(), [], Alerte.AddHours(5)).Dus);
    }

    [Fact]
    public void ADangerCanBeDismissedWithoutBeingTakenIntoAccountWithAReason()
    {
        var (_, alerte) = Nouvelle();

        Assert.Equal("motif", Assert.Throws<SaisieInvalideException>(() => alerte.Ecarter((MotifEcartement)0, null, Responsable, Alerte)).Champ);
        Assert.Equal("commentaire", Assert.Throws<SaisieInvalideException>(() => alerte.Ecarter(MotifEcartement.Autre, "", Responsable, Alerte)).Champ);
        alerte.Ecarter(MotifEcartement.AucunDangerConstate, null, Responsable, Alerte);

        Assert.Equal((EtatDanger.Ecarte, FinDanger.Ecarte, Responsable), (alerte.Etat, alerte.Fin, alerte.FinParCompteId));
        Assert.Equal("danger_clos", Assert.Throws<DangerIncompatibleException>(() => alerte.Ecarter(MotifEcartement.DejaTraite, null, Responsable, Alerte)).Code);
    }

    [Fact]
    public void AnUnacknowledgedDangerRealertsEveryoneAt15MinutesThenEveryHourWithAStableKey()
    {
        var (dossier, alerte) = Nouvelle();
        var intervention = new Intervention(Guid.NewGuid(), dossier, 1, "Couper", Technicien, null, null, Alerte);

        var avant = Rappels.Danger(alerte, dossier, [intervention], Alerte.AddMinutes(14));
        var a15 = Rappels.Danger(alerte, dossier, [intervention], Alerte.AddMinutes(15));
        var rejeu = Rappels.Danger(alerte, dossier, [intervention], Alerte.AddMinutes(74));
        var heure = Rappels.Danger(alerte, dossier, [intervention], Alerte.AddMinutes(75));
        alerte.DeclarerMiseEnSecurite(ActionMiseEnSecurite.PublicTenuADistance, null, Technicien, Alerte.AddMinutes(80));
        var declaree = Rappels.Danger(alerte, dossier, [intervention], Alerte.AddMinutes(135));

        Assert.Equal((0, Alerte.AddMinutes(15)), (avant.Dus.Count, avant.Prochain));
        var nouvelle = Assert.Single(a15.Dus);
        Assert.Equal((TypeNotification.DangerNonPrisEnCompte, true, true, alerte.Id), (nouvelle.Type, nouvelle.Cible.Encadrement, nouvelle.Cible.Information, nouvelle.AlerteId));
        Assert.Equal([Technicien], nouvelle.Cible.EnCharge);
        Assert.Equal(Alerte.AddMinutes(75), a15.Prochain);
        Assert.Equal(nouvelle.Cle, Assert.Single(rejeu.Dus).Cle);
        Assert.EndsWith(":1", Assert.Single(heure.Dus).Cle, StringComparison.Ordinal);
        Assert.Single(declaree.Dus); // Q-M1 : les alertes continuent jusqu'à la prise en compte
    }

    [Fact]
    public void AfterAcknowledgementTheOwnerIsRemindedHourlyOtherManagersFrom4HoursAndAnInsufficientSecuringRestartsTheCycle()
    {
        var (dossier, alerte) = Nouvelle();
        var prise = Alerte.AddMinutes(5);
        alerte.PrendreEnCompte(Responsable, prise);

        var avant = Rappels.Danger(alerte, dossier, [], prise.AddMinutes(59));
        var h1 = Rappels.Danger(alerte, dossier, [], prise.AddHours(1));
        var h4 = Rappels.Danger(alerte, dossier, [], prise.AddHours(4));
        alerte.DeclarerMiseEnSecurite(ActionMiseEnSecurite.AccesCondamne, null, Technicien, prise.AddHours(5));
        var declaree = Rappels.Danger(alerte, dossier, [], prise.AddHours(6));
        var refus = prise.AddHours(7);
        alerte.VerifierMiseEnSecurite(false, "Insuffisant", Responsable, refus);
        var apresRefus = Rappels.Danger(alerte, dossier, [], refus.AddHours(1));

        Assert.Equal((0, prise.AddHours(1)), (avant.Dus.Count, avant.Prochain));
        var premier = Assert.Single(h1.Dus);
        Assert.Equal((TypeNotification.MiseEnSecuriteAFaire, false, false), (premier.Type, premier.Cible.Encadrement, premier.Cible.Information));
        Assert.Equal([Responsable], premier.Cible.EnCharge);
        Assert.Equal(prise.AddHours(2), h1.Prochain);
        Assert.True(Assert.Single(h4.Dus).Cible.Encadrement);
        Assert.Equal((0, (DateTimeOffset?)null), (declaree.Dus.Count, declaree.Prochain));
        var relance = Assert.Single(apresRefus.Dus);
        Assert.False(relance.Cible.Encadrement); // nouveau cycle : les autres R/D au bout de 4 h à nouveau
        Assert.NotEqual(premier.Cle, relance.Cle);
    }

    [Fact]
    public void AlertRecipientsAreManagersPastoralStaffAndPeopleInChargeStillMembers()
    {
        var responsable = new MembreSite(Responsable, "R", ["Responsable"]);
        var pasteur = new MembreSite(Guid.NewGuid(), "P", ["Pasteur"]);
        var technicien = new MembreSite(Technicien, "T", ["Technicien"]);
        var autre = new MembreSite(Guid.NewGuid(), "T2", ["Technicien"]);
        var site = new DestinatairesSite([responsable, pasteur, technicien, autre]);
        var parti = Guid.NewGuid();

        Assert.Equal([Responsable, pasteur.CompteId, Technicien],
            RappelsOrganisation.Destinataires(new CibleRappel(true, [Technicien, parti], Information: true), site));
        Assert.Equal([Technicien], site.OuResponsables(Technicien));
        Assert.Equal([Responsable], site.OuResponsables(parti));
        Assert.Equal([Responsable], site.OuResponsables(null));
    }

    private static Signalement Dossier() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NatureSignalement.Incident, "Câble dénudé", "Chœur", true, false, "Anne", "06", Alerte);

    private static (Signalement Dossier, AlerteDanger Alerte) Nouvelle()
    {
        var dossier = Dossier();
        return (dossier, new AlerteDanger(Guid.NewGuid(), dossier));
    }
}

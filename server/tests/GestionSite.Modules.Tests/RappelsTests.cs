using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Notifications;
using GestionSite.Modules.Maintenance.Planificateur;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

/// <summary>
/// Rappels du planificateur à horloge contrôlée (US-10, S10-02, S10-03) : DEC-05 (T-04.10, T-04.11, T-04.14, Q7-A),
/// dossiers à qualifier (Q8-A), réexamen des attentes (Q9-A), heure de Paris et changements d'heure.
/// </summary>
public sealed class RappelsTests
{
    // Lundi 2 novembre 2026, 10 h à Paris (heure d'hiver, UTC+1).
    private static readonly DateTimeOffset Lundi10h = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Technicien = Guid.NewGuid();

    [Fact]
    public void AQualifiedButUnassignedUrgentCaseRemindsManagersAt75PercentThenEscalatesAt100Percent()
    {
        var dossier = Qualifie(PrioriteDossier.Urgente, Lundi10h);

        var avant = Rappels.Calculer(dossier, [], Lundi10h.AddHours(3).AddMinutes(-1));
        var a13h = Rappels.Calculer(dossier, [], Lundi10h.AddHours(3));
        var a14h = Rappels.Calculer(dossier, [], Lundi10h.AddHours(4));

        // Q7-A : le délai part de la qualification, même sans affectation (T-04.10).
        Assert.Equal(Lundi10h.AddHours(4), dossier.EcheancePriseEnCharge);
        Assert.Empty(avant.Dus);
        Assert.Equal(Lundi10h.AddHours(3), avant.Prochain);
        var rappel = Assert.Single(a13h.Dus);
        Assert.Equal(TypeNotification.PriseEnChargeProche, rappel.Type);
        Assert.Equal((true, 0), (rappel.Cible.Encadrement, rappel.Cible.EnCharge.Count)); // R/D : non affecté
        Assert.EndsWith(":75", rappel.Cle, StringComparison.Ordinal);
        Assert.Equal(Lundi10h.AddHours(4), a13h.Prochain);
        var escalade = Assert.Single(a14h.Dus);
        Assert.Equal((TypeNotification.PriseEnChargeDepassee, true), (escalade.Type, escalade.Cible.Encadrement));
        Assert.EndsWith(":100", escalade.Cle, StringComparison.Ordinal);
        Assert.Equal(Lundi10h.AddDays(1).AddHours(4), a14h.Prochain);
    }

    [Fact]
    public void TheDailyReminderComesBackAtTheSameTimeWithTheSameKeyWhenReplayed()
    {
        var dossier = Qualifie(PrioriteDossier.Urgente, Lundi10h);
        var mardi14h = Lundi10h.AddDays(1).AddHours(4);

        var premier = Rappels.Calculer(dossier, [], mardi14h);
        var rejeu = Rappels.Calculer(dossier, [], mardi14h.AddMinutes(20));
        var mercredi = Rappels.Calculer(dossier, [], mardi14h.AddDays(1));

        // T-04.11 : un rappel quotidien ; rejoué, même clé (la base n'en écrit qu'un par destinataire).
        var quotidien = premier.Dus.Single(r => r.Type == TypeNotification.PriseEnChargeDepassee);
        Assert.EndsWith(":j1", quotidien.Cle, StringComparison.Ordinal);
        Assert.Equal(quotidien.Cle, rejeu.Dus.Single(r => r.Type == TypeNotification.PriseEnChargeDepassee).Cle);
        Assert.EndsWith(":j2", mercredi.Dus.Single(r => r.Type == TypeNotification.PriseEnChargeDepassee).Cle, StringComparison.Ordinal);
        Assert.Equal(Lundi10h.AddHours(36), premier.Prochain); // 75 % du délai de résolution (2 jours) avant le rappel suivant
    }

    [Fact]
    public void ResolutionRemindersGoToTheAssignedTechnicianOnlyAndAreSuspendedWhileOnHold()
    {
        var dossier = Qualifie(PrioriteDossier.Normale, Lundi10h);
        var intervention = Affecter(dossier, Lundi10h.AddHours(1));
        intervention.Demarrer(Lundi10h.AddHours(2));
        dossier.Synchroniser([intervention], Lundi10h.AddHours(2));

        // T-04.14 : 75 % de 30 jours (22,5 j) : rappel au technicien affecté, pas aux R/D.
        var a75 = Rappels.Calculer(dossier, [intervention], Lundi10h.AddHours(540));
        var rappel = Assert.Single(a75.Dus);
        Assert.Equal((TypeNotification.ResolutionProche, false), (rappel.Type, rappel.Cible.Encadrement));
        Assert.Equal([Technicien], rappel.Cible.EnCharge);

        // Attente : délai de résolution suspendu, aucun rappel de résolution ; seuls les rappels de réexamen.
        intervention.MettreEnAttente(MotifAttente.Livraison, null, new DateOnly(2026, 11, 20), Lundi10h.AddDays(3));
        dossier.Synchroniser([intervention], Lundi10h.AddDays(3));
        var enAttente = Rappels.Calculer(dossier, [intervention], Lundi10h.AddDays(40));
        Assert.DoesNotContain(enAttente.Dus, r => r.Type is TypeNotification.ResolutionProche or TypeNotification.ResolutionDepassee);
        Assert.Contains(enAttente.Dus, r => r.Type == TypeNotification.ReexamenDepasse);
    }

    [Fact]
    public void AReassignedCaseSendsTheNextReminderToTheNewPersonInChargeAndThe100PercentGoesToManagers()
    {
        var dossier = Qualifie(PrioriteDossier.Haute, Lundi10h);
        var premiere = Affecter(dossier, Lundi10h.AddHours(1));
        var nouveau = Guid.NewGuid();
        var seconde = new Intervention(Guid.NewGuid(), dossier, 2, "Reprise", nouveau, null, null, Lundi10h.AddHours(2));

        var a75 = Rappels.Calculer(dossier, [seconde], Lundi10h.AddHours(18)); // la première a été clôturée ou retirée
        var deux = Rappels.Calculer(dossier, [premiere, seconde], Lundi10h.AddHours(18));
        var quotidien = Rappels.Calculer(dossier, [seconde], Lundi10h.AddHours(48));

        Assert.Equal([nouveau], Assert.Single(a75.Dus).Cible.EnCharge);
        Assert.Equal([Technicien, nouveau], Assert.Single(deux.Dus).Cible.EnCharge);
        var rappelQuotidien = quotidien.Dus.Single(r => r.Type == TypeNotification.PriseEnChargeDepassee);
        Assert.Equal((true, nouveau), (rappelQuotidien.Cible.Encadrement, rappelQuotidien.Cible.EnCharge.Single()));
    }

    [Fact]
    public void AMovedDeadlineStartsANewCycleOfThresholdsWhileAnUnchangedOneKeepsItsKeys()
    {
        var dossier = Qualifie(PrioriteDossier.Basse, Lundi10h);
        var cleAvant = Rappels.Calculer(dossier, [], Lundi10h.AddDays(6)).Dus.Single().Cle; // 75 % de 7 jours
        var memeEcheance = Rappels.Calculer(dossier, [], Lundi10h.AddDays(6).AddHours(1)).Dus.Single().Cle;

        dossier.Qualifier(PrioriteDossier.Normale, dossier.ZoneId!.Value, null, Lundi10h.AddDays(6));
        var apres = Rappels.Calculer(dossier, [], Lundi10h.AddDays(6)).Dus.Single(r => r.Type == TypeNotification.PriseEnChargeDepassee).Cle;

        Assert.Equal(cleAvant, memeEcheance);
        // Normale (3 jours) : dépassé depuis 3 jours, nouvelle échéance donc nouveau cycle (escalade et rappels repartent).
        Assert.NotEqual(cleAvant[..cleAvant.LastIndexOf(':')], apres[..apres.LastIndexOf(':')]);
        Assert.EndsWith(":j3", apres, StringComparison.Ordinal);
    }

    [Fact]
    public void AResumptionAfterAnOverdueHoldEscalatesAgainAtTheNewResolutionDeadline()
    {
        var dossier = Qualifie(PrioriteDossier.Urgente, Lundi10h);
        var intervention = Affecter(dossier, Lundi10h);
        intervention.Demarrer(Lundi10h);
        dossier.Synchroniser([intervention], Lundi10h);
        var escalade = Rappels.Calculer(dossier, [intervention], Lundi10h.AddDays(2)).Dus.Single(r => r.Type == TypeNotification.ResolutionDepassee).Cle;

        intervention.MettreEnAttente(MotifAttente.Livraison, null, new DateOnly(2026, 11, 20), Lundi10h.AddDays(3));
        dossier.Synchroniser([intervention], Lundi10h.AddDays(3));
        intervention.Reprendre();
        dossier.Synchroniser([intervention], Lundi10h.AddDays(13)); // 10 jours d'attente ajoutés au délai
        var nouvelle = Rappels.Calculer(dossier, [intervention], Lundi10h.AddDays(12)).Dus.Single(r => r.Type == TypeNotification.ResolutionDepassee).Cle;

        Assert.EndsWith(":100", escalade, StringComparison.Ordinal);
        Assert.EndsWith(":100", nouvelle, StringComparison.Ordinal);
        Assert.NotEqual(escalade, nouvelle);
    }

    [Fact]
    public void AReviewDateSetForTodayAfter8DoesNotRemindItsAuthorButStillEscalatesAtDay3()
    {
        var debut = new DateTimeOffset(2026, 11, 10, 13, 0, 0, TimeSpan.Zero); // 14 h à Paris
        var dossier = Qualifie(PrioriteDossier.Normale, debut);
        var intervention = Affecter(dossier, debut);
        intervention.Demarrer(debut);
        intervention.MettreEnAttente(MotifAttente.Livraison, null, new DateOnly(2026, 11, 10), debut);
        dossier.Synchroniser([intervention], debut);

        var unePlusTard = Rappels.Calculer(dossier, [intervention], debut.AddMinutes(1));
        var j3 = Rappels.Calculer(dossier, [intervention], new DateTimeOffset(2026, 11, 13, 7, 0, 0, TimeSpan.Zero));

        Assert.Empty(unePlusTard.Dus);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 7, 0, 0, TimeSpan.Zero), unePlusTard.Prochain);
        Assert.Equal(TypeNotification.ReexamenDepasse, Assert.Single(j3.Dus).Type);
    }

    [Fact]
    public void AClosedCaseHasNoReminderAndAStartedOneNoLongerHasATakeOverDeadline()
    {
        var classe = Recu(Lundi10h);
        classe.ClasserSansSuite(MotifClassement.DejaTraite, null, aDesDoublons: false);
        var dossier = Qualifie(PrioriteDossier.Urgente, Lundi10h);
        var intervention = Affecter(dossier, Lundi10h.AddHours(1));
        intervention.Demarrer(Lundi10h.AddHours(2));
        dossier.Synchroniser([intervention], Lundi10h.AddHours(2));

        var clos = Rappels.Calculer(classe, [], Lundi10h.AddDays(10));
        Assert.Empty(clos.Dus);
        Assert.Null(clos.Prochain);
        Assert.Null(dossier.EcheancePriseEnCharge);
        Assert.DoesNotContain(Rappels.Calculer(dossier, [intervention], Lundi10h.AddDays(1)).Dus, r => r.Type == TypeNotification.PriseEnChargeDepassee);
    }

    [Fact]
    public void ACaseLeftToQualifyRemindsManagersEvery24HoursFromReceptionOrReopening()
    {
        var dossier = Recu(Lundi10h);

        var avant = Rappels.Calculer(dossier, [], Lundi10h.AddHours(23));
        var premier = Rappels.Calculer(dossier, [], Lundi10h.AddHours(24));
        var second = Rappels.Calculer(dossier, [], Lundi10h.AddHours(49));

        Assert.Empty(avant.Dus);
        Assert.Equal(Lundi10h.AddHours(24), avant.Prochain);
        var rappel = Assert.Single(premier.Dus);
        Assert.Equal((TypeNotification.AQualifier, true), (rappel.Type, rappel.Cible.Encadrement));
        Assert.EndsWith(":1", rappel.Cle, StringComparison.Ordinal);
        Assert.EndsWith(":2", Assert.Single(second.Dus).Cle, StringComparison.Ordinal);
        Assert.Equal(Lundi10h.AddHours(72), second.Prochain);

        // Qualifié : plus de rappel « à qualifier » ; rouvert : un nouveau cycle part de la réouverture.
        dossier.Qualifier(PrioriteDossier.Basse, Guid.NewGuid(), null, Lundi10h.AddHours(50));
        Assert.DoesNotContain(Rappels.Calculer(dossier, [], Lundi10h.AddHours(51)).Dus, r => r.Type == TypeNotification.AQualifier);
        var rouvert = Recu(Lundi10h);
        rouvert.ClasserSansSuite(MotifClassement.AucunProbleme, null, aDesDoublons: false);
        rouvert.Rouvrir(Lundi10h.AddDays(5));
        var apres = Rappels.Calculer(rouvert, [], Lundi10h.AddDays(6));
        Assert.EndsWith($":{Lundi10h.AddDays(5).ToUnixTimeMilliseconds()}:1", Assert.Single(apres.Dus).Cle, StringComparison.Ordinal);
    }

    [Fact]
    public void AReviewDateRemindsThePersonInChargeAt8ParisTimeThenManagersAtDay3ThenBothWeekly()
    {
        var reexamen = new DateOnly(2026, 11, 10);
        var (dossier, intervention) = EnAttente(reexamen);
        var huitHeures = new DateTimeOffset(2026, 11, 10, 7, 0, 0, TimeSpan.Zero); // 8 h à Paris en hiver

        var veille = Rappels.Calculer(dossier, [intervention], huitHeures.AddMinutes(-1));
        var jour = Rappels.Calculer(dossier, [intervention], huitHeures);
        var j3 = Rappels.Calculer(dossier, [intervention], huitHeures.AddDays(3));
        var j10Avant8h = Rappels.Calculer(dossier, [intervention], huitHeures.AddDays(10).AddMinutes(-1));
        var j10 = Rappels.Calculer(dossier, [intervention], huitHeures.AddDays(10));

        Assert.Empty(veille.Dus);
        Assert.Equal(huitHeures, veille.Prochain);
        var rappel = Assert.Single(jour.Dus);
        Assert.Equal((TypeNotification.ReexamenAtteint, false, Technicien), (rappel.Type, rappel.Cible.Encadrement, rappel.Cible.EnCharge.Single()));
        Assert.Equal(huitHeures.AddDays(3), jour.Prochain);
        var escalade = Assert.Single(j3.Dus);
        Assert.Equal((TypeNotification.ReexamenDepasse, true, 0), (escalade.Type, escalade.Cible.Encadrement, escalade.Cible.EnCharge.Count));
        Assert.EndsWith(":j3", escalade.Cle, StringComparison.Ordinal);
        Assert.Equal(huitHeures.AddDays(10), j3.Prochain);
        Assert.EndsWith(":j3", Assert.Single(j10Avant8h.Dus).Cle, StringComparison.Ordinal);
        var hebdomadaire = Assert.Single(j10.Dus);
        Assert.Equal((true, Technicien), (hebdomadaire.Cible.Encadrement, hebdomadaire.Cible.EnCharge.Single()));
        Assert.EndsWith(":s1", hebdomadaire.Cle, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportedReviewDateStartsANewCycleAndAResumptionStopsTheReminders()
    {
        var (dossier, intervention) = EnAttente(new DateOnly(2026, 11, 10));
        var avant = Rappels.Calculer(dossier, [intervention], new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)).Dus.Single().Cle;

        var (ancienne, commentaire) = intervention.ReporterReexamen(new DateOnly(2026, 11, 17), "  Devis attendu  ", new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        var reporte = Rappels.Calculer(dossier, [intervention], new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
        var nouveauJour = Rappels.Calculer(dossier, [intervention], new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal((new DateOnly(2026, 11, 10), "Devis attendu", 1), (ancienne, commentaire, intervention.ReportsReexamen));
        Assert.Empty(reporte.Dus);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 7, 0, 0, TimeSpan.Zero), reporte.Prochain);
        Assert.NotEqual(avant, Assert.Single(nouveauJour.Dus).Cle);

        intervention.Reprendre();
        dossier.Synchroniser([intervention], new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, intervention.ReportsReexamen);
        Assert.DoesNotContain(Rappels.Calculer(dossier, [intervention], new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero)).Dus,
            r => r.Type is TypeNotification.ReexamenAtteint or TypeNotification.ReexamenDepasse);
    }

    [Fact]
    public void AReviewDateCanOnlyBeReportedWhileOnHoldToAnotherDateWithinAYearAndWithAComment()
    {
        var (_, intervention) = EnAttente(new DateOnly(2026, 11, 10));
        var maintenant = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);
        var nonDemarree = new Intervention(Guid.NewGuid(), Qualifie(PrioriteDossier.Basse, Lundi10h), 1, "Titre", Technicien, null, null, Lundi10h);

        Assert.Throws<InterventionIncompatibleException>(() => nonDemarree.ReporterReexamen(new DateOnly(2026, 11, 20), "Motif", maintenant));
        Assert.Equal("reexamenLe", Assert.Throws<SaisieInvalideException>(() => intervention.ReporterReexamen(new DateOnly(2026, 11, 10), "Motif", maintenant)).Champ);
        Assert.Equal("reexamenLe", Assert.Throws<SaisieInvalideException>(() => intervention.ReporterReexamen(new DateOnly(2027, 12, 1), "Motif", maintenant)).Champ);
        Assert.Equal("reexamenLe", Assert.Throws<SaisieInvalideException>(() => intervention.ReporterReexamen(null, "Motif", maintenant)).Champ);
        Assert.Equal("commentaire", Assert.Throws<SaisieInvalideException>(() => intervention.ReporterReexamen(new DateOnly(2026, 11, 20), "  ", maintenant)).Champ);
        Assert.Equal(0, intervention.ReportsReexamen);
    }

    [Fact]
    public void DeadlinesAreExactDurationsAcrossTheClockChangeWhileTheReviewHourStaysAt8InParis()
    {
        // Échéance samedi 24 octobre 2026, 14 h à Paris (UTC+2) ; passage à l'heure d'hiver dans la nuit de dimanche.
        var qualifie = new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);
        var dossier = Qualifie(PrioriteDossier.Urgente, qualifie);
        var echeance = qualifie.AddHours(4);

        // 24 h exactes : 14 h à Paris le dimanche avant le changement… puis 13 h le lundi (heure d'hiver).
        var dimanche = Rappels.Calculer(dossier, [], echeance.AddDays(2).AddTicks(-1)).Dus.Single(r => r.Type == TypeNotification.PriseEnChargeDepassee);
        var lundi = Rappels.Calculer(dossier, [], echeance.AddDays(2)).Dus.Single(r => r.Type == TypeNotification.PriseEnChargeDepassee);

        Assert.EndsWith(":j1", dimanche.Cle, StringComparison.Ordinal);
        Assert.EndsWith(":j2", lundi.Cle, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero), JourCivil.A(new DateOnly(2026, 10, 24), Rappels.HeureReexamen));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.Zero), JourCivil.A(new DateOnly(2026, 10, 25), Rappels.HeureReexamen));
        Assert.Equal(new DateTimeOffset(2026, 3, 28, 7, 0, 0, TimeSpan.Zero), JourCivil.A(new DateOnly(2026, 3, 28), Rappels.HeureReexamen));
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 6, 0, 0, TimeSpan.Zero), JourCivil.A(new DateOnly(2026, 3, 29), Rappels.HeureReexamen));
        // Heure sautée (2 h 30 le 29 mars) avancée d'une heure ; heure répétée (2 h 30 le 25 octobre) : première occurrence.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), JourCivil.A(new DateOnly(2026, 3, 29), new TimeOnly(2, 30)));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), JourCivil.A(new DateOnly(2026, 10, 25), new TimeOnly(2, 30)));
    }

    [Fact]
    public void AReviewDateOnTheDayOfTheClockChangeIsStillRemindedAt8ParisTime()
    {
        var (dossier, intervention) = EnAttente(new DateOnly(2027, 3, 28), depuis: new DateTimeOffset(2027, 3, 20, 9, 0, 0, TimeSpan.Zero));

        var veille = Rappels.Calculer(dossier, [intervention], new DateTimeOffset(2027, 3, 27, 12, 0, 0, TimeSpan.Zero));
        var semaine = Rappels.Calculer(dossier, [intervention], new DateTimeOffset(2027, 4, 7, 6, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2027, 3, 28, 6, 0, 0, TimeSpan.Zero), veille.Prochain); // passage à l'heure d'été ce jour-là
        Assert.EndsWith(":s1", Assert.Single(semaine.Dus).Cle, StringComparison.Ordinal); // 7 avril, 8 h (UTC+2)
    }

    [Fact]
    public void RecipientsAreTheCurrentMembersAndAPersonInChargeWhoLeftIsReplacedByManagers()
    {
        var responsable = new MembreSite(Guid.NewGuid(), "R", ["Responsable"]);
        var delegue = new MembreSite(Guid.NewGuid(), "D", ["Delegue"]);
        var technicien = new MembreSite(Technicien, "T", ["Technicien"]);
        var site = new DestinatairesSite([responsable, delegue, technicien]);
        var parti = Guid.NewGuid();

        Assert.Equal([Technicien], RappelsOrganisation.Destinataires(new CibleRappel(false, [Technicien]), site));
        Assert.Equal([responsable.CompteId, delegue.CompteId], RappelsOrganisation.Destinataires(new CibleRappel(false, [parti]), site));
        Assert.Equal([responsable.CompteId, delegue.CompteId, Technicien], RappelsOrganisation.Destinataires(new CibleRappel(true, [Technicien, parti]), site));
        Assert.Equal([responsable.CompteId, delegue.CompteId], RappelsOrganisation.Destinataires(new CibleRappel(true, []), site));
    }

    private static Signalement Recu(DateTimeOffset le) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NatureSignalement.Incident, "Fuite", "Nef", false, false, "Anne", "06", le);

    private static Signalement Qualifie(PrioriteDossier priorite, DateTimeOffset le)
    {
        var dossier = Recu(le.AddHours(-1));
        dossier.Qualifier(priorite, Guid.NewGuid(), null, le);
        return dossier;
    }

    private static Intervention Affecter(Signalement dossier, DateTimeOffset le)
    {
        var intervention = new Intervention(Guid.NewGuid(), dossier, 1, "Remplacer", Technicien, null, null, le);
        dossier.Affecter(le);
        dossier.Synchroniser([intervention], le);
        return intervention;
    }

    private static (Signalement Dossier, Intervention Intervention) EnAttente(DateOnly reexamen, DateTimeOffset? depuis = null)
    {
        var debut = depuis ?? new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
        var dossier = Qualifie(PrioriteDossier.Normale, debut);
        var intervention = Affecter(dossier, debut);
        intervention.Demarrer(debut);
        intervention.MettreEnAttente(MotifAttente.Livraison, null, reexamen, debut);
        dossier.Synchroniser([intervention], debut);
        return (dossier, intervention);
    }
}

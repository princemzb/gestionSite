using GestionSite.Web.Dossiers;

namespace GestionSite.Api.Tests;

/// <summary>Libellés des écrans de dossiers (US-09) : statuts, priorités, motifs, délais restants, journal.</summary>
public sealed class LibellesDossiersTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("AQualifier", "À qualifier")]
    [InlineData("Affecte", "Affecté")]
    [InlineData("EnCours", "En cours")]
    [InlineData("EnAttente", "En attente")]
    [InlineData("Cloture", "Clôturé")]
    [InlineData("ClasseSansSuite", "Classé sans suite")]
    [InlineData("RattacheDoublon", "Rattaché à un doublon")]
    [InlineData("Inconnu", "Inconnu")]
    public void CaseStatusesReadInFrench(string code, string libelle) => Assert.Equal(libelle, LibellesDossiers.Statut(code));

    [Theory]
    [InlineData("Affectee", "Affectée")]
    [InlineData("EnCours", "En cours")]
    [InlineData("EnAttente", "En attente")]
    [InlineData("Cloturee", "Clôturée")]
    [InlineData("Autre", "Autre")]
    public void InterventionStatusesReadInFrench(string code, string libelle) => Assert.Equal(libelle, LibellesDossiers.StatutIntervention(code));

    [Theory]
    [InlineData("Declaration", "Déclaré")]
    [InlineData("Qualification", "Qualifié")]
    [InlineData("RattachementDoublon", "Rattaché à un doublon")]
    [InlineData("ClassementSansSuite", "Classé sans suite")]
    [InlineData("Affectation", "Intervention affectée")]
    [InlineData("Demarrage", "Intervention démarrée")]
    [InlineData("MiseEnAttente", "Mise en attente")]
    [InlineData("Reprise", "Reprise")]
    [InlineData("ClotureIntervention", "Intervention clôturée")]
    [InlineData("Cloture", "Dossier clôturé")]
    [InlineData("Reouverture", "Rouvert")]
    [InlineData("Nouveau", "Nouveau")]
    public void JournalEventsReadInFrench(string code, string libelle) => Assert.Equal(libelle, LibellesDossiers.Nature(code));

    [Fact]
    public void PrioritiesReasonsAndStatesHaveLabels()
    {
        Assert.Equal("Non qualifié", LibellesDossiers.Priorite(null));
        Assert.Equal("Urgente", LibellesDossiers.Priorite("Urgente"));
        Assert.Equal("Rare", LibellesDossiers.Priorite("Rare"));
        Assert.Equal(("priorite-urgente", "priorite-haute", "priorite-normale"),
            (LibellesDossiers.ClassePriorite("Urgente"), LibellesDossiers.ClassePriorite("Haute"), LibellesDossiers.ClassePriorite(null)));
        Assert.Equal("—", LibellesDossiers.Libelle(LibellesDossiers.MotifsAttente, null));
        Assert.Equal("Livraison de fournitures", LibellesDossiers.Libelle(LibellesDossiers.MotifsAttente, "Livraison"));
        Assert.Equal("Inconnu", LibellesDossiers.Libelle(LibellesDossiers.EtatsFinaux, "Inconnu"));
        Assert.Equal("05/11/2026", LibellesDossiers.Date(new DateOnly(2026, 11, 5)));
    }

    [Theory]
    [InlineData(5, "dans 5 h")]
    [InlineData(0.2, "dans 1 h")]
    [InlineData(72, "dans 3 j")]
    [InlineData(-30, "dépassé de 30 h")]
    [InlineData(-100, "dépassé de 5 j")]
    public void RemainingTimeIsInHoursUnderTwoDaysThenInDays(double heures, string attendu) =>
        Assert.Equal(attendu, LibellesDossiers.Restant(Maintenant.AddHours(heures), Maintenant));
}

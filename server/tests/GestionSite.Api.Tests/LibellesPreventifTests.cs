using GestionSite.Web.Preventif;
using GestionSite.Web.Session;

namespace GestionSite.Api.Tests;

/// <summary>Libellés du préventif (US-12) : codes de l'API → texte, jamais « conforme », périodes et messages d'action.</summary>
public sealed class LibellesPreventifTests
{
    [Fact]
    public void ADeclaredResultIsNeverShownAsCompliant()
    {
        Assert.Equal("Sans observation", LibellesPreventif.Resultat("Conforme"));
        Assert.Equal("Non satisfaisant", LibellesPreventif.Resultat("NonConforme"));
        Assert.DoesNotContain(LibellesPreventif.Resultats, r => r.Libelle.Contains("onforme", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("VerificationExterieure", "Vérification extérieure")]
    [InlineData("Inconnue", "Inconnue")]
    [InlineData(null, "—")]
    public void UnknownCodesAreShownAsIsAndMissingOnesAsADash(string? code, string attendu) => Assert.Equal(attendu, LibellesPreventif.Nature(code));

    [Fact]
    public void ReasonsBasesAndContractorsHaveLabels()
    {
        Assert.Equal("Suivi ailleurs (contrat, registre papier)", LibellesPreventif.MotifEcart("SuiviAilleurs"));
        Assert.Equal("Inaccessible", LibellesPreventif.MotifSaut("Inaccessible"));
        Assert.Equal("réglementaire", LibellesPreventif.Base("Reglementaire"));
        Assert.Equal("bonne pratique", LibellesPreventif.Base("BonnePratique"));
        Assert.Equal("organisme agréé", LibellesPreventif.Intervenant("OrganismeAgree"));
        Assert.Equal("commission de sécurité", LibellesPreventif.Intervenant("CommissionSecurite"));
        Assert.Equal("Autre", LibellesPreventif.Intervenant("Autre"));
        Assert.Equal("—", LibellesPreventif.Intervenant(null));
    }

    [Theory]
    [InlineData("AVenir", "À venir", "echeance-a-venir")]
    [InlineData("AFaire", "À faire", "echeance-a-faire")]
    [InlineData("EnRetard", "En retard", "echeance-en-retard")]
    [InlineData("RealiseeAvecReserves", "Réalisée avec réserves", "echeance-a-venir")]
    [InlineData("Sautee", "Sautée", "echeance-a-venir")]
    [InlineData(null, "—", "echeance-a-venir")]
    public void DueDateStatesHaveALabelAndAColour(string? etat, string libelle, string classe)
    {
        Assert.Equal(libelle, LibellesPreventif.Etat(etat));
        Assert.Equal(classe, LibellesPreventif.ClasseEtat(etat));
    }

    [Theory]
    [InlineData(1, "Ans", "tous les ans")]
    [InlineData(5, "Ans", "tous les 5 ans")]
    [InlineData(1, "Mois", "tous les mois")]
    [InlineData(6, "Mois", "tous les 6 mois")]
    [InlineData(1, "Semaines", "toutes les semaines")]
    [InlineData(6, "Semaines", "toutes les 6 semaines")]
    public void PeriodsReadNaturally(int valeur, string unite, string attendu) =>
        Assert.Equal(attendu, LibellesPreventif.Periode(new PeriodeApi(valeur, unite)));

    [Fact]
    public void APlanWithoutPeriodIsOneOffAndDatesAreFrench()
    {
        Assert.Equal("ponctuel", LibellesPreventif.Periode(null));
        Assert.Equal("05/03/2027", LibellesPreventif.Date(new DateOnly(2027, 3, 5)));
        Assert.Equal("05/03", LibellesPreventif.DateCourte(new DateOnly(2027, 3, 5)));
        Assert.Equal("Février 2027", LibellesPreventif.Mois(new DateOnly(2027, 2, 1)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("ok", null)]
    [InlineData("cle", "Cette proposition n'existe plus (classement ou composant changé) : la liste est rechargée.")]
    [InlineData("deja_planifiee", "Cette proposition est déjà confirmée : le plan existe.")]
    [InlineData("motif", "Choisissez un motif.")]
    [InlineData("modifie_entre_temps", "Quelqu'un d'autre vient de modifier ce plan : la fiche est rechargée, recommencez.")]
    [InlineData("statut_incompatible", "Cette action n'est plus possible dans l'état actuel du plan.")]
    [InlineData("interdit", "Vous n'avez pas le droit de faire cette action.")]
    [InlineData("introuvable", "Cet élément n'existe plus.")]
    [InlineData("indisponible", "Action refusée ou service momentanément indisponible : réessayez.")]
    public void EachActionOutcomeHasAMessage(string? issue, string? message) => Assert.Equal(message, ActionsPreventifEndpoints.Message(issue));
}

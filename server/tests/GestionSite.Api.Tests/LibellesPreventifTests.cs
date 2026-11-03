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
    [InlineData("Creation", "Plan créé")]
    [InlineData("Modification", "Règle modifiée")]
    [InlineData("Suspension", "Plan suspendu")]
    [InlineData("Reprise", "Plan repris")]
    [InlineData("Affectation", "Échéance affectée")]
    [InlineData("Saut", "Échéance sautée")]
    [InlineData("Realisation", "Contrôle enregistré")]
    [InlineData("Inconnue", "Inconnue")]
    public void EachJournalEventHasALabel(string nature, string libelle) => Assert.Equal(libelle, LibellesPreventif.Journal(nature));

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
        Assert.Equal("05/03/2027", Perimetre.Date(new DateOnly(2027, 3, 5)));
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
    [InlineData("echeance", "Cette échéance n'est plus l'échéance courante : la fiche est rechargée.")]
    public void EachActionOutcomeHasAMessage(string? issue, string? message) => Assert.Equal(message, ActionsPreventifEndpoints.Message(issue));

    [Theory]
    [InlineData("grille_modifiee", null, "points", "La grille de ce corps d'état vient d'être modifiée")]
    [InlineData("commande_reutilisee", null, "resultat", "Cet envoi a déjà servi")]
    [InlineData("composant_retire", null, "resultat", "Ce composant vient d'être retiré")]
    [InlineData("modifie_entre_temps", null, "resultat", "Quelqu'un d'autre vient de modifier cette échéance ou ce lot")]
    [InlineData("statut_incompatible", null, "resultat", "Ce plan est suspendu")]
    [InlineData("donnee_invalide", "echeance", "resultat", "n'est plus l'échéance courante du plan")]
    [InlineData("donnee_invalide", "photos", "resultat", "Photos : 3 au plus")]
    [InlineData("donnee_invalide", "rapport", "resultat", "Rapport : un fichier PDF")]
    [InlineData("donnee_invalide", "realiseLe", "resultat", "Date de réalisation")]
    [InlineData("donnee_invalide", "resultat", "resultat", "exclut « Sans observation »")]
    [InlineData("donnee_invalide", "compteRendu", "resultat", "Compte rendu : obligatoire pour un entretien")]
    [InlineData("donnee_invalide", "prestataire", "debut", "Prestataire : une ligne visible")]
    [InlineData("donnee_invalide", "numeroAgrement", "debut", "N° d'agrément")]
    [InlineData("donnee_invalide", "observations", "debut", "Observations : 50 au plus")]
    [InlineData("donnee_invalide", "releves", "debut", "Relevés : 8 au plus")]
    [InlineData("donnee_invalide", "etat", "etat", "Choisissez l'état constaté.")]
    [InlineData("donnee_invalide", "repartition", "etat", "La somme des quantités")]
    [InlineData("donnee_invalide", "points", "points", "Vérifiez les points et les mesures.")]
    [InlineData("donnee_invalide", "mesures", "points", "Vérifiez les points et les mesures.")]
    [InlineData("donnee_invalide", "grilleVersion", "points", "Vérifiez les points et les mesures.")]
    public void EachRefusalOfAControlSaysWhatToFixAndWhere(string code, string? champ, string etape, string message)
    {
        var (texte, reprise) = LibellesPreventif.RefusRealisation(code, champ, System.Net.HttpStatusCode.UnprocessableEntity);
        Assert.Contains(message, texte, StringComparison.Ordinal);
        Assert.Equal(etape, reprise);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "Ce contrôle ne vous est pas confié")]
    [InlineData(System.Net.HttpStatusCode.NotFound, "Ce plan n'existe plus sur ce site.")]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, "Service momentanément indisponible")]
    public void OtherRefusalsKeepTheCurrentStep(System.Net.HttpStatusCode statut, string message)
    {
        var (texte, reprise) = LibellesPreventif.RefusRealisation(null, null, statut);
        Assert.Contains(message, texte, StringComparison.Ordinal);
        Assert.Null(reprise);
    }

    [Fact]
    public void AGridPointIsNeverShownAsCompliant()
    {
        Assert.Equal("Sans défaut", LibellesPreventif.Point(true, false));
        Assert.Equal("Défaut", LibellesPreventif.Point(false, false));
        Assert.Equal("Défaut, danger signalé", LibellesPreventif.Point(false, true));
        Assert.Equal("Non vérifié", LibellesPreventif.Point(null, false));
    }

    [Fact]
    public void SizesMeasuresAndCasesReadNaturally()
    {
        Assert.Equal("1 Ko", LibellesPreventif.Taille(10));
        Assert.Equal("200 Ko", LibellesPreventif.Taille(204_800));
        Assert.Equal("2,5 Mo", LibellesPreventif.Taille(2_621_440));
        Assert.Equal("48,5 °C", LibellesPreventif.Mesure(48.5m, "°C"));
        Assert.Equal("3", LibellesPreventif.Mesure(3m, ""));
        Assert.Equal("non relevée", LibellesPreventif.Mesure(null, "bar"));
        Assert.Equal("1 dossier ouvert", LibellesPreventif.Ecarts(1, 0));
        Assert.Equal("2 dossiers ouverts, 1 constat rattaché à un dossier existant", LibellesPreventif.Ecarts(2, 1));
        Assert.Equal("3 constats rattachés à un dossier existant", LibellesPreventif.Ecarts(0, 3));
        Assert.Equal("Remarque (facultative)", LibellesPreventif.ChampCompteRendu("InspectionInterne"));
        Assert.Equal("Compte rendu (obligatoire)", LibellesPreventif.ChampCompteRendu("Entretien"));
        Assert.Equal("Compte rendu (facultatif)", LibellesPreventif.ChampCompteRendu("VerificationExterieure"));
    }
}

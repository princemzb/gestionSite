using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>
/// Écrans de terrain du préventif (US-12, S12-07) de bout en bout : contrôle pas à pas sur téléphone (maquette A, même
/// parcours que l'évaluation), vérification extérieure avec rapport PDF, saut d'une échéance, fiche et historique des contrôles.
/// </summary>
public sealed partial class WebInventaireTests
{
    private static readonly string Hier = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task TheAssignedTechnicianInspectsStepByStepAndASecondDefectJoinsTheOpenCase()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose L.", Role.Responsable);
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var communes = await EgliseAsync(technicien);
        Chemin(await PosterAsync(technicien, $"{_page}?action=composant", "composant", ("ComposantSaisi.Nom", "Tableau divisionnaire"),
            ("ComposantSaisi.CorpsEtat", "CourantsForts"), ("ComposantSaisi.ZoneId", communes), ("ComposantSaisi.Verification", "oui"), ("ComposantSaisi.Mode", "individuel")));
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var (fiche, controler) = await PlanConfirmeAsync(responsable, "inspection-interne");
        var avant = await LirePageAsync(responsable, fiche);
        Chemin(await PosterAsync(responsable, fiche, "affectation", ("Affectation.Echeance", EcheanceAffectee().Match(avant).Groups[1].Value),
            ("Affectation.Version", VersionAffectee().Match(avant).Groups[1].Value), ("Affectation.IntervenantCompteId", Technicien().Match(avant).Groups[1].Value)));

        var mesControles = await LirePageAsync(technicien, $"/organisations/{_organisation}/preventif");
        var ficheTechnicien = await LirePageAsync(technicien, fiche);
        var etape1 = await LirePageAsync(technicien, controler);
        var commande = Commande().Match(etape1).Groups[1].Value;
        var points = ChampsControle(commande, "1", "1", "suivant", ("Saisie.Constats[0]", "oui"), ("Saisie.Constats[1]", "oui"), ("Saisie.Constats[2]", "danger"),
            ("Saisie.Constats[3]", ""), ("Saisie.Constats[4]", ""), ("Saisie.Mesures[0]", "48,5"));
        var mesureFausse = await LireAsync(await PosterAsync(technicien, controler, "controle", ChampsControle(commande, "1", "1", "suivant", ("Saisie.Mesures[0]", "chaud"))));
        var etape2 = await LireAsync(await PosterAsync(technicien, controler, "controle", points));
        var sansEtat = await LireAsync(await PosterAsync(technicien, controler, "controle", Etape(points, "2", "suivant")));
        var etape3 = await LireAsync(await PosterAsync(technicien, controler, "controle", [.. Etape(points, "2", "suivant"), ("Saisie.Etat", "Mauvais")]));
        (string Nom, string Valeur)[] final = [.. Etape(points, "3", "enregistrer"), ("Saisie.Etat", "Mauvais"), ("Saisie.RealiseLe", Hier),
            ("Saisie.Resultat", "AvecObservations"), ("Saisie.CompteRendu", "Bornier noirci au départ « Sono »")];
        var retour = await LireAsync(await PosterAsync(technicien, controler, "controle", Etape(final, "3", "retour")));
        var sansPhoto = await LireAsync(await PosterAsync(technicien, controler, "controle", final));
        using var envoi = await EnvoyerFichiersAsync(technicien, controler, "controle", final, Photo());
        var enregistre = await LirePageAsync(technicien, Chemin(envoi));

        Assert.Contains("Mes contrôles · 1", mesControles, StringComparison.Ordinal);
        Assert.Contains(">Contrôler</a>", mesControles, StringComparison.Ordinal);
        Assert.Contains("Historique des contrôles", mesControles, StringComparison.Ordinal);
        Assert.Contains("Faire le contrôle", ficheTechnicien, StringComparison.Ordinal);
        Assert.Contains("Sauter cette échéance", ficheTechnicien, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 1 sur 3", etape1, StringComparison.Ordinal);
        Assert.Contains("Pas de trace d'échauffement (noircissement, odeur)", etape1, StringComparison.Ordinal);
        Assert.Contains("Défaut, danger", etape1, StringComparison.Ordinal);
        Assert.DoesNotContain("Conforme", etape1, StringComparison.Ordinal); // jamais « conforme » (décision du 30/10)
        Assert.Contains("Mesure : un nombre", mesureFausse, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 2 sur 3", etape2, StringComparison.Ordinal);
        Assert.Contains("name=\"Saisie.Constats[2]\" value=\"danger\"", etape2, StringComparison.Ordinal); // étape 1 conservée
        Assert.Contains("Choisissez l'état constaté.", sansEtat, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 3 sur 3", etape3, StringComparison.Ordinal);
        Assert.Contains("obligatoire : état mauvais ou critique", etape3, StringComparison.Ordinal);
        Assert.Contains("État constaté : Mauvais.", etape3, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"Conforme\"", etape3, StringComparison.Ordinal); // un défaut exclut « sans observation »
        Assert.Contains("Un défaut ou une observation exclut « Sans observation »", etape3, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 2 sur 3", retour, StringComparison.Ordinal);
        Assert.Contains("Photos : 3 au plus", sansPhoto, StringComparison.Ordinal);
        Assert.Contains("Contrôle enregistré ; évaluation : 3,0 · Mauvais ; 1 dossier ouvert.", enregistre, StringComparison.Ordinal);
        Assert.Contains("Résultat déclaré : Avec observations", enregistre, StringComparison.Ordinal);
        Assert.Contains("Réalisée avec réserves", enregistre, StringComparison.Ordinal);
        Assert.Contains("Défaut, danger signalé : </span>Pas de trace d'échauffement", enregistre, StringComparison.Ordinal);
        Assert.Contains("Température au point chaud : 48,5 °C", enregistre, StringComparison.Ordinal);
        Assert.Contains("Bornier noirci au départ « Sono »", enregistre, StringComparison.Ordinal);
        Assert.Contains("nouveau dossier · À qualifier · danger signalé", enregistre, StringComparison.Ordinal);
        Assert.Contains("Photo 1 du contrôle", enregistre, StringComparison.Ordinal);

        var photo = PhotoControle().Match(enregistre).Groups[1].Value;
        using var image = await technicien.GetAsync(new Uri(photo, UriKind.Relative));
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);

        // Échéance suivante, non affectée : le responsable la saisit ; le même point en défaut rejoint le dossier ouvert.
        var suivant = await LirePageAsync(responsable, controler);
        var parTechnicien = await LirePageAsync(technicien, controler);
        var second = await LirePageAsync(responsable, Chemin(await PosterAsync(responsable, controler, "controle",
            ChampsControle(Commande().Match(suivant).Groups[1].Value, "1", "3", "enregistrer", ("Saisie.Constats[0]", ""), ("Saisie.Constats[1]", ""),
                ("Saisie.Constats[2]", "non"), ("Saisie.Etat", "Degrade"),
                ("Saisie.RealiseLe", Hier), ("Saisie.Resultat", "AvecObservations")))));
        var historique = await LirePageAsync(responsable, $"/organisations/{_organisation}/preventif/controles");
        var ficheApres = await LirePageAsync(responsable, fiche);

        Assert.Contains("Ce contrôle ne vous est pas confié", parTechnicien, StringComparison.Ordinal);
        Assert.Contains("évaluation : 5,0 · Dégradé ; 1 constat rattaché à un dossier existant", second, StringComparison.Ordinal);
        Assert.Contains("rattaché au dossier déjà ouvert", second, StringComparison.Ordinal);
        Assert.Contains("Contrôles · Site centre", historique, StringComparison.Ordinal);
        Assert.Contains("Inspection · Avec observations · 1 écart · note 3,0", historique, StringComparison.Ordinal);
        Assert.Contains("Inspection · Avec observations · 1 écart · note 5,0", historique, StringComparison.Ordinal);
        Assert.Matches("href=\"/organisations/[^\"]+/preventif/controle\\?site=[^\"]+\"><strong>", ficheApres);
    }

    [Fact]
    public async Task AnExternalVerificationRecordsObservationsAndRequiresItsPdfReport()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose L.", Role.Responsable);
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var communes = await EgliseAsync(technicien);
        Chemin(await PosterAsync(technicien, $"{_page}?action=composant", "composant", ("ComposantSaisi.Nom", "Extincteurs"),
            ("ComposantSaisi.CorpsEtat", "SecuriteIncendie"), ("ComposantSaisi.ZoneId", communes), ("ComposantSaisi.Verification", "oui"), ("ComposantSaisi.Mode", "individuel")));
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var propositions = $"/organisations/{_organisation}/preventif/propositions";
        var avant = await LirePageAsync(responsable, propositions);
        Chemin(await PosterAsync(responsable, propositions, "classement",
            ("Classement.BatimentId", BatimentClasse().Match(avant).Groups[1].Value), ("Classement.Version", VersionClassement().Match(avant).Groups[1].Value),
            ("Classement.Erp", "true"), ("Classement.Type", "V"), ("Classement.Categorie", "3"), ("Classement.Permis", "Non")));
        var (fiche, controler) = await PlanConfirmeAsync(responsable, "incendie-verification");

        var etape1 = await LirePageAsync(responsable, controler);
        var commande = Commande().Match(etape1).Groups[1].Value;
        var plusObservation = await LireAsync(await PosterAsync(responsable, controler, "controle", ChampsControle(commande, "", "1", "ajouter-observation")));
        var plusReleve = await LireAsync(await PosterAsync(responsable, controler, "controle", ChampsControle(commande, "", "1", "ajouter-releve")));
        var saisie = ChampsControle(commande, "", "1", "suivant", ("Saisie.Prestataire", "Bureau Alpha"), ("Saisie.NumeroAgrement", "AG-2026-114"),
            ("Saisie.Observations[0].Libelle", "Extincteur du narthex :\nvérification dépassée"), ("Saisie.Observations[0].ZoneId", communes),
            ("Saisie.Observations[0].Danger", "true"), ("Saisie.Observations[1].Libelle", ""), ("Saisie.Observations[1].ZoneId", communes),
            ("Saisie.Releves[0].Libelle", "Pression"), ("Saisie.Releves[0].Valeur", "15"), ("Saisie.Releves[0].Unite", "bar"));
        var releveFaux = await LireAsync(await PosterAsync(responsable, controler, "controle",
            [.. saisie.Where(c => c.Nom != "Saisie.Releves[0].Valeur"), ("Saisie.Releves[0].Valeur", "haute")]));
        var etape2 = await LireAsync(await PosterAsync(responsable, controler, "controle", saisie));
        (string Nom, string Valeur)[] final = [.. Etape(saisie, "2", "enregistrer"), ("Saisie.RealiseLe", Hier), ("Saisie.Resultat", "NonConforme")];
        var sansRapport = await LireAsync(await PosterAsync(responsable, controler, "controle", final));
        using var envoi = await EnvoyerFichiersAsync(responsable, controler, "controle", final,
            ("rapport", "rapport.pdf", "application/pdf", "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray()));
        var enregistre = await LirePageAsync(responsable, Chemin(envoi));

        Assert.Contains("Contrôler · étape 1 sur 2", etape1, StringComparison.Ordinal);
        Assert.Contains("Observations du rapport", etape1, StringComparison.Ordinal);
        Assert.Contains("Saisie.Observations[1].Libelle", plusObservation, StringComparison.Ordinal);
        Assert.Contains("Saisie.Releves[1].Libelle", plusReleve, StringComparison.Ordinal);
        Assert.Contains("Relevé : un libellé, une valeur", releveFaux, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 2 sur 2", etape2, StringComparison.Ordinal);
        Assert.Contains("obligatoire : vérification réglementaire par un prestataire", etape2, StringComparison.Ordinal);
        Assert.Contains("name=\"Saisie.Observations[0].Danger\" value=\"true\"", etape2, StringComparison.Ordinal); // conservée en champ caché
        Assert.DoesNotContain("value=\"Conforme\"", etape2, StringComparison.Ordinal);
        Assert.Contains("Rapport : un fichier PDF de 10 Mo au plus, obligatoire", sansRapport, StringComparison.Ordinal);
        Assert.Contains("Résultat déclaré : Non satisfaisant", enregistre, StringComparison.Ordinal);
        Assert.Contains("Bureau Alpha", enregistre, StringComparison.Ordinal);
        Assert.Contains("AG-2026-114", enregistre, StringComparison.Ordinal);
        Assert.Contains("Extincteur du narthex :\nvérification dépassée", enregistre, StringComparison.Ordinal);
        Assert.Contains("Église · Parties communes · danger signalé", enregistre, StringComparison.Ordinal);
        Assert.Contains("Pression : 15 bar", enregistre, StringComparison.Ordinal);
        Assert.Contains("Télécharger le rapport PDF", enregistre, StringComparison.Ordinal);
        Assert.Contains("(1 Ko)", enregistre, StringComparison.Ordinal);
        Assert.Contains("1 dossier ouvert", enregistre, StringComparison.Ordinal);
        Assert.Contains("rapport PDF", await LirePageAsync(responsable, fiche), StringComparison.Ordinal);

        using var rapport = await responsable.GetAsync(new Uri(RapportControle().Match(enregistre).Groups[1].Value, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, rapport.StatusCode);
        Assert.Equal("application/pdf", rapport.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", rapport.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("%PDF-", await rapport.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADueDateIsSkippedWithAReasonAndMaintenanceEndsWithAReport()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose L.", Role.Responsable);
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        await EgliseAsync(technicien);
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var (fiche, controler) = await PlanConfirmeAsync(responsable, "cheneaux-nettoyage");
        var avant = await LirePageAsync(responsable, fiche);
        Chemin(await PosterAsync(responsable, fiche, "affectation", ("Affectation.Echeance", EcheanceAffectee().Match(avant).Groups[1].Value),
            ("Affectation.Version", VersionAffectee().Match(avant).Groups[1].Value), ("Affectation.IntervenantCompteId", Technicien().Match(avant).Groups[1].Value)));
        var echeance = EcheanceSaut().Match(await LirePageAsync(technicien, fiche)).Groups[1].Value;

        var sansCommentaire = await LirePageAsync(technicien, await ActionAsync(technicien, fiche, PlanAction(fiche, "sauter"), ("echeance", echeance), ("motif", "Autre")));
        var perimee = await LirePageAsync(technicien, await ActionAsync(technicien, fiche, PlanAction(fiche, "sauter"), ("echeance", "2020-01-01"), ("motif", "Inaccessible")));
        var saute = await LirePageAsync(technicien, await ActionAsync(technicien, fiche, PlanAction(fiche, "sauter"), ("echeance", echeance), ("motif", "Inaccessible")));
        var nonAffecte = await LirePageAsync(technicien, controler);

        Assert.Contains("Commentaire obligatoire pour le motif « Autre »", sansCommentaire, StringComparison.Ordinal);
        Assert.Contains("Cette échéance n'est plus l'échéance courante", perimee, StringComparison.Ordinal);
        Assert.Contains("Échéance sautée · Marc D.", saute, StringComparison.Ordinal);
        Assert.Contains("Sautée · Inaccessible", saute, StringComparison.Ordinal);
        Assert.Contains("Ce contrôle ne vous est pas confié", nonAffecte, StringComparison.Ordinal);
        Assert.DoesNotContain("Faire le contrôle", saute, StringComparison.Ordinal); // l'échéance suivante n'est pas affectée

        var etape1 = await LirePageAsync(responsable, controler);
        var commande = Commande().Match(etape1).Groups[1].Value;
        var releves = ChampsControle(commande, "", "1", "suivant", ("Saisie.Releves[0].Libelle", "Hauteur de dépôt"), ("Saisie.Releves[0].Valeur", "2,5"),
            ("Saisie.Releves[0].Unite", "cm"));
        var etape2 = await LireAsync(await PosterAsync(responsable, controler, "controle", releves));
        (string Nom, string Valeur)[] final = [.. Etape(releves, "2", "enregistrer"), ("Saisie.RealiseLe", Hier), ("Saisie.Resultat", "Conforme")];
        var sansCompteRendu = await LireAsync(await PosterAsync(responsable, controler, "controle", final));
        var dateFuture = await LireAsync(await PosterAsync(responsable, controler, "controle",
            [.. final.Where(c => c.Nom != "Saisie.RealiseLe"), ("Saisie.RealiseLe", "2099-01-01"), ("Saisie.CompteRendu", "Chéneaux dégagés")]));
        var autreEcheance = await LireAsync(await PosterAsync(responsable, controler, "controle",
            [.. final, ("Saisie.CompteRendu", "Chéneaux dégagés"), ("Saisie.Echeance", "2020-10-31")]));
        var premature = await LireAsync(await PosterAsync(responsable, controler, "controle", [.. Etape(final, "1", "enregistrer"), ("Saisie.CompteRendu", "Chéneaux")]));
        Assert.Contains("n'est plus l'échéance courante du plan", autreEcheance, StringComparison.Ordinal); // jamais reportée sur l'échéance suivante
        Assert.Contains("Contrôler · étape 1 sur 2", premature, StringComparison.Ordinal); // rien n'est enregistré avant la dernière étape
        var enregistre = await LirePageAsync(responsable, Chemin(await PosterAsync(responsable, controler, "controle",
            [.. final, ("Saisie.CompteRendu", "Chéneaux dégagés\nDescentes libres")])));

        Assert.Contains("Contrôler · étape 1 sur 2", etape1, StringComparison.Ordinal);
        Assert.Contains("Relevés", etape1, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 2 sur 2", etape2, StringComparison.Ordinal);
        Assert.Contains("value=\"Conforme\"", etape2, StringComparison.Ordinal); // sans défaut ni observation
        Assert.Contains("Sans observation", etape2, StringComparison.Ordinal);
        Assert.Contains("Compte rendu : obligatoire pour un entretien", sansCompteRendu, StringComparison.Ordinal);
        Assert.Contains("Date de réalisation : aujourd'hui ou avant", dateFuture, StringComparison.Ordinal);
        Assert.Contains("Contrôle enregistré.", enregistre, StringComparison.Ordinal);
        Assert.Contains("Résultat déclaré : Sans observation", enregistre, StringComparison.Ordinal);
        Assert.Contains(">Réalisée</span>", enregistre, StringComparison.Ordinal);
        Assert.Contains("Hauteur de dépôt : 2,5 cm", enregistre, StringComparison.Ordinal);
        Assert.Contains("Chéneaux dégagés\nDescentes libres", enregistre, StringComparison.Ordinal);
        Assert.Contains("Aucun défaut ni observation : aucun dossier ouvert.", enregistre, StringComparison.Ordinal);

        using var pasteur = await ConnecterAsync("pasteur@inventaire-web.test");
        Assert.Contains("Vous n'exécutez pas les contrôles de ce site.", await LirePageAsync(pasteur, controler), StringComparison.Ordinal);
        Assert.Contains("Entretien · Sans observation", await LirePageAsync(pasteur, $"/organisations/{_organisation}/preventif/controles"), StringComparison.Ordinal);
        Assert.DoesNotContain("Faire le contrôle", await LirePageAsync(pasteur, fiche), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALotIsInspectedAgainAfterItsGridChangedAndUnavailablePlansAreExplained()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose L.", Role.Responsable);
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var communes = await EgliseAsync(technicien);
        Chemin(await PosterAsync(technicien, $"{_page}?action=composant", "composant", Chaises(communes, quantite: "40", bon: "40", degrade: "", verification: "oui")));
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var (fiche, controler) = await PlanConfirmeAsync(responsable, "inspection-interne");

        var etape1 = await LirePageAsync(responsable, controler);
        var commande = Commande().Match(etape1).Groups[1].Value;
        Assert.Contains("Aucun contrôle enregistré sur ce site.", await LirePageAsync(responsable, $"/organisations/{_organisation}/preventif/controles"), StringComparison.Ordinal);
        var etape2 = await LireAsync(await PosterAsync(responsable, controler, "controle", ChampsControle(commande, "1", "1", "suivant")));
        var mauvaiseSomme = await LireAsync(await PosterAsync(responsable, controler, "controle", ChampsControle(commande, "1", "2", "suivant", ("Saisie.Bon", "30"))));
        Chemin(await PosterAsync(responsable, $"/organisations/{_organisation}/grilles?corps=Mobilier", "grille",
            ("Saisie.Version", "1"), ("Saisie.Action", "enregistrer"), ("Saisie.Points[0]", "Pieds stables"), ("Saisie.Points[1]", "Assise intacte")));
        (string Nom, string Valeur)[] final(string version) => ChampsControle(commande, version, "3", "enregistrer",
            ("Saisie.Constats[0]", "oui"), ("Saisie.Constats[1]", "oui"), ("Saisie.Bon", "40"), ("Saisie.RealiseLe", Hier), ("Saisie.Resultat", "Conforme"));
        var grilleModifiee = await LireAsync(await PosterAsync(responsable, controler, "controle", final("1")));
        var nouvelleCommande = Commande().Match(grilleModifiee).Groups[1].Value;
        var enregistre = await LirePageAsync(responsable, Chemin(await PosterAsync(responsable, controler, "controle",
            [.. final("2").Where(c => c.Nom != "Saisie.CommandeId"), ("Saisie.CommandeId", nouvelleCommande)])));

        Assert.Contains("Combien dans chaque état ?", etape2, StringComparison.Ordinal);
        Assert.Contains("Répartition : 30 pour une quantité de 40.", mauvaiseSomme, StringComparison.Ordinal);
        Assert.Contains("La grille de ce corps d'état vient d'être modifiée", grilleModifiee, StringComparison.Ordinal);
        Assert.Contains("grille v2", grilleModifiee, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 1 sur 3", grilleModifiee, StringComparison.Ordinal);
        Assert.NotEqual(commande, nouvelleCommande);
        Assert.Contains("évaluation : 9,0 · Bon", enregistre, StringComparison.Ordinal);
        Assert.Contains("Répartition : 40 bon sur 40.", await LireAsync(await PosterAsync(responsable, controler, "controle",
            ChampsControle(commande, "2", "2", "suivant", ("Saisie.Bon", "40")))), StringComparison.Ordinal);

        var site = await SiteAsync();
        var inconnu = await LirePageAsync(responsable, $"/organisations/{_organisation}/preventif/controler?site={site}&plan={Guid.NewGuid()}");
        var controleInconnu = await LirePageAsync(responsable, $"/organisations/{_organisation}/preventif/controle?site={site}&controle={Guid.NewGuid()}");
        var version = VersionPlan().Match(await LirePageAsync(responsable, fiche)).Groups[1].Value;
        await ActionAsync(responsable, fiche, PlanAction(fiche, "suspendre"), ("version", version), ("motif", "Chaises prêtées"));
        var suspendu = await LirePageAsync(responsable, controler);

        Assert.Contains("Plan introuvable sur ce site.", inconnu, StringComparison.Ordinal);
        Assert.Contains("Contrôle introuvable sur ce site.", controleInconnu, StringComparison.Ordinal);
        Assert.Contains("Plan suspendu : Chaises prêtées", suspendu, StringComparison.Ordinal);
        Assert.Contains("← Fiche du plan", suspendu, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AZoneInspectionPlannedThroughTheApiIsAssignedThenDoneWithoutAComponentState()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose L.", Role.Responsable);
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var communes = await EgliseAsync(technicien);
        await PlanDeZoneAsync(await SiteAsync(), communes, "CourantsForts");
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var fiche = LienPlan().Match(await LirePageAsync(responsable, $"/organisations/{_organisation}/preventif")).Groups[1].Value;
        var controler = fiche.Replace("/preventif/plan?", "/preventif/controler?", StringComparison.Ordinal);
        var avant = await LirePageAsync(responsable, fiche);
        Chemin(await PosterAsync(responsable, fiche, "affectation", ("Affectation.Echeance", EcheanceAffectee().Match(avant).Groups[1].Value),
            ("Affectation.Version", VersionAffectee().Match(avant).Groups[1].Value), ("Affectation.IntervenantCompteId", Technicien().Match(avant).Groups[1].Value)));

        var mesControles = await LirePageAsync(technicien, $"/organisations/{_organisation}/preventif");
        var etape1 = await LirePageAsync(technicien, controler);
        var points = ChampsControle(Commande().Match(etape1).Groups[1].Value, "1", "1", "suivant", ("Saisie.Constats[0]", "oui"), ("Saisie.Constats[1]", "oui"),
            ("Saisie.Constats[2]", "non"), ("Saisie.Constats[3]", ""), ("Saisie.Constats[4]", ""), ("Saisie.Mesures[0]", "35"));
        var etape2 = await LireAsync(await PosterAsync(technicien, controler, "controle", points));
        var enregistre = await LirePageAsync(technicien, Chemin(await PosterAsync(technicien, controler, "controle",
            [.. Etape(points, "2", "enregistrer"), ("Saisie.RealiseLe", Hier), ("Saisie.Resultat", "AvecObservations"), ("Saisie.CompteRendu", "Prise noircie côté chœur")])));
        var historique = await LirePageAsync(technicien, $"/organisations/{_organisation}/preventif/controles");

        Assert.Contains("Mes contrôles · 1", mesControles, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 1 sur 2", etape1, StringComparison.Ordinal); // zone : pas d'étape « État »
        Assert.Contains("Pas de trace d'échauffement (noircissement, odeur)", etape1, StringComparison.Ordinal);
        Assert.Contains("Contrôler · étape 2 sur 2", etape2, StringComparison.Ordinal);
        Assert.DoesNotContain("Combien dans chaque état ?", etape2, StringComparison.Ordinal);
        Assert.Contains("Contrôle enregistré ; 1 dossier ouvert.", enregistre, StringComparison.Ordinal); // aucune évaluation de composant
        Assert.DoesNotContain("<dt>Évaluation</dt>", enregistre, StringComparison.Ordinal);
        Assert.Contains("Pas de trace d'échauffement (noircissement, odeur) — défaut", enregistre, StringComparison.Ordinal);
        Assert.Contains("Température au point chaud : 35 °C", enregistre, StringComparison.Ordinal);
        Assert.Contains("Prise noircie côté chœur", enregistre, StringComparison.Ordinal);
        Assert.Contains("Inspection · Avec observations · 1 écart", historique, StringComparison.Ordinal);
        Assert.DoesNotContain("· note", historique, StringComparison.Ordinal);
    }

    // Plan d'inspection d'une zone pour un corps d'état, créé par l'API (aucun écran ne le propose) par un responsable du site.
    private async Task PlanDeZoneAsync(string site, string zone, string corpsEtat)
    {
        using var client = _api.CreateClient();
        using var connexion = await client.PostAsJsonAsync("/auth/login", new { email = "responsable@inventaire-web.test", motDePasse = MotDePasse });
        var jeton = (await connexion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jetonAcces").GetString();
        using var demande = new HttpRequestMessage(HttpMethod.Post, new Uri($"/organizations/{_organisation}/sites/{site}/preventive/plans", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                commandeId = Guid.NewGuid(),
                cible = new { type = "ZoneCorpsEtat", zoneId = Guid.Parse(zone), corpsEtat },
                nature = "InspectionInterne",
                libelle = "Inspection électrique des parties communes",
                @base = "BonnePratique",
                periode = new { valeur = 1, unite = "Ans" },
                premiereEcheance = DateOnly.FromDateTime(DateTime.UtcNow),
            }),
        };
        demande.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jeton);
        using var reponse = await client.SendAsync(demande);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    // Bâtiment « Église » créé par le technicien ; renvoie sa zone « Parties communes ».
    private async Task<string> EgliseAsync(HttpClient technicien)
    {
        var batiment = await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        return ZoneCommunes().Match(await LirePageAsync(technicien, Chemin(batiment))).Groups[1].Value;
    }

    // Proposition confirmée par un responsable ; renvoie la fiche du plan (seul plan du site) et l'écran de contrôle.
    private async Task<(string Fiche, string Controler)> PlanConfirmeAsync(HttpClient responsable, string modele)
    {
        var propositions = $"/organisations/{_organisation}/preventif/propositions";
        var liste = await LirePageAsync(responsable, propositions);
        Assert.EndsWith("&issue=ok", await ActionAsync(responsable, propositions, "propositions/confirmer",
            ("cle", Cle(modele, liste)), ("commandeId", Guid.NewGuid().ToString())), StringComparison.Ordinal);
        var fiche = LienPlan().Match(await LirePageAsync(responsable, $"/organisations/{_organisation}/preventif")).Groups[1].Value;
        return (fiche, fiche.Replace("/preventif/plan?", "/preventif/controler?", StringComparison.Ordinal));
    }

    private static (string Nom, string Valeur)[] ChampsControle(string commande, string version, string etape, string action, params (string Nom, string Valeur)[] champs) =>
        [("Saisie.CommandeId", commande), ("Saisie.GrilleVersion", version), ("Saisie.Etape", etape), ("Saisie.Action", action), .. champs];

    private static (string Nom, string Valeur)[] Etape((string Nom, string Valeur)[] champs, string etape, string action) =>
        [.. champs.Where(c => c.Nom is not ("Saisie.Etape" or "Saisie.Action")), ("Saisie.Etape", etape), ("Saisie.Action", action)];

    private static (string Partie, string Nom, string Type, byte[] Octets) Photo()
    {
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 64, 48) { Format = ImageMagick.MagickFormat.Jpeg };
        return ("photos", "constat.jpg", "image/jpeg", image.ToByteArray());
    }

    private static async Task<HttpResponseMessage> EnvoyerFichiersAsync(
        HttpClient navigateur, string page, string formName, (string Nom, string Valeur)[] champs, params (string Partie, string Nom, string Type, byte[] Octets)[] fichiers)
    {
        var html = await LirePageAsync(navigateur, page);
        using var contenu = new MultipartFormDataContent
        {
            { new StringContent(Antifalsification().Match(html).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent(formName), "_handler" },
        };
        foreach (var (nom, valeur) in champs)
        {
            contenu.Add(new StringContent(valeur), nom);
        }

        foreach (var (partie, nom, type, octets) in fichiers)
        {
            var fichier = new ByteArrayContent(octets);
            fichier.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            contenu.Add(fichier, partie, nom);
        }

        return await navigateur.PostAsync(new Uri(page, UriKind.Relative), contenu);
    }

    [GeneratedRegex("<img src=\"(/organisations/[^\"]+/preventif/controles/[^\"]+/photos/1)\"")]
    private static partial Regex PhotoControle();

    [GeneratedRegex("<a href=\"(/organisations/[^\"]+/preventif/controles/[^\"]+/rapport)\">Télécharger le rapport PDF</a>")]
    private static partial Regex RapportControle();

    [GeneratedRegex("name=\"echeance\" value=\"([0-9-]{10})\"")]
    private static partial Regex EcheanceSaut();
}

using System.Net;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>
/// Écrans du préventif (US-12, S12-06) de bout en bout : plans proposés à confirmer, calendrier mensuel des échéances
/// (maquette B), fiche du plan avec affectation et suspension.
/// </summary>
public sealed partial class WebInventaireTests
{
    [Fact]
    public async Task AManagerDeclaresTheClassificationThenConfirmsOrDismissesEachProposedPlan()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose L.", Role.Responsable);
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        Chemin(await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église")));
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var propositions = $"/organisations/{_organisation}/preventif/propositions";

        var avant = await LirePageAsync(responsable, propositions);
        var classe = await LirePageAsync(responsable, Chemin(await PosterAsync(responsable, propositions, "classement",
            ("Classement.BatimentId", BatimentClasse().Match(avant).Groups[1].Value), ("Classement.Version", VersionClassement().Match(avant).Groups[1].Value),
            ("Classement.Erp", "true"), ("Classement.Type", "V"), ("Classement.Categorie", "3"), ("Classement.Permis", "Inconnu"))));

        Assert.Contains("Plans proposés · Site centre", avant, StringComparison.Ordinal);
        Assert.Contains("Nettoyage des chéneaux, gouttières et descentes avant l'hiver", avant, StringComparison.Ordinal);
        Assert.DoesNotContain("Visite périodique de la commission de sécurité", avant, StringComparison.Ordinal);
        Assert.Contains("Visite périodique de la commission de sécurité", classe, StringComparison.Ordinal);
        Assert.Contains("Vérification extérieure · tous les 5 ans · commission de sécurité · réglementaire, à confirmer", classe, StringComparison.Ordinal);
        Assert.Contains("Maintenance du défibrillateur (DAE)", classe, StringComparison.Ordinal);
        Assert.Contains("<option value=\"3\" selected", classe, StringComparison.Ordinal);
        Assert.DoesNotContain("Conforme", classe, StringComparison.Ordinal); // seul l'avertissement « jamais conforme » le mentionne

        var confirme = await ActionAsync(responsable, propositions, "propositions/confirmer",
            ("cle", Cle("registre-securite", classe)), ("commandeId", Guid.NewGuid().ToString()));
        var sansMotif = await ActionAsync(responsable, propositions, "propositions/ecarter", ("cle", Cle("cheneaux-nettoyage", classe)), ("motif", "Autre"));
        var ecarte = await ActionAsync(responsable, propositions, "propositions/ecarter",
            ("cle", Cle("cheneaux-nettoyage", classe)), ("motif", "SuiviAilleurs"), ("commentaire", "Contrat du couvreur"));
        var apres = await LirePageAsync(responsable, ecarte);

        Assert.EndsWith("&issue=ok", confirme, StringComparison.Ordinal);
        Assert.Contains("Commentaire obligatoire pour le motif « Autre »", await LirePageAsync(responsable, sansMotif), StringComparison.Ordinal);
        Assert.DoesNotContain("Revue du registre de sécurité", apres, StringComparison.Ordinal); // confirmée : le plan existe
        Assert.Contains("Nettoyage des chéneaux", apres, StringComparison.Ordinal); // écartée : reste visible avec son motif
        Assert.Contains("par Rose L. : « Suivi ailleurs (contrat, registre papier) — Contrat du couvreur »", apres, StringComparison.Ordinal);
        Assert.Contains("Revoir et confirmer", apres, StringComparison.Ordinal);

        using var pasteur = await ConnecterAsync("pasteur@inventaire-web.test");
        var lecture = await LirePageAsync(pasteur, propositions);
        Assert.Contains("ERP de type V, 3e catégorie", lecture, StringComparison.Ordinal);
        Assert.DoesNotContain("Écarter la proposition", lecture, StringComparison.Ordinal);
        Assert.DoesNotContain("Enregistrer le classement", lecture, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMonthlyCalendarShowsDueDatesAndTheManagerAssignsThenSuspendsAPlan()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose L.", Role.Responsable);
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        Chemin(await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église")));
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var propositions = await LirePageAsync(responsable, $"/organisations/{_organisation}/preventif/propositions");
        Assert.EndsWith("&issue=ok", await ActionAsync(responsable, $"/organisations/{_organisation}/preventif/propositions", "propositions/confirmer",
            ("cle", Cle("cheneaux-nettoyage", propositions)), ("commandeId", Guid.NewGuid().ToString())), StringComparison.Ordinal);
        var preventif = $"/organisations/{_organisation}/preventif";

        var calendrier = await LirePageAsync(responsable, preventif);
        var fiche = LienPlan().Match(calendrier).Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);
        var avantAffectation = await LirePageAsync(responsable, fiche);
        var affecte = await LirePageAsync(responsable, Chemin(await PosterAsync(responsable, fiche, "affectation",
            ("Affectation.Echeance", EcheanceAffectee().Match(avantAffectation).Groups[1].Value),
            ("Affectation.Version", VersionAffectee().Match(avantAffectation).Groups[1].Value),
            ("Affectation.IntervenantCompteId", Technicien().Match(avantAffectation).Groups[1].Value))));
        var mesControles = await LirePageAsync(technicien, preventif);
        var ficheTechnicien = await LirePageAsync(technicien, fiche);
        var fevrier = await LirePageAsync(responsable, $"{preventif}?mois=2027-02");
        var echeance = DateOnly.ParseExact(EcheanceAffectee().Match(avantAffectation).Groups[1].Value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var moisEcheance = await LirePageAsync(responsable, $"{preventif}?mois={echeance:yyyy-MM}");
        var anSuivant = await LirePageAsync(responsable, $"{preventif}?mois={echeance.AddYears(1):yyyy-MM}");

        Assert.Matches("class=\"echeance echeance-(a-venir|a-faire|en-retard)\" href=\"[^\"]+\">\\s*<span class=\"visuellement-masque\">(À venir|À faire|En retard) : </span>Nettoyage des chéneaux", moisEcheance);
        Assert.Matches("class=\"echeance echeance-a-venir echeance-prevue\" href=\"[^\"]+\">\\s*<span class=\"visuellement-masque\">Prévue : </span>Nettoyage des chéneaux", anSuivant);

        Assert.Contains("Préventif · Site centre", calendrier, StringComparison.Ordinal);
        Assert.Contains("Plans proposés · ", calendrier, StringComparison.Ordinal);
        Assert.Contains("Tous les plans · 1", calendrier, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Mois suivant\"", calendrier, StringComparison.Ordinal);
        Assert.Contains("Février 2027", fevrier, StringComparison.Ordinal);
        Assert.Contains("Nettoyage des chéneaux, gouttières et descentes avant l'hiver", avantAffectation, StringComparison.Ordinal);
        Assert.Contains("Prochaine échéance : ", avantAffectation, StringComparison.Ordinal);
        Assert.Contains("tous les ans, date fixe", avantAffectation, StringComparison.Ordinal);
        Assert.Contains("Affectée à Marc D.", affecte, StringComparison.Ordinal);
        Assert.Contains("Échéance affectée · Rose L.", affecte, StringComparison.Ordinal);
        Assert.Contains("Mes contrôles · 1", mesControles, StringComparison.Ordinal);
        Assert.DoesNotContain("Affecter l'échéance", ficheTechnicien, StringComparison.Ordinal);
        Assert.DoesNotContain("Suspendre le plan", ficheTechnicien, StringComparison.Ordinal);

        var sansMotif = await ActionAsync(responsable, fiche, PlanAction(fiche, "suspendre"), ("version", VersionPlan().Match(affecte).Groups[1].Value));
        var suspendu = await LirePageAsync(responsable, await ActionAsync(responsable, fiche, PlanAction(fiche, "suspendre"),
            ("version", VersionPlan().Match(affecte).Groups[1].Value), ("motif", "Couverture en travaux")));
        var repris = await LirePageAsync(responsable, await ActionAsync(responsable, fiche, PlanAction(fiche, "reprendre"),
            ("version", VersionPlan().Match(suspendu).Groups[1].Value)));

        Assert.DoesNotContain("&issue=ok", sansMotif, StringComparison.Ordinal);
        Assert.Contains("Plan suspendu : Couverture en travaux", suspendu, StringComparison.Ordinal);
        Assert.Contains("Reprendre le plan", suspendu, StringComparison.Ordinal);
        Assert.Contains("Plan repris · Rose L.", repris, StringComparison.Ordinal);
        Assert.Contains("Prochaine échéance : ", repris, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APlanOfAnotherSiteOrAForgedActionIsRefused()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var site = await SiteAsync();
        var inconnu = await LirePageAsync(technicien, $"/organisations/{_organisation}/preventif/plan?site={site}&plan={Guid.NewGuid()}");
        using var sansJeton = new FormUrlEncodedContent([new("cle", "registre-securite:batiment:0"), new("commandeId", Guid.NewGuid().ToString())]);
        using var forge = await technicien.PostAsync(
            new Uri($"/organisations/{_organisation}/sites/{site}/preventif/propositions/confirmer", UriKind.Relative), sansJeton);
        var refuse = await ActionAsync(technicien, $"/organisations/{_organisation}/preventif/propositions?site={site}", "propositions/confirmer",
            ("cle", "registre-securite:batiment:0"), ("commandeId", Guid.NewGuid().ToString()));

        Assert.Contains("Plan introuvable sur ce site.", inconnu, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, forge.StatusCode);
        Assert.EndsWith("&issue=interdit", refuse, StringComparison.Ordinal); // le technicien ne planifie pas
    }

    // Poste un formulaire d'action (un clic) avec le jeton de la page ; renvoie l'adresse de retour (avec l'issue).
    private async Task<string> ActionAsync(HttpClient navigateur, string page, string action, params (string Nom, string Valeur)[] champs)
    {
        var html = await LirePageAsync(navigateur, page.Contains('?', StringComparison.Ordinal) ? page : $"{page}?site={await SiteAsync()}");
        var cible = action.StartsWith('/') ? action : $"/organisations/{_organisation}/sites/{await SiteAsync()}/preventif/{action}";
        using var contenu = new FormUrlEncodedContent(
            [new("__RequestVerificationToken", Antifalsification().Match(html).Groups[1].Value), .. champs.Select(c => new KeyValuePair<string, string>(c.Nom, c.Valeur))]);
        using var reponse = await navigateur.PostAsync(new Uri(cible, UriKind.Relative), contenu);
        return Chemin(reponse);
    }

    private string PlanAction(string fiche, string action) =>
        $"/organisations/{_organisation}/sites/{Regex.Match(fiche, "site=([0-9a-f-]{36})").Groups[1].Value}/preventif/plans/{Regex.Match(fiche, "plan=([0-9a-f-]{36})").Groups[1].Value}/{action}";

    private static string Cle(string modele, string page)
    {
        var cle = Regex.Match(page, $"name=\"cle\" value=\"({Regex.Escape(modele)}:[^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(cle), $"Proposition « {modele} » absente.");
        return cle;
    }

    [GeneratedRegex("name=\"Classement.BatimentId\" value=\"([0-9a-f-]{36})\"")]
    private static partial Regex BatimentClasse();

    [GeneratedRegex("name=\"Classement.Version\" value=\"(\\d+)\"")]
    private static partial Regex VersionClassement();

    [GeneratedRegex("href=\"(/organisations/[^\"]+/preventif/plan\\?site=[^\"#]+)\"")]
    private static partial Regex LienPlan();

    [GeneratedRegex("name=\"Affectation.Echeance\" value=\"([0-9-]{10})\"")]
    private static partial Regex EcheanceAffectee();

    [GeneratedRegex("name=\"Affectation.Version\" value=\"(\\d+)\"")]
    private static partial Regex VersionAffectee();

    [GeneratedRegex("<option value=\"([0-9a-f-]{36})\">Marc D.</option>")]
    private static partial Regex Technicien();

    [GeneratedRegex("name=\"version\" value=\"(\\d+)\"")]
    private static partial Regex VersionPlan();
}

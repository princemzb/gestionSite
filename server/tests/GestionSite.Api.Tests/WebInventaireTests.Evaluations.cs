using System.Text.RegularExpressions;

namespace GestionSite.Api.Tests;

/// <summary>Évaluations et état du site (US-11, S11-04) de bout en bout : maquettes A (tableau du site, évaluation pas à pas).</summary>
public sealed partial class WebInventaireTests
{
    [Fact]
    public async Task ATechnicianEvaluatesStepByStepAndACriticalStateProposesACase()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var (communes, tableau, _) = await SiteEvalueAsync(technicien);
        var evaluer = $"{_page}/evaluer?site={await SiteAsync()}&composant={tableau}";

        var etape1 = await LirePageAsync(technicien, evaluer);
        var commande = Commande().Match(etape1).Groups[1].Value;
        var points = ChampsEvaluation(commande, etape: "1", action: "suivant", ("Saisie.Constats[0]", "oui"), ("Saisie.Constats[1]", "oui"),
            ("Saisie.Constats[2]", "non"), ("Saisie.Constats[3]", ""), ("Saisie.Constats[4]", ""), ("Saisie.Mesures[0]", "48,5"));
        var mesureFausse = await LireAsync(await PosterAsync(technicien, evaluer, "evaluation",
            ChampsEvaluation(commande, etape: "1", action: "suivant", ("Saisie.Mesures[0]", "beaucoup"))));
        var etape2 = await LireAsync(await PosterAsync(technicien, evaluer, "evaluation", points));
        var etape3 = await LireAsync(await PosterAsync(technicien, evaluer, "evaluation", [.. points.Where(c => c.Nom != "Saisie.Etape" && c.Nom != "Saisie.Action"),
            ("Saisie.Etape", "2"), ("Saisie.Action", "suivant"), ("Saisie.Etat", "Critique")]));
        (string Nom, string Valeur)[] final = [.. points.Where(c => c.Nom != "Saisie.Etape" && c.Nom != "Saisie.Action"),
            ("Saisie.Etape", "3"), ("Saisie.Action", "enregistrer"), ("Saisie.Etat", "Critique"), ("Saisie.Remarque", "Odeur de brûlé au départ « Sono »")];
        var sansPhoto = await LireAsync(await PosterAsync(technicien, evaluer, "evaluation", final));
        using var envoi = await EnvoyerAvecPhotoAsync(technicien, evaluer, "evaluation", final);
        var fiche = await LirePageAsync(technicien, Chemin(envoi));

        Assert.Contains("Évaluer · étape 1 sur 3", etape1, StringComparison.Ordinal);
        Assert.Contains("Pas de trace d'échauffement (noircissement, odeur)", etape1, StringComparison.Ordinal);
        Assert.Contains("Température au point chaud", etape1, StringComparison.Ordinal);
        Assert.Contains("Mesure : un nombre", mesureFausse, StringComparison.Ordinal);
        Assert.Contains("Évaluer · étape 2 sur 3", etape2, StringComparison.Ordinal);
        Assert.Contains("name=\"Saisie.Mesures[0]\" value=\"48,5\"", etape2, StringComparison.Ordinal); // étape 1 conservée
        Assert.Contains("obligatoire : état mauvais ou critique", etape3, StringComparison.Ordinal);
        Assert.Contains("Photo obligatoire pour un état mauvais ou critique", sansPhoto, StringComparison.Ordinal);
        Assert.Contains("Évaluation enregistrée : 1,0 · Critique.", fiche, StringComparison.Ordinal);
        Assert.Contains("Ouvrir un dossier prérempli", fiche, StringComparison.Ordinal);
        Assert.Contains("Défaut : </span>Pas de trace d'échauffement", fiche, StringComparison.Ordinal); // libellé pour lecteur d'écran
        Assert.True(etape1.IndexOf("value=\"suivant\"", StringComparison.Ordinal) < etape1.IndexOf("Annuler", StringComparison.Ordinal)); // Entrée avance
        Assert.Contains("Température au point chaud : 48,5 °C", fiche, StringComparison.Ordinal);
        Assert.Contains("Photo 1 de l'évaluation", fiche, StringComparison.Ordinal);

        var photo = PhotoEvaluation().Match(fiche).Groups[1].Value;
        using var image = await technicien.GetAsync(new Uri(photo, UriKind.Relative));
        var declaration = await LirePageAsync(technicien, LienDeclaration().Match(fiche).Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal));

        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"<option value=\"{communes}\" selected", declaration, StringComparison.Ordinal);
        Assert.Contains("Tableau divisionnaire : évalué « Critique »", declaration, StringComparison.Ordinal);
        Assert.Contains("Odeur de brûlé au départ « Sono »", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALotIsSplitByStateAndTheSiteDashboardShowsTheWorstZoneFirst()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var (_, tableau, chaises) = await SiteEvalueAsync(technicien);
        var site = await SiteAsync();
        var evaluer = $"{_page}/evaluer?site={site}&composant={chaises}";
        var commande = Commande().Match(await LirePageAsync(technicien, evaluer)).Groups[1].Value;
        (string Nom, string Valeur)[] Lot(string etape, string action, string bon) =>
            ChampsEvaluation(commande, etape, action, ("Saisie.Bon", bon), ("Saisie.Degrade", "8"));

        var mauvaiseSomme = await LireAsync(await PosterAsync(technicien, evaluer, "evaluation", Lot("2", "suivant", "22")));
        var retour = await LireAsync(await PosterAsync(technicien, evaluer, "evaluation", Lot("2", "retour", "32")));
        var fiche = await LirePageAsync(technicien, Chemin(await PosterAsync(technicien, evaluer, "evaluation", Lot("3", "enregistrer", "32"))));
        var etat = await LirePageAsync(technicien, $"/organisations/{_organisation}/etat?site={site}");
        using var pasteur = await ConnecterAsync("pasteur@inventaire-web.test");
        var etatPasteur = await LirePageAsync(pasteur, $"/organisations/{_organisation}/etat");
        var evaluerPasteur = await LirePageAsync(pasteur, $"{_page}/evaluer?site={site}&composant={tableau}");

        Assert.Contains("Répartition : 30 pour une quantité de 40.", mauvaiseSomme, StringComparison.Ordinal);
        Assert.Contains("Évaluer · étape 1 sur 3", retour, StringComparison.Ordinal);
        Assert.Contains("Évaluation enregistrée : 8,2 · Bon.", fiche, StringComparison.Ordinal); // (32×9 + 8×5) / 40
        Assert.Contains("Évaluation : bon 40 → 32, dégradé 0 → 8", fiche, StringComparison.Ordinal);
        Assert.DoesNotContain("Ouvrir un dossier prérempli", fiche, StringComparison.Ordinal);
        Assert.Contains("État du site · Site centre", etat, StringComparison.Ordinal);
        Assert.Contains("1 composant évalué sur 3", etat, StringComparison.Ordinal);
        Assert.Contains("Aucun composant en état critique", etat, StringComparison.Ordinal);
        Assert.Contains("Parties communes", etat, StringComparison.Ordinal);
        Assert.Contains("2 composants · 50 % évalués", etat, StringComparison.Ordinal); // tableau et chaises ; l'armoire est en sacristie
        Assert.True(etat.IndexOf("Parties communes", StringComparison.Ordinal) < etat.IndexOf("Sacristie", StringComparison.Ordinal)); // notée avant non notée
        Assert.Contains("État du site", etatPasteur, StringComparison.Ordinal);
        Assert.Contains("Vous n'évaluez pas les composants de ce site.", evaluerPasteur, StringComparison.Ordinal);
    }

    // Bâtiment « Église » (Parties communes), zone « Sacristie » vide d'évaluation, tableau divisionnaire, 40 chaises « bon »
    // et une armoire en sacristie.
    private async Task<(string Communes, string Tableau, string Chaises)> SiteEvalueAsync(HttpClient technicien)
    {
        var batiment = await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var communes = ZoneCommunes().Match(await LirePageAsync(technicien, Chemin(batiment))).Groups[1].Value;
        var idBatiment = Batiment().Match(await LirePageAsync(technicien, _page)).Groups[1].Value;
        var sacristie = Chemin(await PosterAsync(technicien, $"{_page}?action=zone&pour={idBatiment}", "structure",
            ("StructureSaisie.Type", "zone"), ("StructureSaisie.BatimentId", idBatiment), ("StructureSaisie.Nom", "Sacristie")));
        var zoneSacristie = Regex.Match(sacristie, "zone=([0-9a-f-]{36})").Groups[1].Value;
        var tableau = Chemin(await PosterAsync(technicien, $"{_page}?action=composant", "composant",
            ("ComposantSaisi.Nom", "Tableau divisionnaire"), ("ComposantSaisi.CorpsEtat", "CourantsForts"), ("ComposantSaisi.ZoneId", communes),
            ("ComposantSaisi.Mode", "individuel")));
        var chaises = Chemin(await PosterAsync(technicien, $"{_page}?action=composant", "composant", Chaises(communes, quantite: "40", bon: "40", degrade: "")));
        Chemin(await PosterAsync(technicien, $"{_page}?action=composant", "composant",
            ("ComposantSaisi.Nom", "Armoire"), ("ComposantSaisi.CorpsEtat", "Mobilier"), ("ComposantSaisi.ZoneId", zoneSacristie), ("ComposantSaisi.Mode", "individuel")));
        return (communes, Identifiant().Match(tableau).Groups[1].Value, Identifiant().Match(chaises).Groups[1].Value);
    }

    private async Task<string> SiteAsync()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        return SiteDuLien().Match(await LirePageAsync(technicien, _page)).Groups[1].Value;
    }

    private static (string Nom, string Valeur)[] ChampsEvaluation(string commande, string etape, string action, params (string Nom, string Valeur)[] champs) =>
        [("Saisie.CommandeId", commande), ("Saisie.GrilleVersion", "1"), ("Saisie.Etape", etape), ("Saisie.Action", action), .. champs];

    private static async Task<HttpResponseMessage> EnvoyerAvecPhotoAsync(HttpClient navigateur, string page, string formName, (string Nom, string Valeur)[] champs)
    {
        var html = await LirePageAsync(navigateur, page);
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 64, 48) { Format = ImageMagick.MagickFormat.Jpeg };
        using var contenu = new MultipartFormDataContent
        {
            { new StringContent(Antifalsification().Match(html).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent(formName), "_handler" },
        };
        foreach (var (nom, valeur) in champs)
        {
            contenu.Add(new StringContent(valeur), nom);
        }

        var photo = new ByteArrayContent(image.ToByteArray());
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        contenu.Add(photo, "photos", "constat.jpg");
        return await navigateur.PostAsync(new Uri(page, UriKind.Relative), contenu);
    }

    [GeneratedRegex("name=\"Saisie.CommandeId\" value=\"([0-9a-f-]{36})\"")]
    private static partial Regex Commande();

    [GeneratedRegex("<img src=\"(/organisations/[^\"]+/evaluations/[^\"]+/photos/1)\"")]
    private static partial Regex PhotoEvaluation();

    [GeneratedRegex("<a class=\"bouton-lien\" href=\"([^\"]+)\">Ouvrir un dossier prérempli</a>")]
    private static partial Regex LienDeclaration();

    [GeneratedRegex("inventaire\\?site=([0-9a-f-]{36})")]
    private static partial Regex SiteDuLien();
}

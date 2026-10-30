using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>Écran des grilles d'évaluation (US-11, S11-05, maquette A) de bout en bout : liste et éditeur ligne à ligne.</summary>
public sealed partial class WebInventaireTests
{
    [Fact]
    public async Task AManagerReordersAddsAndSavesANewGridVersion()
    {
        await MembreAsync("responsable@inventaire-web.test", "Rose R.", Role.Responsable);
        using var responsable = await ConnecterAsync("responsable@inventaire-web.test");
        var page = $"/organisations/{_organisation}/grilles?corps=Mobilier";
        (string Nom, string Valeur)[] Points(string action, params string[] points) =>
            [("Saisie.Version", "1"), ("Saisie.Action", action), .. points.Select((p, i) => ($"Saisie.Points[{i}]", p))];

        var liste = await LirePageAsync(responsable, page);
        var ajout = await LireAsync(await PosterAsync(responsable, page, "grille",
            Points("ajouter", "Assise et dossier sans fissure ni écharde", "Pieds stables, patins présents", "Empilage ou rangement sans déformation")));
        var monte = await LireAsync(await PosterAsync(responsable, page, "grille", Points("monter:1", "Assise intacte", "Pieds stables")));
        var doublon = await LireAsync(await PosterAsync(responsable, page, "grille", Points("enregistrer", "Pieds stables", "pieds stables")));
        var enregistree = await LirePageAsync(responsable, Chemin(await PosterAsync(responsable, page, "grille",
            [.. Points("enregistrer", "Pieds stables", "Assise intacte", "Housse propre"), ("Saisie.Libelles[0]", "Hauteur d'assise"), ("Saisie.Unites[0]", "cm")])));
        var defaut = await LirePageAsync(responsable, $"{page}&defaut=true");
        var inchangee = await LireAsync(await PosterAsync(responsable, page, "grille",
            [("Saisie.Version", "2"), ("Saisie.Action", "enregistrer"), ("Saisie.Points[0]", "Pieds stables"), ("Saisie.Points[1]", "Assise intacte"),
                ("Saisie.Points[2]", "Housse propre"), ("Saisie.Libelles[0]", "Hauteur d'assise"), ("Saisie.Unites[0]", "cm")]));

        Assert.Contains("Grilles d'évaluation · Site centre", liste, StringComparison.Ordinal);
        Assert.Contains("Grille par défaut (version 1)", liste, StringComparison.Ordinal);
        Assert.Contains(">Grilles d'évaluation</a>", liste, StringComparison.Ordinal); // entrée de navigation de R/D
        Assert.Contains("name=\"Saisie.Points[3]\" value=\"\"", ajout, StringComparison.Ordinal);
        Assert.True(monte.IndexOf("value=\"Pieds stables\"", StringComparison.Ordinal) < monte.IndexOf("value=\"Assise intacte\"", StringComparison.Ordinal));
        Assert.Contains("Points à observer : de 1 à 15, tous différents", doublon, StringComparison.Ordinal);
        Assert.Contains("Version 2 enregistrée", enregistree, StringComparison.Ordinal);
        Assert.Contains("v2 adaptée", enregistree, StringComparison.Ordinal);
        Assert.Contains("adaptée le ", enregistree, StringComparison.Ordinal);
        Assert.Contains("par Rose R.", enregistree, StringComparison.Ordinal);
        Assert.Contains("value=\"Hauteur d'assise\"", enregistree, StringComparison.Ordinal);
        Assert.Contains("value=\"Empilage ou rangement sans déformation\"", defaut, StringComparison.Ordinal); // contenu par défaut rechargé
        Assert.Contains("Enregistrer la version 3", defaut, StringComparison.Ordinal);
        Assert.Contains("Aucune modification : la version 2 reste en vigueur.", inchangee, StringComparison.Ordinal);
        Assert.True(liste.IndexOf("value=\"enregistrer\"", StringComparison.Ordinal) < liste.IndexOf("value=\"ajouter\"", StringComparison.Ordinal)); // Entrée enregistre
    }

    [Fact]
    public async Task TheEditorMovesRemovesAndExplainsRefusals()
    {
        await MembreAsync("delegue@inventaire-web.test", "Denis D.", Role.Delegue);
        using var delegue = await ConnecterAsync("delegue@inventaire-web.test");
        var page = $"/organisations/{_organisation}/grilles?corps=Gaz";
        (string Nom, string Valeur)[] Champs(string version, string action, params (string Nom, string Valeur)[] autres) =>
            [("Saisie.Version", version), ("Saisie.Action", action), ("Saisie.Points[0]", "Aucune odeur de gaz"), ("Saisie.Points[1]", "Robinet accessible"), .. autres];

        var descendu = await LireAsync(await PosterAsync(delegue, page, "grille", Champs("1", "descendre:0")));
        var supprime = await LireAsync(await PosterAsync(delegue, page, "grille", Champs("1", "supprimer:1")));
        var mesure = await LireAsync(await PosterAsync(delegue, page, "grille", Champs("1", "ajouter-mesure", ("Saisie.Libelles[0]", "Pression"), ("Saisie.Unites[0]", "mbar"))));
        var sansMesure = await LireAsync(await PosterAsync(delegue, page, "grille", Champs("1", "supprimer-mesure:0", ("Saisie.Libelles[0]", "Pression"), ("Saisie.Unites[0]", "mbar"))));
        var sansUnite = await LireAsync(await PosterAsync(delegue, page, "grille", Champs("1", "enregistrer", ("Saisie.Libelles[0]", "Pression"), ("Saisie.Unites[0]", ""))));
        Chemin(await PosterAsync(delegue, page, "grille", Champs("1", "enregistrer")));
        var perimee = await LireAsync(await PosterAsync(delegue, page, "grille", Champs("1", "enregistrer", ("Saisie.Points[2]", "Flexibles à jour"))));
        var inconnue = await LireAsync(await PosterAsync(delegue, page, "grille", Champs("2", "inconnue")));

        Assert.True(descendu.IndexOf("value=\"Robinet accessible\"", StringComparison.Ordinal) < descendu.IndexOf("value=\"Aucune odeur de gaz\"", StringComparison.Ordinal));
        Assert.DoesNotContain("value=\"Robinet accessible\"", supprime, StringComparison.Ordinal);
        Assert.Contains("name=\"Saisie.Libelles[1]\" value=\"\"", mesure, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"Pression\"", sansMesure, StringComparison.Ordinal);
        Assert.Contains("Mesures : 8 au plus, toutes différentes, chacune avec un libellé", sansUnite, StringComparison.Ordinal);
        Assert.Contains("Quelqu'un vient de modifier cette grille", perimee, StringComparison.Ordinal);
        Assert.Contains("value=\"Robinet accessible\"", inconnue, StringComparison.Ordinal); // action inconnue : saisie inchangée
    }

    [Fact]
    public async Task ATechnicianReadsGridsWithoutEditing()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");

        var page = await LirePageAsync(technicien, $"/organisations/{_organisation}/grilles?corps=CourantsForts");
        var inventaire = await LirePageAsync(technicien, _page);

        Assert.Contains("Bouton test du différentiel 30 mA : déclenche", page, StringComparison.Ordinal);
        Assert.Contains("Seuls le responsable et les délégués du site adaptent les grilles.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Saisie.Points", page, StringComparison.Ordinal);
        Assert.DoesNotContain(">Grilles d'évaluation</a>", inventaire, StringComparison.Ordinal);
    }
}

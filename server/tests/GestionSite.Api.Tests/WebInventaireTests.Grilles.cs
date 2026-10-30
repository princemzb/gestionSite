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
        var defaut = await LireAsync(await PosterAsync(responsable, page, "grille", [("Saisie.Version", "2"), ("Saisie.Action", "defaut")]));

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

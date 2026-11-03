using System.Text.RegularExpressions;
using GestionSite.Web.Aide;

namespace GestionSite.Api.Tests;

/// <summary>
/// Aide intégrée (DOC-02) sans infrastructure : manuel embarqué, correspondance écrans → ancres, rendu sûr du Markdown,
/// recherche et livret.
/// </summary>
public sealed partial class AideIntegreeTests
{
    private static readonly ManuelAide Manuel = ManuelAide.Charger();

    // Pages sans aide d'écran : accueil provisoire, affiche à imprimer, formulaire public du fidèle (sans compte), pages d'aide.
    private static readonly string[] PagesDispensees = ["Accueil", "Affiche", "Choix", "Signaler", "Aide", "AideChapitre"];

    [Fact]
    public void TheEmbeddedManualHasEveryChapterAnchorAndCapture()
    {
        Assert.Equal(13, Manuel.Chapitres.Count);
        Assert.Equal("00-sommaire", Manuel.Chapitres[0].Nom);
        Assert.Equal(("05-dossiers", 5, "Dossiers", "dossiers"),
            (Manuel.Chapitres[5].Nom, Manuel.Chapitres[5].Numero, Manuel.Chapitres[5].Titre, Manuel.Chapitres[5].Ancre));
        Assert.True(Manuel.Sections.Count >= 81, $"{Manuel.Sections.Count} sections");
        Assert.Equal(Directory.GetFiles(Path.Combine(Depot(), "docs", "manuel", "images"), "*.png").Length, Manuel.NomsImages.Count);
        Assert.NotNull(Manuel.Edition);
        Assert.NotNull(Manuel.Image("connexion.png"));
        Assert.Null(Manuel.Image("../appsettings.json"));
    }

    [Fact]
    public void EveryAnchorCitedByAScreenOrARoleExistsInTheManual()
    {
        var citees = EcransAide.Ecrans.Values.SelectMany(e => e.Autres.Prepend(e.Ancre))
            .Concat(EcransAide.Roles.SelectMany(r => r.Ancres)).Append("manuel-a-venir").Distinct().ToList();

        Assert.All(citees, ancre => Assert.True(Manuel.Section(ancre) is not null, $"ancre « {ancre} » absente du manuel"));
        Assert.All(EcransAide.Ecrans.Values, e => Assert.DoesNotContain(e.Ancre, e.Autres));
    }

    [Fact]
    public void EveryDeliveredScreenDeclaresItsHelp()
    {
        var pages = Directory.GetFiles(Path.Combine(Depot(), "server", "src", "GestionSite.Web", "Components", "Pages"), "*.razor");
        var ecrans = new List<string>();
        foreach (var page in pages.Where(p => !PagesDispensees.Contains(Path.GetFileNameWithoutExtension(p))))
        {
            var aide = AideDeclaree().Match(File.ReadAllText(page));
            Assert.True(aide.Success, $"{Path.GetFileName(page)} : aucune aide d'écran (<AideEcran Ecran=\"…\" />)");
            Assert.True(EcransAide.Ecrans.ContainsKey(aide.Groups[1].Value), $"{Path.GetFileName(page)} : écran « {aide.Groups[1].Value} » inconnu");
            ecrans.Add(aide.Groups[1].Value);
        }

        Assert.Equal(20, ecrans.Count);
        Assert.Empty(EcransAide.Ecrans.Keys.Except(ecrans)); // aucune entrée orpheline dans la table
    }

    [Fact]
    public void EveryChapterRendersWithItsAnchorsAndWithoutRawMarkup()
    {
        foreach (var chapitre in Manuel.Chapitres)
        {
            var html = Manuel.RendreChapitre(chapitre, "/organisations/o/aide");

            Assert.DoesNotContain("{#", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".md", html, StringComparison.Ordinal); // tout lien vers un fichier du manuel est réécrit
            Assert.All(Manuel.Sections.Where(s => s.Chapitre == chapitre && s.Niveau > 1),
                s => Assert.Contains($"id=\"{s.Ancre}\"", html, StringComparison.Ordinal));
            foreach (Match image in ImageRendue().Matches(html))
            {
                Assert.Contains(image.Groups[1].Value, Manuel.NomsImages);
            }
        }
    }

    [Fact]
    public void ASectionStopsAtTheNextSectionOfTheSameLevel()
    {
        var qualifier = Manuel.Section("dossiers-qualifier")!;
        var inventaire = Manuel.Section("inventaire")!;
        var structure = Manuel.Section("inventaire-structure")!;

        Assert.Equal(("Qualifier un dossier", 2, "05-dossiers"), (qualifier.Titre, qualifier.Niveau, qualifier.Chapitre.Nom));
        Assert.Contains("Enregistrer la qualification", qualifier.Corps, StringComparison.Ordinal);
        Assert.DoesNotContain("Traiter un doublon", qualifier.Corps, StringComparison.Ordinal);
        Assert.DoesNotContain("## Qualifier", qualifier.Corps, StringComparison.Ordinal);
        Assert.DoesNotContain("Décrire la structure", inventaire.Corps, StringComparison.Ordinal); // un chapitre : son introduction
        Assert.Contains("### Ajouter un bâtiment", structure.Corps, StringComparison.Ordinal); // sous-sections comprises
        Assert.DoesNotContain("Ajouter un bâtiment", structure.TexteRecherche, StringComparison.Ordinal);
        Assert.DoesNotContain("{#", Manuel.Section("inventaire-batiment")!.TexteRecherche, StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionForAScreenLinksToTheHelpPagesOfTheOrganisation()
    {
        var html = Manuel.RendreSection(Manuel.Section("depot-recus")!, "/organisations/o/aide");

        Assert.Contains("href=\"/organisations/o/aide/05-dossiers#dossiers\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" id=\"", html, StringComparison.Ordinal); // ancres réservées à la page du chapitre
        Assert.Contains("<blockquote>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[clic](javascript:alert(1))")]
    [InlineData("[clic](JavaScript:alert(1))")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<data:text/html,x>")]
    [InlineData("<ftp://serveur/x>")]
    [InlineData("[clic](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("![x](https://exemple.invalid/x.png)")]
    [InlineData("![x](images/../../appsettings.json)")]
    [InlineData("[clic](//exemple.invalid/x)")]
    [InlineData("[clic](../appsettings.json)")]
    [InlineData("[clic](05-dossiers.md#\"onmouseover=alert(1))")]
    [InlineData("[clic](05-dossiers.md){onclick=alert(1)}")]
    [InlineData("## Titre {onclick=alert(1)}")]
    public void HostileMarkdownNeverProducesActiveContent(string markdown)
    {
        var html = RenduManuel.Html(markdown, Contexte(avecAncres: false));

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("<[a-z]+[^>]* on[a-z]+=", html);
        Assert.DoesNotContain("href=\"javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("href=\"(?!/aide/|#)", html); // seuls les liens du manuel subsistent
        Assert.DoesNotContain("exemple.invalid", html, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("src=\"(?!/aide/images/)", html);
    }

    [Fact]
    public void SafeAutolinksAreKept()
    {
        var html = RenduManuel.Html("<https://exemple.org/a> et <aide@exemple.org>", Contexte(avecAncres: false));

        Assert.Contains("<a href=\"https://exemple.org/a\">", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"mailto:aide@exemple.org\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RawHtmlIsEscapedAsText()
    {
        var html = RenduManuel.Html("Un <b>gras</b> et <script>alert(1)</script>", Contexte(avecAncres: false));

        Assert.Contains("&lt;b&gt;gras&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("05-dossiers.md#dossiers-qualifier", false, "/aide/05-dossiers#dossiers-qualifier")]
    [InlineData("05-dossiers.md", false, "/aide/05-dossiers")]
    [InlineData("#dossiers-delais", false, "/aide/05-dossiers#dossiers-delais")]
    [InlineData("#dossiers-delais", true, "#dossiers-delais")]
    [InlineData("05-dossiers.md#dossiers-delais", true, "#dossiers-delais")]
    [InlineData("06-inventaire.md#inventaire", true, "/aide/06-inventaire#inventaire")]
    [InlineData("https://exemple.org/page", false, "https://exemple.org/page")]
    [InlineData("mailto:aide@exemple.org", false, "mailto:aide@exemple.org")]
    [InlineData("99-inconnu.md", false, null)]
    [InlineData("#", false, null)]
    [InlineData("", false, null)]
    [InlineData("http://exemple.org", false, null)]
    [InlineData("https://", false, null)]
    [InlineData("05-dossiers.md#Ancre_Invalide", false, null)]
    public void LinksAreRewrittenOrDropped(string url, bool avecAncres, string? attendu)
    {
        Assert.Equal(attendu, RenduManuel.Cible(url, Contexte(avecAncres)));
    }

    [Fact]
    public void ADroppedLinkKeepsItsTextAndAnExternalLinkOpensSafely()
    {
        var html = RenduManuel.Html("Voir [le **guide**](99-inconnu.md) et [le site](https://exemple.org).", Contexte(avecAncres: false));

        Assert.Contains("Voir le <strong>guide</strong> et", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://exemple.org/\" rel=\"noopener noreferrer\">le site</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturesAndDiagramsAreRenderedWithoutScripts()
    {
        var html = RenduManuel.Html("![Écran « Membres »](images/membres.png)\n\n![Sur téléphone](images/mes-interventions-tel.png)\n\n"
            + "```mermaid\nflowchart TD\n  A --> B\n```\n\n```\ncode <b>\n```", Contexte(avecAncres: false));

        Assert.Contains("<img class=\"capture\" src=\"/aide/images/membres.png\" alt=\"Écran « Membres »\" loading=\"lazy\">",
            System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains("class=\"capture telephone\"", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(System.Net.WebUtility.HtmlDecode(html), "Écran « Membres »</span>")); // légende une seule fois
        Assert.Contains("aide-schema", html, StringComparison.Ordinal);
        Assert.DoesNotContain("flowchart", html, StringComparison.Ordinal);
        Assert.Contains("code &lt;b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadingAnchorsBecomeIdsOnlyForAWholeChapter()
    {
        Assert.Contains("<h2 id=\"x-y\">Titre</h2>", RenduManuel.Html("## Titre {#x-y}", Contexte(avecAncres: true)), StringComparison.Ordinal);
        Assert.Contains("<h2>Titre</h2>", RenduManuel.Html("## Titre {#x-y}", Contexte(avecAncres: false)), StringComparison.Ordinal);
        Assert.Contains("<h2>Titre <em>court</em></h2>", RenduManuel.Html("## Titre *court*", Contexte(avecAncres: true)), StringComparison.Ordinal);
    }

    [Fact]
    public void SearchIgnoresAccentsAndCaseAndListsTitlesFirst()
    {
        var accents = Manuel.Chercher("Clôturer");
        var sansAccents = Manuel.Chercher("cloturer");

        Assert.NotEmpty(accents);
        Assert.Equal(accents.Select(r => r.Section.Ancre), sansAccents.Select(r => r.Section.Ancre));
        Assert.Equal("dossiers-cloturer", accents[0].Section.Ancre);
        Assert.All(Manuel.Chercher("qualification priorité"), r =>
            Assert.Contains("priorit", ManuelAide.Normaliser(r.Section.Titre + " " + r.Section.TexteRecherche), StringComparison.Ordinal));
        Assert.True(Manuel.Chercher("dossier").Count <= 20);
        Assert.Empty(Manuel.Chercher("x"));
        Assert.Empty(Manuel.Chercher(null));
        Assert.Empty(Manuel.Chercher("motintrouvablezzz"));
    }

    [Fact]
    public void AnExcerptSurroundsTheFirstWordFound()
    {
        var resultat = Manuel.Chercher("prise en charge").First(r => r.Section.Ancre == "dossiers-qualifier");

        Assert.Contains("prise en charge", resultat.Extrait, StringComparison.OrdinalIgnoreCase);
        Assert.True(resultat.Extrait.Length <= 190, resultat.Extrait);
    }

    [Fact]
    public void NormalisingKeepsTheLength()
    {
        Assert.Equal("eleve a l'ecole", ManuelAide.Normaliser("Élève à l'École"));
        Assert.Equal("Élève à l'École".Length, ManuelAide.Normaliser("Élève à l'École").Length);
        Assert.Equal("a b", ManuelAide.Normaliser("a\tb"));
    }

    [Fact]
    public void TheTasksOfThePersonsRolesComeFirst()
    {
        var technicien = EcransAide.RolesPour(["Technicien"]);
        var aucun = EcransAide.RolesPour([]);

        Assert.Equal(("Technicien", true), (technicien[0].Taches.Titre, technicien[0].SesRoles));
        Assert.All(technicien.Skip(1), t => Assert.False(t.SesRoles));
        Assert.Equal(EcransAide.Roles.Select(r => r.Titre), aucun.Select(t => t.Taches.Titre));
        Assert.Equal(2, EcransAide.RolesPour(["Delegue", "Administrateur"]).Count(t => t.SesRoles));
    }

    [Fact]
    public void TheBookletIsOfferedOnlyWhenItExists()
    {
        var chemin = Path.Combine(Path.GetTempPath(), $"livret-{Guid.NewGuid():N}.pdf");
        var livret = new LivretAide(chemin);
        Assert.Null(livret.Libelle);

        File.WriteAllBytes(chemin, new byte[3 * 1024 * 1024 / 2]);
        try
        {
            Assert.Equal("PDF, 1,5 Mo", livret.Libelle);
        }
        finally
        {
            File.Delete(chemin);
        }
    }

    [Fact]
    public void AnInconsistentManualIsRefusedAtStartup()
    {
        var images = new Dictionary<string, byte[]>();

        Assert.Throws<InvalidOperationException>(() => new ManuelAide([("01-a.md", "# A {#a}\n## B {#b}"), ("02-b.md", "# C {#b}")], images));
        Assert.Throws<InvalidOperationException>(() => new ManuelAide([("01-a.md", "Sans titre")], images));
        var manuel = new ManuelAide([("01-a.md", "# 1. A {#a}\n```\n## Pas un titre {#code}\n```\n## B {#b}"), ("README.md", "# R {#r}")], images);
        Assert.Equal(["a", "b"], manuel.Sections.Select(s => s.Ancre));
        Assert.Null(manuel.Edition);
        Assert.Null(manuel.Chapitre("README"));
    }

    private static ContexteRendu Contexte(bool avecAncres) =>
        new("/aide", "05-dossiers", Manuel.NomsChapitres, Manuel.NomsImages, avecAncres);

    private static string Depot()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "server", "GestionSite.slnx")))
        {
            dossier = dossier.Parent;
        }

        return dossier?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }

    [GeneratedRegex("<AideEcran Ecran=\"([a-z-]+)\"")]
    private static partial Regex AideDeclaree();

    [GeneratedRegex("src=\"/aide/images/([^\"]+)\"")]
    private static partial Regex ImageRendue();
}

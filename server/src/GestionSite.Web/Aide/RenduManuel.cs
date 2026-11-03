using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GestionSite.Web.Aide;

/// <summary>Contexte d'un rendu : où pointent les liens, quelles captures existent, ancres posées ou non.</summary>
/// <param name="BaseLiens">Préfixe des pages d'aide (« /organisations/{id}/aide » ou « /aide ») ; null : aucun lien vers elles.</param>
/// <param name="Chapitre">Chapitre rendu (« 05-dossiers ») : cible des liens « #ancre » d'un extrait.</param>
/// <param name="Chapitres">Chapitres existants : un lien vers un autre fichier n'est gardé que s'il en est un.</param>
/// <param name="Images">Captures existantes et leur version (« dossier-qualifier.png » → empreinte courte).</param>
/// <param name="AvecAncres">Vrai pour un chapitre entier (titres avec leur identifiant, liens internes « #ancre »).</param>
internal sealed record ContexteRendu(
    string? BaseLiens, string Chapitre, IReadOnlySet<string> Chapitres, IReadOnlyDictionary<string, string> Images, bool AvecAncres);

/// <summary>
/// Rendu HTML d'un extrait du manuel (DOC-02). Le manuel est versionné et relu, mais le rendu reste sûr par construction :
/// aucun HTML brut (échappé), liens limités au manuel, à https et à mailto, images limitées aux captures du manuel, aucun
/// attribut générique, aucun script (les schémas Mermaid renvoient au livret PDF).
/// </summary>
internal static partial class RenduManuel
{
    internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();

    /// <summary>Ancre stable en fin de titre : « Qualifier un dossier {#dossiers-qualifier} ».</summary>
    [GeneratedRegex(@"\s*\{#([a-z0-9]+(?:-[a-z0-9]+)*)\}\s*$")]
    internal static partial Regex AncreTitre();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex AncreValide();

    [GeneratedRegex(@"^(\d{2}-[a-z0-9-]+)\.md$")]
    private static partial Regex FichierChapitre();

    internal static string Html(string markdown, ContexteRendu contexte)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var titre in document.Descendants<HeadingBlock>())
        {
            TraiterTitre(titre, contexte.AvecAncres);
        }

        foreach (var lien in document.Descendants<LinkInline>().ToList())
        {
            if (lien.IsImage)
            {
                TraiterImage(lien, contexte);
            }
            else
            {
                TraiterLien(lien, contexte);
            }
        }

        // Liens automatiques « <schéma:…> » : https et adresses e-mail seulement, les autres restent du texte.
        foreach (var lien in document.Descendants<AutolinkInline>().ToList())
        {
            var sur = lien.IsEmail || (Cible(lien.Url, contexte) is { } cible && cible.StartsWith("https:", StringComparison.Ordinal));
            if (!sur)
            {
                lien.ReplaceBy(new LiteralInline(lien.Url), copyChildren: false);
            }
        }

        using var ecrivain = new StringWriter(CultureInfo.InvariantCulture);
        var rendu = new HtmlRenderer(ecrivain);
        Pipeline.Setup(rendu);
        rendu.ObjectRenderers.Replace<CodeBlockRenderer>(new RenduCode());
        rendu.Render(document);
        ecrivain.Flush();
        return ecrivain.ToString();
    }

    /// <summary>Texte d'un titre sans son ancre ni ses marques d'emphase (« **Qualifier** {#x} » → « Qualifier »).</summary>
    internal static string TexteTitre(string titre) =>
        AncreTitre().Replace(titre, string.Empty).Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal).Trim();

    private static void TraiterTitre(HeadingBlock titre, bool avecAncres)
    {
        if (titre.Inline?.LastChild is not LiteralInline dernier)
        {
            return;
        }

        var texte = dernier.Content.ToString();
        var ancre = AncreTitre().Match(texte);
        if (!ancre.Success)
        {
            return;
        }

        dernier.Content = new Markdig.Helpers.StringSlice(texte[..ancre.Index]);
        if (avecAncres)
        {
            titre.GetAttributes().Id = ancre.Groups[1].Value;
        }
    }

    private static void TraiterImage(LinkInline image, ContexteRendu contexte)
    {
        var legende = Texte(image);
        var nom = image.Url is { } url && url.StartsWith("images/", StringComparison.Ordinal) ? url["images/".Length..] : null;
        if (nom is null || !contexte.Images.TryGetValue(nom, out var version))
        {
            image.ReplaceBy(new LiteralInline(legende), copyChildren: false);
            return;
        }

        // Nom vérifié parmi les captures du manuel ; légende échappée. Une capture de téléphone (« -tel ») s'affiche plus étroite.
        var encodeur = HtmlEncoder.Default;
        var classe = nom.EndsWith("-tel.png", StringComparison.Ordinal) ? "capture telephone" : "capture";
        image.ReplaceBy(new HtmlInline(
            $"<span class=\"aide-figure\"><img class=\"{classe}\" src=\"/aide/images/{encodeur.Encode(nom)}?v={version}\" alt=\"{encodeur.Encode(legende)}\" loading=\"lazy\">"
            + $"<span class=\"legende\">{encodeur.Encode(legende)}</span></span>"), copyChildren: false);
    }

    private static void TraiterLien(LinkInline lien, ContexteRendu contexte)
    {
        var cible = Cible(lien.Url ?? string.Empty, contexte);
        if (cible is null)
        {
            Deballer(lien);
            return;
        }

        lien.Url = cible;
        lien.Title = null;
        if (cible.StartsWith("https:", StringComparison.Ordinal))
        {
            lien.GetAttributes().AddProperty("rel", "noopener noreferrer");
        }
    }

    /// <summary>Adresse sûre d'un lien du manuel, ou null : le lien est alors remplacé par son texte.</summary>
    internal static string? Cible(string url, ContexteRendu contexte)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var absolue) && absolue.Scheme is "https" or "mailto" ? absolue.AbsoluteUri : null;
        }

        var diese = url.IndexOf('#', StringComparison.Ordinal);
        var chemin = diese < 0 ? url : url[..diese];
        var ancre = diese < 0 ? null : url[(diese + 1)..];
        if (ancre is not null && !AncreValide().IsMatch(ancre))
        {
            return null;
        }

        string chapitre;
        if (chemin.Length == 0)
        {
            if (ancre is null)
            {
                return null;
            }

            chapitre = contexte.Chapitre;
        }
        else if (FichierChapitre().Match(chemin) is { Success: true } fichier && contexte.Chapitres.Contains(fichier.Groups[1].Value))
        {
            chapitre = fichier.Groups[1].Value;
        }
        else
        {
            return null;
        }

        var fragment = ancre is null ? string.Empty : $"#{ancre}";
        if (contexte.AvecAncres && chapitre == contexte.Chapitre && ancre is not null)
        {
            return fragment;
        }

        return contexte.BaseLiens is null ? null : $"{contexte.BaseLiens}/{chapitre}{fragment}";
    }

    private static void Deballer(LinkInline lien)
    {
        var enfant = lien.FirstChild;
        while (enfant is not null)
        {
            var suivant = enfant.NextSibling;
            enfant.Remove();
            lien.InsertBefore(enfant);
            enfant = suivant;
        }

        lien.Remove();
    }

    private static string Texte(ContainerInline conteneur) =>
        string.Concat(conteneur.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    /// <summary>Blocs de code : un schéma Mermaid (dessiné dans le livret, jamais par un script ici) renvoie au livret.</summary>
    private sealed class RenduCode : CodeBlockRenderer
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock obj)
        {
            if (obj is FencedCodeBlock { Info: "mermaid" })
            {
                renderer.Write("<p class=\"aide-schema\">Le schéma de ce cycle figure dans le livret PDF du manuel.</p>\n");
                return;
            }

            base.Write(renderer, obj);
        }
    }
}

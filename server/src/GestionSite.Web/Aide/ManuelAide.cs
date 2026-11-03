using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace GestionSite.Web.Aide;

/// <summary>Chapitre du manuel (« 05-dossiers.md »).</summary>
/// <param name="Nom">Nom sans extension (« 05-dossiers »), utilisé dans l'adresse de la page du chapitre.</param>
/// <param name="Numero">Numéro du chapitre (0 pour la présentation).</param>
/// <param name="Titre">Titre sans numéro ni ancre (« Dossiers »).</param>
/// <param name="Ancre">Ancre du titre de niveau 1 (« dossiers »).</param>
/// <param name="Markdown">Texte source du chapitre.</param>
internal sealed record ChapitreManuel(string Nom, int Numero, string Titre, string Ancre, string Markdown);

/// <summary>Section du manuel repérée par son ancre stable.</summary>
/// <param name="Ancre">Ancre stable du titre (« dossiers-qualifier »).</param>
/// <param name="Titre">Titre sans son ancre.</param>
/// <param name="Niveau">Niveau du titre (1 pour un chapitre).</param>
/// <param name="Chapitre">Chapitre de la section.</param>
/// <param name="Corps">Markdown de la section sans son titre : sous-sections comprises (niveau 2 et plus), introduction seule pour un chapitre.</param>
/// <param name="TexteRecherche">Texte brut propre à la section (sans ses sous-sections), pour la recherche.</param>
internal sealed record SectionManuel(string Ancre, string Titre, int Niveau, ChapitreManuel Chapitre, string Corps, string TexteRecherche);

/// <summary>Résultat de recherche : section et extrait autour du premier mot trouvé.</summary>
internal sealed record ResultatRecherche(SectionManuel Section, string Extrait);

/// <summary>
/// Manuel d'utilisation (docs/manuel, DOC-01) embarqué dans l'application web : source unique de l'aide intégrée (DOC-02).
/// Chargé une fois au démarrage ; chaque titre de niveau 1 à 3 porte une ancre stable, que les écrans citent.
/// </summary>
internal sealed partial class ManuelAide
{
    private const string PrefixeRessource = "manuel/";
    private const string PrefixeImages = "manuel/images/";
    private const int ResultatsMax = 20;

    private readonly Dictionary<string, ChapitreManuel> _chapitres;
    private readonly Dictionary<string, SectionManuel> _sections = new(StringComparer.Ordinal);
    private readonly List<SectionManuel> _ordre = [];
    private readonly Dictionary<string, byte[]> _images;

    internal ManuelAide(IEnumerable<(string Fichier, string Markdown)> chapitres, IReadOnlyDictionary<string, byte[]> images)
    {
        _images = new Dictionary<string, byte[]>(images, StringComparer.Ordinal);
        Chapitres = [.. chapitres.Where(c => FichierChapitre().IsMatch(c.Fichier)).OrderBy(c => c.Fichier, StringComparer.Ordinal)
            .Select(c => LireChapitre(c.Fichier, c.Markdown))];
        _chapitres = Chapitres.ToDictionary(c => c.Nom, StringComparer.Ordinal);
        foreach (var chapitre in Chapitres)
        {
            foreach (var section in LireSections(chapitre))
            {
                if (!_sections.TryAdd(section.Ancre, section))
                {
                    throw new InvalidOperationException($"Ancre « {section.Ancre} » en double dans le manuel ({chapitre.Nom}).");
                }

                _ordre.Add(section);
            }
        }

        NomsChapitres = _chapitres.Keys.ToHashSet(StringComparer.Ordinal);
        NomsImages = _images.Keys.ToHashSet(StringComparer.Ordinal);
        Edition = MotifEdition().Match(Chapitres.Count > 0 ? Chapitres[0].Markdown : string.Empty) is { Success: true } edition
            ? edition.Groups[1].Value
            : null;
    }

    /// <summary>Chapitres dans l'ordre de leur numéro (la présentation « 00 » d'abord).</summary>
    internal IReadOnlyList<ChapitreManuel> Chapitres { get; }

    internal IReadOnlySet<string> NomsChapitres { get; }

    internal IReadOnlySet<string> NomsImages { get; }

    /// <summary>Date d'édition lue dans la présentation (« 3 novembre 2026 »), ou null.</summary>
    internal string? Edition { get; }

    /// <summary>Sections dans l'ordre du manuel.</summary>
    internal IReadOnlyList<SectionManuel> Sections => _ordre;

    /// <summary>Manuel embarqué dans l'assemblage (voir GestionSite.Web.csproj).</summary>
    internal static ManuelAide Charger()
    {
        var assemblage = typeof(ManuelAide).Assembly;
        var noms = assemblage.GetManifestResourceNames();
        var chapitres = noms.Where(n => n.StartsWith(PrefixeRessource, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)
                && !n.StartsWith(PrefixeImages, StringComparison.Ordinal))
            .Select(n => (n[PrefixeRessource.Length..], Lire(assemblage, n)))
            .ToList();
        var images = noms.Where(n => n.StartsWith(PrefixeImages, StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal))
            .ToDictionary(n => n[PrefixeImages.Length..], n => Octets(assemblage, n), StringComparer.Ordinal);
        return new ManuelAide(chapitres, images);
    }

    internal ChapitreManuel? Chapitre(string nom) => _chapitres.GetValueOrDefault(nom);

    internal SectionManuel? Section(string ancre) => _sections.GetValueOrDefault(ancre);

    internal byte[]? Image(string nom) => _images.GetValueOrDefault(nom);

    /// <summary>HTML d'une section (sans son titre) pour le panneau d'aide d'un écran.</summary>
    internal string RendreSection(SectionManuel section, string baseLiens) =>
        RenduManuel.Html(section.Corps, Contexte(baseLiens, section.Chapitre.Nom, avecAncres: false));

    /// <summary>HTML d'un chapitre entier (titres avec leur ancre, sauf le titre de niveau 1 que la page affiche).</summary>
    internal string RendreChapitre(ChapitreManuel chapitre, string baseLiens)
    {
        var lignes = chapitre.Markdown.Split('\n');
        var premier = TitresHorsCode(lignes).FirstOrDefault(t => t.Niveau == 1);
        var corps = premier is null ? chapitre.Markdown : string.Join('\n', lignes.Skip(premier.Ligne + 1));
        return RenduManuel.Html(corps, Contexte(baseLiens, chapitre.Nom, avecAncres: true));
    }

    /// <summary>
    /// Recherche sans tenir compte des accents ni de la casse : sections dont le titre et le texte contiennent tous les mots
    /// (2 caractères au moins), titres trouvés d'abord, dans l'ordre du manuel.
    /// </summary>
    internal IReadOnlyList<ResultatRecherche> Chercher(string? requete)
    {
        var mots = Normaliser(requete ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(m => m.Length >= 2).Distinct(StringComparer.Ordinal).Take(8).ToArray();
        if (mots.Length == 0)
        {
            return [];
        }

        return [.. _ordre
            .Select(s => (Section: s, Titre: Normaliser(s.Titre), Texte: Normaliser(s.TexteRecherche)))
            .Where(s => mots.All(m => s.Titre.Contains(m, StringComparison.Ordinal) || s.Texte.Contains(m, StringComparison.Ordinal)))
            .OrderByDescending(s => mots.Any(m => s.Titre.Contains(m, StringComparison.Ordinal)))
            .Take(ResultatsMax)
            .Select(s => new ResultatRecherche(s.Section, Extrait(s.Section.TexteRecherche, mots)))];
    }

    private ContexteRendu Contexte(string baseLiens, string chapitre, bool avecAncres) =>
        new(baseLiens, chapitre, NomsChapitres, NomsImages, avecAncres);

    private static ChapitreManuel LireChapitre(string fichier, string markdown)
    {
        var nom = fichier[..^".md".Length];
        var titre = TitresHorsCode(markdown.Split('\n')).FirstOrDefault(t => t.Niveau == 1)
            ?? throw new InvalidOperationException($"{fichier} : le chapitre doit commencer par un titre de niveau 1.");
        var numero = int.Parse(nom[..2], CultureInfo.InvariantCulture);
        return new ChapitreManuel(nom, numero, NumeroChapitre().Replace(titre.Texte, string.Empty), titre.Ancre ?? nom, markdown);
    }

    private static IEnumerable<SectionManuel> LireSections(ChapitreManuel chapitre)
    {
        var lignes = chapitre.Markdown.Split('\n');
        var titres = TitresHorsCode(lignes).ToList();
        for (var i = 0; i < titres.Count; i++)
        {
            var titre = titres[i];
            if (titre.Ancre is null)
            {
                continue;
            }

            // Fin du corps : titre suivant de même niveau ou plus haut ; un chapitre (niveau 1) s'arrête à sa première section.
            var limite = Math.Max(titre.Niveau, 2);
            var fin = titres.Skip(i + 1).FirstOrDefault(t => t.Niveau <= limite)?.Ligne ?? lignes.Length;
            var finPropre = i + 1 < titres.Count ? Math.Min(titres[i + 1].Ligne, fin) : fin;
            var corps = string.Join('\n', lignes[(titre.Ligne + 1)..fin]).Trim('\n', '\r', ' ');
            var propre = string.Join('\n', SansCode(lignes[(titre.Ligne + 1)..finPropre]));
            yield return new SectionManuel(titre.Ancre, titre.Texte, titre.Niveau, chapitre, corps,
                Espaces().Replace(Markdown.ToPlainText(propre, RenduManuel.Pipeline), " ").Trim());
        }
    }

    private sealed record Titre(int Niveau, string Texte, string? Ancre, int Ligne);

    private static IEnumerable<Titre> TitresHorsCode(string[] lignes)
    {
        var code = false;
        for (var i = 0; i < lignes.Length; i++)
        {
            var ligne = lignes[i];
            if (ligne.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                code = !code;
                continue;
            }

            if (!code && TitreMarkdown().Match(ligne) is { Success: true } titre)
            {
                var ancre = RenduManuel.AncreTitre().Match(titre.Groups[2].Value);
                yield return new Titre(titre.Groups[1].Length, RenduManuel.TexteTitre(titre.Groups[2].Value),
                    ancre.Success ? ancre.Groups[1].Value : null, i);
            }
        }
    }

    private static IEnumerable<string> SansCode(IEnumerable<string> lignes)
    {
        var code = false;
        foreach (var ligne in lignes)
        {
            var cloture = ligne.TrimStart().StartsWith("```", StringComparison.Ordinal);
            if (cloture)
            {
                code = !code;
            }

            // Ancres des sous-titres retirées : elles ne sont pas du texte à chercher.
            yield return code || cloture ? string.Empty : RenduManuel.AncreTitre().Replace(ligne, string.Empty);
        }
    }

    private static string Extrait(string texte, string[] mots)
    {
        const int Avant = 60;
        const int Longueur = 180;
        var normalise = Normaliser(texte);
        var position = mots.Select(m => normalise.IndexOf(m, StringComparison.Ordinal)).Where(p => p >= 0).DefaultIfEmpty(0).Min();
        // Normaliser conserve la longueur (une lettre accentuée devient une lettre) : la position vaut pour le texte d'origine.
        var debut = Math.Max(0, Math.Min(position, normalise.Length) - Avant);
        if (debut > 0 && texte.IndexOf(' ', debut) is var espace and >= 0 && espace < position)
        {
            debut = espace + 1;
        }

        var fin = Math.Min(texte.Length, debut + Longueur);
        if (fin < texte.Length && texte.LastIndexOf(' ', fin) is var dernier && dernier > debut)
        {
            fin = dernier;
        }

        return $"{(debut > 0 ? "… " : string.Empty)}{texte[debut..fin].Trim()}{(fin < texte.Length ? " …" : string.Empty)}";
    }

    /// <summary>Minuscules sans accents, longueur conservée (un caractère par caractère).</summary>
    internal static string Normaliser(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (var caractere in texte)
        {
            var decompose = caractere.ToString().Normalize(NormalizationForm.FormD);
            var lettre = decompose.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(decompose[0]) != UnicodeCategory.NonSpacingMark
                ? decompose[0]
                : caractere;
            resultat.Append(char.IsWhiteSpace(lettre) ? ' ' : char.ToLowerInvariant(lettre));
        }

        return resultat.ToString();
    }

    private static string Lire(Assembly assemblage, string nom)
    {
        using var flux = assemblage.GetManifestResourceStream(nom)!;
        using var lecteur = new StreamReader(flux, Encoding.UTF8);
        return lecteur.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static byte[] Octets(Assembly assemblage, string nom)
    {
        using var flux = assemblage.GetManifestResourceStream(nom)!;
        using var copie = new MemoryStream();
        flux.CopyTo(copie);
        return copie.ToArray();
    }

    [GeneratedRegex(@"^\d{2}-[a-z0-9-]+\.md$")]
    private static partial Regex FichierChapitre();

    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+?)\s*$")]
    private static partial Regex TitreMarkdown();

    [GeneratedRegex(@"^\d+\.\s+")]
    private static partial Regex NumeroChapitre();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();

    [GeneratedRegex(@"Édition du ([^.\n]+)\.")]
    private static partial Regex MotifEdition();
}

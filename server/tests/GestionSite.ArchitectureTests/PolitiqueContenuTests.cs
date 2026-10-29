using System.Text.RegularExpressions;

namespace GestionSite.ArchitectureTests;

/// <summary>
/// La politique de sécurité du contenu de l'application web n'autorise ni style ni script en ligne : un attribut
/// style ou un bloc &lt;style&gt; dans un composant serait ignoré par le navigateur (rendu faux, violation signalée).
/// </summary>
public sealed partial class PolitiqueContenuTests
{
    [Fact]
    public void WebComponentsUseStylesheetClassesOnly()
    {
        var composants = new DirectoryInfo(Path.Combine(SolutionLayout.SourceDirectory.FullName, "GestionSite.Web", "Components"))
            .EnumerateFiles("*.razor", SearchOption.AllDirectories).ToList();

        var fautifs = composants.Where(f => StyleEnLigne().IsMatch(File.ReadAllText(f.FullName))).Select(f => f.Name).ToList();

        Assert.NotEmpty(composants);
        Assert.Empty(fautifs);
    }

    [GeneratedRegex("""\sstyle\s*=\s*["']|<style[\s>]""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleEnLigne();
}

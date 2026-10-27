using System.Xml.Linq;

namespace GestionSite.ArchitectureTests;

/// <summary>
/// Complète <see cref="ModuleBoundaryTests"/> : une référence de projet déclarée mais inutilisée
/// n'apparaît pas dans l'assemblage compilé ; elle est donc contrôlée dans les fichiers projet.
/// </summary>
public sealed class ProjectReferenceTests
{
    private static readonly DirectoryInfo ModulesDirectory = new(
        Path.Combine(FindSolutionDirectory().FullName, "src", "Modules"));

    public static TheoryData<string> ModuleProjects => new(
        ModulesDirectory.GetFiles("*.csproj", SearchOption.AllDirectories).Select(f => f.FullName));

    [Fact]
    public void ModulesDirectoryContainsTheSixModules()
    {
        Assert.Equal(6, ModulesDirectory.GetFiles("*.csproj", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [MemberData(nameof(ModuleProjects))]
    public void ModuleProjectDoesNotReferenceAnotherProject(string projectPath)
    {
        var references = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(r => (string?)r.Attribute("Include"))
            .ToList();

        Assert.Empty(references);
    }

    private static DirectoryInfo FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.GetFiles("GestionSite.slnx").Length == 0)
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("GestionSite.slnx introuvable.");
    }
}

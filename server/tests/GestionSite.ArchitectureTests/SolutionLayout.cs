using System.Reflection;
using System.Xml.Linq;

namespace GestionSite.ArchitectureTests;

/// <summary>
/// Découverte des projets de la solution à partir des sources, pour qu'un nouveau module
/// soit contrôlé sans modifier les tests.
/// </summary>
internal static class SolutionLayout
{
    internal const string Prefix = "GestionSite.";
    internal const string ModulePrefix = "GestionSite.Modules.";
    internal const string SharedKernel = "GestionSite.SharedKernel";
    internal const string ContractsSuffix = ".Contracts";

    private static readonly Lazy<DirectoryInfo> Root = new(FindSolutionDirectory);

    internal static DirectoryInfo SourceDirectory => new(Path.Combine(Root.Value.FullName, "src"));

    /// <summary>Projets d'implémentation des modules (hors contrats).</summary>
    internal static IReadOnlyList<FileInfo> ModuleProjects =>
        new DirectoryInfo(Path.Combine(SourceDirectory.FullName, "Modules"))
            .GetFiles("*.csproj", SearchOption.AllDirectories)
            .Where(f => IsModuleImplementation(ProjectName(f)))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Projets partageables entre modules : contrats publics et noyau partagé.</summary>
    internal static IReadOnlyList<FileInfo> SharedProjects =>
        SourceDirectory.GetFiles("*.csproj", SearchOption.AllDirectories)
            .Where(f => IsShareable(ProjectName(f)))
            .ToList();

    internal static string ProjectName(FileInfo project) => Path.GetFileNameWithoutExtension(project.Name);

    internal static bool IsModuleImplementation(string name) =>
        name.StartsWith(ModulePrefix, StringComparison.Ordinal) && !name.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    internal static bool IsShareable(string name) =>
        name == SharedKernel || (name.StartsWith(ModulePrefix, StringComparison.Ordinal) && name.EndsWith(ContractsSuffix, StringComparison.Ordinal));

    internal static IEnumerable<string> ProjectReferences(FileInfo project) =>
        XDocument.Load(project.FullName)
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(((string?)r.Attribute("Include") ?? string.Empty).Replace('\\', '/')));

    /// <summary>Assemblages GestionSite.* atteints transitivement depuis <paramref name="start"/>.</summary>
    internal static ISet<string> TransitiveSolutionReferences(Assembly start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<AssemblyName>(start.GetReferencedAssemblies());
        while (pending.TryPop(out var name))
        {
            if (name.Name is null || !name.Name.StartsWith(Prefix, StringComparison.Ordinal) || !seen.Add(name.Name))
            {
                continue;
            }

            foreach (var next in Assembly.Load(name).GetReferencedAssemblies())
            {
                pending.Push(next);
            }
        }

        return seen;
    }

    private static DirectoryInfo FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.GetFiles("GestionSite.slnx").Length == 0)
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"GestionSite.slnx introuvable au-dessus de {AppContext.BaseDirectory} : exécuter les tests d'architecture depuis l'arborescence des sources.");
    }
}

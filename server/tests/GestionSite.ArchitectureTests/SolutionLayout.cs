using System.Reflection;
using System.Xml.Linq;

namespace GestionSite.ArchitectureTests;

/// <summary>
/// Découverte des projets à partir des sources, pour qu'un nouveau module ou sous-projet soit
/// contrôlé sans modifier les tests. Un module X regroupe GestionSite.Modules.X (point d'entrée
/// assemblé par l'API) et ses éventuels sous-projets GestionSite.Modules.X.* ; ses contrats
/// publics sont GestionSite.Modules.X.Contracts.
/// </summary>
internal static class SolutionLayout
{
    internal const string Prefix = "GestionSite.";
    internal const string ModulePrefix = "GestionSite.Modules.";
    internal const string SharedKernel = "GestionSite.SharedKernel";
    internal const string ContractsSuffix = ".Contracts";

    private static readonly Lazy<DirectoryInfo> Root = new(FindSolutionDirectory);

    private static readonly Lazy<IReadOnlyList<FileInfo>> Projects = new(() =>
        new DirectoryInfo(Path.Combine(Root.Value.FullName, "src"))
            .GetFiles("*.csproj", SearchOption.AllDirectories)
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList());

    /// <summary>Tous les projets de src/, triés par nom.</summary>
    internal static IReadOnlyList<FileInfo> SourceProjects => Projects.Value;

    /// <summary>Points d'entrée des modules : GestionSite.Modules.X, un par module.</summary>
    internal static IEnumerable<string> ModuleEntries =>
        SourceProjects.Select(ProjectName).Where(n => ModuleOf(n) is { } m && n == ModulePrefix + m && !IsContracts(n));

    internal const string ApiHost = "GestionSite.Api";

    internal static DirectoryInfo SourceDirectory => new(Path.Combine(Root.Value.FullName, "src"));

    /// <summary>Projets ni module ni contrat ni noyau : hôtes (API, futur web, tâches différées…).</summary>
    internal static IEnumerable<string> Hosts =>
        SourceProjects.Select(ProjectName).Where(n => ModuleOf(n) is null && n != SharedKernel);

    internal static string ProjectName(FileInfo project) => Path.GetFileNameWithoutExtension(project.Name);

    internal static FileInfo Project(string name) => SourceProjects.Single(p => ProjectName(p) == name);

    /// <summary>Nom du module (X) d'un projet GestionSite.Modules.X[.…], sinon null.</summary>
    internal static string? ModuleOf(string name) =>
        name.StartsWith(ModulePrefix, StringComparison.Ordinal) ? name[ModulePrefix.Length..].Split('.')[0] : null;

    internal static bool IsContracts(string name) =>
        ModuleOf(name) is not null && name.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    internal static bool IsModuleImplementation(string name) => ModuleOf(name) is not null && !IsContracts(name);

    /// <summary>
    /// Une implémentation du module X peut dépendre des projets de X, des contrats des autres modules
    /// et du noyau partagé ; jamais d'une autre implémentation, de l'API ou d'un autre hôte.
    /// </summary>
    internal static bool ImplementationMayReference(string from, string to) =>
        ModuleOf(to) == ModuleOf(from) || IsContracts(to) || to == SharedKernel;

    /// <summary>Les contrats ne dépendent que du noyau partagé ; le noyau ne dépend d'aucun projet.</summary>
    internal static bool SharedMayReference(string from, string to) => from != SharedKernel && to == SharedKernel;

    /// <summary>Références de projet déclarées (attributs Include, listes séparées par « ; » comprises).</summary>
    internal static IEnumerable<string> ProjectReferences(FileInfo project) =>
        XDocument.Load(project.FullName)
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .SelectMany(r => ((string?)r.Attribute("Include") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(path => Path.GetFileNameWithoutExtension(path.Trim().Replace('\\', '/')));

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

            Assembly loaded;
            try
            {
                loaded = Assembly.Load(name);
            }
            catch (FileNotFoundException exception)
            {
                throw new InvalidOperationException(
                    $"{name.Name} (référencé par {start.GetName().Name}) est introuvable : l'assembler via GestionSite.Api.", exception);
            }

            foreach (var next in loaded.GetReferencedAssemblies())
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

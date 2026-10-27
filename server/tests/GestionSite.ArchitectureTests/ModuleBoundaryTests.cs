using System.Reflection;

namespace GestionSite.ArchitectureTests;

/// <summary>
/// Frontières du monolithe modulaire (ADR-01, ARCHITECTURE.md) : un module n'accède à un autre
/// que par ses contrats publics (GestionSite.Modules.X.Contracts) ou le noyau partagé ; jamais
/// à son implémentation, ni à l'API ou à un autre hôte. Seule l'API assemble les modules.
/// </summary>
public sealed class ModuleBoundaryTests
{
    public static TheoryData<string> Modules => new(SolutionLayout.ModuleProjects.Select(SolutionLayout.ProjectName));

    [Fact]
    public void ModulesAreDiscovered()
    {
        Assert.NotEmpty(SolutionLayout.ModuleProjects);
    }

    [Fact]
    public void ApiAssemblesExactlyTheDiscoveredModules()
    {
        var assembled = typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(SolutionLayout.IsModuleImplementation)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(SolutionLayout.ModuleProjects.Select(SolutionLayout.ProjectName).ToHashSet(StringComparer.Ordinal), assembled);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ModuleProjectReferencesOnlyShareableProjects(string module)
    {
        var project = SolutionLayout.ModuleProjects.Single(f => SolutionLayout.ProjectName(f) == module);

        var forbidden = SolutionLayout.ProjectReferences(project).Where(r => !SolutionLayout.IsShareable(r));

        Assert.Empty(forbidden);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ModuleReachesNoOtherImplementationEvenIndirectly(string module)
    {
        var reached = SolutionLayout.TransitiveSolutionReferences(Assembly.Load(module));

        Assert.DoesNotContain(reached, name => !SolutionLayout.IsShareable(name));
    }

    [Fact]
    public void SharedProjectsReferenceOnlyShareableProjects()
    {
        // Aucun projet partagé n'existe encore : le test devient actif dès le premier contrat.
        var forbidden = SolutionLayout.SharedProjects
            .SelectMany(p => SolutionLayout.ProjectReferences(p).Select(r => $"{SolutionLayout.ProjectName(p)} -> {r}"))
            .Where(link => !SolutionLayout.IsShareable(link.Split(" -> ")[1]));

        Assert.Empty(forbidden);
    }
}

using System.Reflection;

namespace GestionSite.ArchitectureTests;

/// <summary>
/// Frontières du monolithe modulaire (ADR-01, ARCHITECTURE.md) : un module n'accède à un autre
/// que par ses contrats publics (GestionSite.Modules.Y.Contracts) ou le noyau partagé ; jamais à
/// son implémentation, ni à l'API ou à un autre hôte. Seule l'API assemble les modules.
/// </summary>
public sealed class ModuleBoundaryTests
{
    public static TheoryData<string> Implementations => new(
        SolutionLayout.SourceProjects.Select(SolutionLayout.ProjectName).Where(SolutionLayout.IsModuleImplementation));

    public static TheoryData<string> Entries => new(SolutionLayout.ModuleEntries);

    [Fact]
    public void ModulesAreDiscovered()
    {
        Assert.NotEmpty(SolutionLayout.ModuleEntries);
    }

    [Fact]
    public void ApiAssemblesExactlyTheModuleEntries()
    {
        var assembled = SolutionLayout.ProjectReferences(SolutionLayout.Project("GestionSite.Api"))
            .Where(SolutionLayout.IsModuleImplementation)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(SolutionLayout.ModuleEntries.ToHashSet(StringComparer.Ordinal), assembled);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public void ImplementationReferencesOnlyItsModuleContractsOrKernel(string project)
    {
        var forbidden = SolutionLayout.ProjectReferences(SolutionLayout.Project(project))
            .Where(r => !SolutionLayout.ImplementationMayReference(project, r));

        Assert.Empty(forbidden);
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void ModuleReachesNoOtherImplementationEvenIndirectly(string module)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(module);
        }
        catch (FileNotFoundException)
        {
            Assert.Fail($"{module} n'est pas chargé : l'ajouter aux références de GestionSite.Api.");
            return;
        }

        var reached = SolutionLayout.TransitiveSolutionReferences(assembly);

        Assert.DoesNotContain(reached, name => !SolutionLayout.ImplementationMayReference(module, name));
    }

    [Fact]
    public void ContractsDependOnlyOnTheKernelAndTheKernelOnNothing()
    {
        var forbidden =
            from project in SolutionLayout.SourceProjects
            let name = SolutionLayout.ProjectName(project)
            where SolutionLayout.IsContracts(name) || name == SolutionLayout.SharedKernel
            from reference in SolutionLayout.ProjectReferences(project)
            where !SolutionLayout.SharedMayReference(name, reference)
            select $"{name} -> {reference}";

        Assert.Empty(forbidden);
    }
}

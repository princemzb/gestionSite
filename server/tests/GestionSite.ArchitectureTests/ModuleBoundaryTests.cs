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

    /// <summary>
    /// Complète les contrôles de fichiers projet sur les assemblages compilés : détecte une dépendance
    /// réellement utilisée, quelle que soit la manière dont elle a été déclarée.
    /// </summary>
    [Theory]
    [MemberData(nameof(Entries))]
    public void CompiledModuleUsesNoOtherImplementationEvenIndirectly(string module)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(module);
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException($"{module} n'est pas chargé : l'ajouter aux références de GestionSite.Api.");
        }

        var reached = SolutionLayout.TransitiveSolutionReferences(assembly);

        Assert.DoesNotContain(reached, name => !SolutionLayout.ImplementationMayReference(module, name));
    }

    [Fact]
    public void OnlyTheApiHostReferencesModuleImplementations()
    {
        var forbidden =
            from host in SolutionLayout.Hosts
            where host != SolutionLayout.ApiHost
            from reference in SolutionLayout.ProjectReferences(SolutionLayout.Project(host))
            where SolutionLayout.IsModuleImplementation(reference)
            select $"{host} -> {reference}";

        Assert.Empty(forbidden);
    }

    [Fact]
    public void NoReferenceIsInjectedThroughSharedBuildFiles()
    {
        var injecting =
            from file in SolutionLayout.SourceDirectory.Parent!.GetFiles("Directory.Build.*", SearchOption.AllDirectories)
                .Concat(SolutionLayout.SourceDirectory.GetFiles("*.targets", SearchOption.AllDirectories))
                .Concat(SolutionLayout.SourceDirectory.GetFiles("*.props", SearchOption.AllDirectories))
            where System.Xml.Linq.XDocument.Load(file.FullName).Descendants()
                .Any(e => e.Name.LocalName is "ProjectReference" or "Reference")
            select file.FullName;

        Assert.Empty(injecting.Distinct());
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void ApiRegistersAndMapsEveryModule(string module)
    {
        var program = File.ReadAllText(Path.Combine(
            SolutionLayout.SourceDirectory.FullName, SolutionLayout.ApiHost, "Program.cs"));
        var name = SolutionLayout.ModuleOf(module);

        Assert.Contains($".Add{name}Module()", program, StringComparison.Ordinal);
        Assert.Contains($".Map{name}Endpoints()", program, StringComparison.Ordinal);
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

using System.Reflection;
using GestionSite.Modules.Budget;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Maintenance;
using GestionSite.Modules.Patrimoine;
using GestionSite.Modules.Plateforme;
using GestionSite.Modules.Stock;

namespace GestionSite.ArchitectureTests;

/// <summary>
/// Frontières du monolithe modulaire (ADR-01, ARCHITECTURE.md) : un module ne référence
/// ni un autre module ni l'API ; seule l'API assemble les modules.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private const string ModulePrefix = "GestionSite.Modules.";

    public static TheoryData<string> ModuleNames => new(Modules.Select(m => m.GetName().Name!));

    private static readonly Assembly[] Modules =
    [
        typeof(IdentiteModule).Assembly,
        typeof(PatrimoineModule).Assembly,
        typeof(MaintenanceModule).Assembly,
        typeof(StockModule).Assembly,
        typeof(BudgetModule).Assembly,
        typeof(PlateformeModule).Assembly,
    ];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void ModuleDoesNotReferenceAnotherModuleOrTheApi(string moduleName)
    {
        var module = Modules.Single(m => m.GetName().Name == moduleName);

        var forbidden = module.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith(ModulePrefix, StringComparison.Ordinal) || n == "GestionSite.Api")
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void ApiAssemblesEveryModule()
    {
        var referenced = typeof(Program).Assembly.GetReferencedAssemblies().Select(r => r.Name).ToHashSet();

        Assert.All(Modules, m => Assert.Contains(m.GetName().Name, referenced));
    }

    [Fact]
    public void EveryModuleProjectIsCoveredByTheseTests()
    {
        var declared = Modules.Select(m => m.GetName().Name).ToHashSet();
        var referencedByApi = typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith(ModulePrefix, StringComparison.Ordinal));

        Assert.All(referencedByApi, n => Assert.Contains(n, declared));
    }
}

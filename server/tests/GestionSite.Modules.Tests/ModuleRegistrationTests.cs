using GestionSite.Modules.Budget;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Maintenance;
using GestionSite.Modules.Patrimoine;
using GestionSite.Modules.Plateforme;
using GestionSite.Modules.Stock;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Tests;

/// <summary>
/// Tests unitaires des points d'entrée des modules ; chaque module y ajoutera ses tests
/// de règles métier (calculs, transitions) sans démarrer l'API.
/// </summary>
public sealed class ModuleRegistrationTests
{
    private static readonly Dictionary<string, Func<IServiceCollection, IServiceCollection>> Registrations = new()
    {
        ["Identite"] = s => s.AddIdentiteModule(),
        ["Patrimoine"] = s => s.AddPatrimoineModule(),
        ["Maintenance"] = s => s.AddMaintenanceModule(),
        ["Stock"] = s => s.AddStockModule(),
        ["Budget"] = s => s.AddBudgetModule(),
        ["Plateforme"] = s => s.AddPlateformeModule(),
    };

    public static TheoryData<string> Modules => new(Registrations.Keys);

    [Theory]
    [MemberData(nameof(Modules))]
    public void RegistrationReturnsTheSameCollectionForChaining(string module)
    {
        var services = new ServiceCollection();

        Assert.Same(services, Registrations[module](services));
    }

    [Fact]
    public async Task EndpointMappingReturnsTheSameBuilderForChaining()
    {
        await using var app = WebApplication.CreateBuilder().Build();

        var mapped = app.MapIdentiteEndpoints()
            .MapPatrimoineEndpoints()
            .MapMaintenanceEndpoints()
            .MapStockEndpoints()
            .MapBudgetEndpoints()
            .MapPlateformeEndpoints();

        Assert.Same(app, mapped);
    }
}

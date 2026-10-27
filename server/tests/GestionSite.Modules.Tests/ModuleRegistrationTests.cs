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
    public static TheoryData<Func<IServiceCollection, IServiceCollection>> Registrations => new()
    {
        s => s.AddIdentiteModule(),
        s => s.AddPatrimoineModule(),
        s => s.AddMaintenanceModule(),
        s => s.AddStockModule(),
        s => s.AddBudgetModule(),
        s => s.AddPlateformeModule(),
    };

    [Theory]
    [MemberData(nameof(Registrations))]
    public void RegistrationReturnsTheSameCollectionForChaining(Func<IServiceCollection, IServiceCollection> register)
    {
        var services = new ServiceCollection();

        Assert.Same(services, register(services));
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

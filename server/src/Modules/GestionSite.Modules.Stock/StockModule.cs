using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Stock;

/// <summary>
/// Point d'entrée du module Stock : articles, mouvements immuables et transferts entre sites.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class StockModule
{
    public static IServiceCollection AddStockModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}

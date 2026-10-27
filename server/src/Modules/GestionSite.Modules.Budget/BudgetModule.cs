using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Budget;

/// <summary>
/// Point d'entrée du module Budget : enveloppes, autorisations, engagements et dépenses.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class BudgetModule
{
    public static IServiceCollection AddBudgetModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}

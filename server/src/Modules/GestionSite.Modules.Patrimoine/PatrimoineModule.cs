using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Patrimoine;

/// <summary>
/// Point d'entrée du module Patrimoine : bâtiments, niveaux, zones, composants, grilles et évaluations.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class PatrimoineModule
{
    public static IServiceCollection AddPatrimoineModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapPatrimoineEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}

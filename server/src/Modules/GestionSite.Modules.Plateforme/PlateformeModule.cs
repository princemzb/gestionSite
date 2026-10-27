using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Plateforme;

/// <summary>
/// Point d'entrée du module Plateforme : organisations clientes, abonnements et autorisations de support.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class PlateformeModule
{
    public static IServiceCollection AddPlateformeModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapPlateformeEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}

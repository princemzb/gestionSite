using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Identite;

/// <summary>
/// Point d'entrée du module Identite : comptes, adhésions aux organisations, rôles et sites autorisés.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class IdentiteModule
{
    public static IServiceCollection AddIdentiteModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapIdentiteEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}

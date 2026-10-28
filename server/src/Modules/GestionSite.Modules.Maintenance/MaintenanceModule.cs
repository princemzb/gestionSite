using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Maintenance;

/// <summary>
/// Point d'entrée du module Maintenance : dossiers, interventions, attentes, inspections et occurrences.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class MaintenanceModule
{
    public static IServiceCollection AddMaintenanceModule(this IServiceCollection services) =>
        services.AddMaintenancePersistence().AddScoped<ServiceDepotPublic>();

    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder endpoints) => endpoints.MapDepotPublic();
}

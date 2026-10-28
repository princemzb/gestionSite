using GestionSite.Modules.Plateforme.PocSynchronisation;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GestionSite.Modules.Plateforme;

/// <summary>
/// Point d'entrée du module Plateforme : organisations clientes, abonnements et autorisations de support.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// Héberge aussi, provisoirement, le POC expérimental PREP-06 (désactivé par défaut).
/// </summary>
public static class PlateformeModule
{
    public static IServiceCollection AddPlateformeModule(this IServiceCollection services)
    {
        services.AddOptions<PocSynchronisationOptions>().BindConfiguration(PocSynchronisationOptions.Section);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ServiceCommandes>();
        services.AddScoped<IDatabaseMigrator, PocSynchronisationMigrator>();
        return services;
    }

    public static IEndpointRouteBuilder MapPlateformeEndpoints(this IEndpointRouteBuilder endpoints) => endpoints.MapPocSynchronisation();
}

using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Maintenance;

/// <summary>
/// Point d'entrée du module Maintenance : dossiers, interventions, attentes, inspections et occurrences.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class MaintenanceModule
{
    public static IServiceCollection AddMaintenanceModule(this IServiceCollection services)
    {
        TraitementPhotos.Configurer();
        services.AddOptions<TurnstileOptions>().BindConfiguration(TurnstileOptions.Section)
            .Validate(o => o.Desactive || !string.IsNullOrWhiteSpace(o.CleSecrete),
                "Turnstile:CleSecrete est requise (Turnstile:Desactive seulement en développement et en test).")
            .ValidateOnStart();
        services.AddHttpClient<VerificationTurnstile>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IVerificationHumaine>(p => p.GetRequiredService<IOptions<TurnstileOptions>>().Value.Desactive
            ? new SansVerification()
            : p.GetRequiredService<VerificationTurnstile>());
        return services.AddMaintenancePersistence().AddLimitesDepotPublic().AddScoped<ServiceDepotPublic>();
    }

    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder endpoints) => endpoints.MapDepotPublic();
}

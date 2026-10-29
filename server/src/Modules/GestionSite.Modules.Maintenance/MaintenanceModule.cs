using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.DepotsRecus;
using GestionSite.Modules.Maintenance.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            .Validate<IHostEnvironment>((o, environnement) => !o.Desactive || environnement.IsDevelopment(),
                "Turnstile:Desactive n'est permis qu'en développement.")
            .ValidateOnStart();
        services.AddHttpClient<VerificationTurnstile>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IVerificationHumaine>(p => p.GetRequiredService<IOptions<TurnstileOptions>>().Value switch
        {
            { Desactive: true } => new SansVerification(),
            { CleSecrete: { Length: > 0 } } => p.GetRequiredService<VerificationTurnstile>(),
            _ => ActivatorUtilities.CreateInstance<VerificationIndisponible>(p),
        });
        services.AddOptions<DepotPublicOptions>().BindConfiguration(DepotPublicOptions.Section)
            .Validate(o => o.LimiteParSite > 0 && o.Fenetre > TimeSpan.Zero, "DepotPublic : limite et fenêtre positives.")
            .ValidateOnStart();
        return services.AddMaintenancePersistence().AddSingleton<LimiteDepots>().AddScoped<ServiceDepotPublic>()
            .AddScoped<ServiceDepotsRecus>();
    }

    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder endpoints) => endpoints.MapDepotPublic().MapDepotsRecus();
}

using GestionSite.Modules.Maintenance.Dangers;
using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.DepotsRecus;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Notifications;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Maintenance.Planificateur;
using GestionSite.Modules.Maintenance.Preventif;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Maintenance;

/// <summary>
/// Point d'entrée du module Maintenance : dossiers, interventions, attentes, inspections, occurrences, centre de notifications
/// et planificateur des rappels.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class MaintenanceModule
{
    public static IServiceCollection AddMaintenanceModule(this IServiceCollection services)
    {
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
        services.AddOptions<PlanificateurOptions>().BindConfiguration(PlanificateurOptions.Section)
            .Validate(o => o.Port is >= 0 and <= 65535, "Planificateur:Port : 0 (aucun port dédié) à 65535.")
            .ValidateOnStart();
        return services.AddMaintenancePersistence().AddSingleton<LimiteDepots>().AddScoped<ServiceDepotPublic>()
            .AddScoped<ServiceDepotsRecus>().AddScoped<ServiceDossiers>().AddScoped<ServiceInterventions>()
            .AddScoped<ServicePropositions>().AddScoped<ServicePlans>().AddScoped<ServiceOccurrences>()
            .AddScoped<ServiceNotifications>().AddScoped<ServiceDangers>().AddScoped<RappelsOrganisation>().AddSingleton<ServicePlanificateur>();
    }

    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapDepotPublic().MapDepotsRecus().MapDossiers().MapPreventif().MapNotifications().MapDangers().MapPlanificateur();

    /// <summary>À placer en tête du pipeline : sur le port dédié au planificateur, seule sa route répond.</summary>
    public static IApplicationBuilder UseMaintenanceModule(this IApplicationBuilder app) => app.RestreindrePortPlanificateur();
}

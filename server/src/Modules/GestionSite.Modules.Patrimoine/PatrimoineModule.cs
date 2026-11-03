using GestionSite.Modules.Patrimoine.Composants;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.Modules.Patrimoine.Etablissement;
using GestionSite.Modules.Patrimoine.Evaluations;
using GestionSite.Modules.Patrimoine.Grilles;
using GestionSite.Modules.Patrimoine.Lecture;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.PlansBatiment;
using GestionSite.Modules.Patrimoine.Score;
using GestionSite.Modules.Patrimoine.Structure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GestionSite.Modules.Patrimoine;

/// <summary>
/// Point d'entrée du module Patrimoine : bâtiments, niveaux, zones, composants, grilles, évaluations et plans de bâtiment.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class PatrimoineModule
{
    public static IServiceCollection AddPatrimoineModule(this IServiceCollection services)
    {
        services.AddOptions<FondsPlanOptions>().BindConfiguration(FondsPlanOptions.Section)
            .Validate(o => o.QuotaFondsOctets > 0, "PlansBatiment:QuotaFondsOctets doit être positif.")
            .Validate(o => o.RenduPdfDelaiSecondes > 0, "PlansBatiment:RenduPdfDelaiSecondes doit être positif.")
            .Validate(o => o.RenduPdfMemoireOctets > 0, "PlansBatiment:RenduPdfMemoireOctets doit être positif.")
            .Validate<IHostEnvironment>(
                (o, environnement) => o.RenduPdfLanceur is { } lanceur ? Path.IsPathFullyQualified(lanceur) && File.Exists(lanceur) : environnement.IsDevelopment(),
                "PlansBatiment:RenduPdfLanceur doit être le chemin absolu d'un fichier existant, obligatoire hors développement (rendu PDF isolé, SOC-05).")
            .ValidateOnStart();
        return services.AddPatrimoinePersistence().AddSingleton<ProcessusRenduPdf>().AddScoped<ServiceStructure>().AddScoped<ServiceComposants>()
            .AddScoped<IInventaireSite, InventaireSite>().AddScoped<IEvaluationsInventaire, EvaluationsInventaire>()
            .AddScoped<ServiceGrilles>().AddScoped<ServiceEvaluations>().AddScoped<ServiceScore>()
            .AddScoped<ServiceEtablissement>().AddScoped<ServicePlansBatiment>().AddScoped<ServiceFondsPlan>();
    }

    public static IEndpointRouteBuilder MapPatrimoineEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapStructure().MapComposants().MapGrilles().MapEvaluations().MapScore().MapEtablissement().MapPlansBatiment();
}

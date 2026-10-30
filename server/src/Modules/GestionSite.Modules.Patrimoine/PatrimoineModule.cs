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

namespace GestionSite.Modules.Patrimoine;

/// <summary>
/// Point d'entrée du module Patrimoine : bâtiments, niveaux, zones, composants, grilles, évaluations et plans de bâtiment.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class PatrimoineModule
{
    public static IServiceCollection AddPatrimoineModule(this IServiceCollection services) =>
        services.AddPatrimoinePersistence().AddScoped<ServiceStructure>().AddScoped<ServiceComposants>()
            .AddScoped<IInventaireSite, InventaireSite>().AddScoped<IEvaluationsInventaire, EvaluationsInventaire>()
            .AddScoped<ServiceGrilles>().AddScoped<ServiceEvaluations>().AddScoped<ServiceScore>()
            .AddScoped<ServiceEtablissement>().AddScoped<ServicePlansBatiment>();

    public static IEndpointRouteBuilder MapPatrimoineEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapStructure().MapComposants().MapGrilles().MapEvaluations().MapScore().MapEtablissement().MapPlansBatiment();
}

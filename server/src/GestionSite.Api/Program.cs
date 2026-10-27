using GestionSite.Api.Infrastructure;
using GestionSite.Modules.Budget;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Maintenance;
using GestionSite.Modules.Patrimoine;
using GestionSite.Modules.Plateforme;
using GestionSite.Modules.Stock;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddIdentiteModule()
    .AddPatrimoineModule()
    .AddMaintenanceModule()
    .AddStockModule()
    .AddBudgetModule()
    .AddPlateformeModule();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Vivacité : le processus répond, sans dépendance. Disponibilité : base et stockage joignables.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(InfrastructureServices.ReadyTag) });
app.MapIdentiteEndpoints()
    .MapPatrimoineEndpoints()
    .MapMaintenanceEndpoints()
    .MapStockEndpoints()
    .MapBudgetEndpoints()
    .MapPlateformeEndpoints();

await app.RunAsync().ConfigureAwait(false);


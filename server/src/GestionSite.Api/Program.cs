using GestionSite.Modules.Budget;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Maintenance;
using GestionSite.Modules.Patrimoine;
using GestionSite.Modules.Plateforme;
using GestionSite.Modules.Stock;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddIdentiteModule()
    .AddPatrimoineModule()
    .AddMaintenanceModule()
    .AddStockModule()
    .AddBudgetModule()
    .AddPlateformeModule();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapIdentiteEndpoints()
    .MapPatrimoineEndpoints()
    .MapMaintenanceEndpoints()
    .MapStockEndpoints()
    .MapBudgetEndpoints()
    .MapPlateformeEndpoints();

await app.RunAsync().ConfigureAwait(false);


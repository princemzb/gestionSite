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
// Disponibilité mise en cache brièvement : des sondes en boucle ne multiplient pas les appels
// facturés (R2) ni les connexions (Neon). Seules les réponses 200 sont mises en cache.
builder.Services.AddOutputCache(options =>
    options.AddPolicy("ready", policy => policy.Expire(TimeSpan.FromSeconds(10))));

var app = builder.Build();

app.UseOutputCache();
app.UseAuthentication();
app.UseAuthorization();

// Vivacité : le processus répond, sans dépendance. Disponibilité : base à jour et stockage joignables.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(InfrastructureServices.ReadyTag) })
    .CacheOutput("ready");
app.MapIdentiteEndpoints()
    .MapPatrimoineEndpoints()
    .MapMaintenanceEndpoints()
    .MapStockEndpoints()
    .MapBudgetEndpoints()
    .MapPlateformeEndpoints();

await app.RunAsync().ConfigureAwait(false);


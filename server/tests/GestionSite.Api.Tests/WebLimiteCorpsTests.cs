using System.Collections.Concurrent;
using System.Net;
using GestionSite.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>
/// Limite du corps des envois de l'application web : l'écran de contrôle la porte par un attribut de sa page (appliqué par le
/// routage, avant l'authentification), les formulaires avec photos par leur chemin. Le serveur de test n'a pas de limite :
/// une limite observable, à 30 Mo comme Kestrel, est posée en tête du pipeline.
/// </summary>
public sealed class WebLimiteCorpsTests : IAsyncLifetime
{
    private const long Kestrel = 30_000_000;
    private const long Mio = 1024 * 1024;
    private readonly ConcurrentQueue<LimiteObservable> _limites = new();
    private WebApplicationFactory<WebOptions> _web = null!;

    public Task InitializeAsync()
    {
        _web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new PoseLimite(_limites))));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _web.DisposeAsync();

    [Theory]
    [InlineData("preventif/controler?site=00000000-0000-0000-0000-000000000001&plan=00000000-0000-0000-0000-000000000002", 44 * Mio)]
    [InlineData("inventaire/evaluer", 34 * Mio)]
    [InlineData("dossiers/00000000-0000-0000-0000-000000000003", 34 * Mio)]
    [InlineData("preventif/plan", Kestrel)]
    public async Task EachFormKeepsItsBodyLimit(string page, long attendue)
    {
        using var navigateur = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var contenu = new FormUrlEncodedContent([new("champ", "valeur")]);

        using var reponse = await navigateur.PostAsync(new Uri($"/organisations/{Guid.NewGuid()}/{page}", UriKind.Relative), contenu);

        Assert.NotEqual(HttpStatusCode.OK, reponse.StatusCode); // sans session : rien n'est traité
        Assert.Equal(attendue, Assert.Single(_limites).MaxRequestBodySize);
    }

    private sealed class LimiteObservable : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = Kestrel;
    }

    private sealed class PoseLimite(ConcurrentQueue<LimiteObservable> limites) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (contexte, suite) =>
            {
                var limite = new LimiteObservable();
                contexte.Features.Set<IHttpMaxRequestBodySizeFeature>(limite);
                limites.Enqueue(limite);
                await suite(contexte);
            });
            next(app);
        };
    }
}

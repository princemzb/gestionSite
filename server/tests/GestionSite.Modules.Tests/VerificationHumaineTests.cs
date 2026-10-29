using System.Net;
using GestionSite.Modules.Maintenance;
using GestionSite.Modules.Maintenance.DepotPublic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Tests;

/// <summary>Anti-robot du dépôt public (S08-03) : Turnstile vérifié côté serveur, refus par défaut.</summary>
public sealed class VerificationHumaineTests
{
    [Theory]
    [InlineData("{\"success\":true}", HttpStatusCode.OK, true)]
    [InlineData("{\"success\":false,\"error-codes\":[\"timeout-or-duplicate\"]}", HttpStatusCode.OK, false)]
    [InlineData("pas du JSON", HttpStatusCode.OK, false)]
    [InlineData("{\"success\":true}", HttpStatusCode.InternalServerError, false)]
    public async Task OnlyAnExplicitSuccessFromCloudflareAccepts(string reponse, HttpStatusCode statut, bool attendu)
    {
        var cloudflare = new CloudflareSimule(statut, reponse);

        var accepte = await Verification(cloudflare).VerifierAsync("jeton-du-navigateur", CancellationToken.None);

        Assert.Equal(attendu, accepte);
        Assert.Equal(VerificationTurnstile.Adresse, cloudflare.Adresse);
        Assert.Equal("secret=cle-secrete&response=jeton-du-navigateur", cloudflare.Corps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task AMissingTokenIsRefusedWithoutCallingCloudflare(string? jeton)
    {
        var cloudflare = new CloudflareSimule(HttpStatusCode.OK, "{\"success\":true}");

        Assert.False(await Verification(cloudflare).VerifierAsync(jeton, CancellationToken.None));
        Assert.Null(cloudflare.Adresse);
    }

    [Fact]
    public async Task AnUnreachableCloudflareRefuses()
    {
        var cloudflare = new CloudflareSimule(HttpStatusCode.OK, "") { Panne = true };

        Assert.False(await Verification(cloudflare).VerifierAsync("jeton", CancellationToken.None));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void DisablingTheCheckIsOnlyAllowedInDevelopment(string environnement, bool permis)
    {
        using var fournisseur = Fournisseur(new() { ["Turnstile:Desactive"] = "true" }, environnement);

        var lecture = Record.Exception(() => fournisseur.GetRequiredService<IOptions<TurnstileOptions>>().Value);

        Assert.Equal(permis, lecture is null);
    }

    [Fact]
    public async Task WithoutTheSecretDepositsAreUnavailableRatherThanUnchecked()
    {
        using var fournisseur = Fournisseur([], "Production");
        using var portee = fournisseur.CreateScope();

        var verification = portee.ServiceProvider.GetRequiredService<IVerificationHumaine>();

        await Assert.ThrowsAsync<DepotIndisponibleException>(() => verification.VerifierAsync("jeton", CancellationToken.None));
    }

    private static VerificationTurnstile Verification(CloudflareSimule cloudflare) => new(
        new HttpClient(cloudflare), Options.Create(new TurnstileOptions { CleSecrete = "cle-secrete" }), NullLogger<VerificationTurnstile>.Instance);

    private static ServiceProvider Fournisseur(Dictionary<string, string?> reglages, string environnement) => new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(reglages).Build())
        .AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environnement })
        .AddLogging()
        .AddMaintenanceModule()
        .BuildServiceProvider();

    private sealed class CloudflareSimule(HttpStatusCode statut, string reponse) : HttpMessageHandler
    {
        public bool Panne { get; init; }

        public Uri? Adresse { get; private set; }

        public string? Corps { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Panne)
            {
                throw new HttpRequestException("injoignable");
            }

            Adresse = request.RequestUri;
            Corps = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statut) { Content = new StringContent(reponse) };
        }
    }
}

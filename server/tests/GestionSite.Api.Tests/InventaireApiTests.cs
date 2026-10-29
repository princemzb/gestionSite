using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>Inventaire d'un site (US-07) sur l'API et PostgreSQL réels : organisation, site, technicien et pasteur prêts.</summary>
public abstract class InventaireApiTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    protected const string MotDePasse = "un-mot-de-passe-solide";
    private protected TestDatabase Database { get; private set; } = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    protected Guid Organisation { get; private set; }
    protected Guid Site { get; private set; }
    protected string Base { get; private set; } = null!;
    private string? _activationAdministrateur;

    public async Task InitializeAsync()
    {
        Database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(Database);
        _client = _api.CreateClient();
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, "Saint-Paul", "admin@inventaire.test", "Admin", CancellationToken.None, premierSite: "Site centre");
        Organisation = ouverte.OrganisationId;
        _activationAdministrateur = ouverte.JetonActivation;
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(Organisation);
        Site = await scope.ServiceProvider.GetRequiredService<IdentiteDbContext>().Sites.Select(s => s.Id).SingleAsync();
        Base = $"/organizations/{Organisation}/sites/{Site}/inventory";
        await MembreAsync("technicien@inventaire.test", Role.Technicien);
        await MembreAsync("pasteur@inventaire.test", Role.Pasteur);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await Database.DisposeAsync();
    }

    protected static async Task<string?> ChampAsync(HttpResponseMessage reponse)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("champ").GetString();
    }

    protected async Task<Guid> CreerAsync(string chemin, object corps, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, chemin, jeton, corps);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    protected async Task<HttpResponseMessage> EnvoyerAsync(HttpMethod methode, string chemin, string jeton, object? corps = null)
    {
        using var demande = new HttpRequestMessage(methode, new Uri(chemin, UriKind.Relative));
        demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        if (corps is not null)
        {
            demande.Content = JsonContent.Create(corps);
        }

        return await _client.SendAsync(demande);
    }

    protected async Task<string> JetonAsync(string email)
    {
        if (email.StartsWith("admin@", StringComparison.Ordinal) && _activationAdministrateur is { } jeton)
        {
            // Activation réelle : elle accepte aussi l'adhésion de l'administrateur.
            using var activation = await _client.PostAsJsonAsync("/auth/activate", new { jeton, motDePasse = MotDePasse });
            _activationAdministrateur = null;
        }

        using var reponse = await _client.PostAsJsonAsync("/auth/login", new { email, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jetonAcces").GetString()!;
    }

    private async Task MembreAsync(string email, Role role)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(Organisation);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var compte = new Compte { Id = Guid.NewGuid(), UserName = email, Email = email };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse)).Succeeded);
        var membre = new Membre(Guid.NewGuid(), Organisation, compte.Id, DateTimeOffset.UtcNow);
        membre.Reviser([(role, await context.Sites.SingleAsync(s => s.Id == Site))]);
        membre.Accepter(DateTimeOffset.UtcNow);
        context.Add(membre);
        await context.SaveChangesAsync();
    }
}

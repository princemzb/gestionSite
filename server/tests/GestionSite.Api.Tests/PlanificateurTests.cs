using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Maintenance.Planificateur;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Planificateur des rappels (US-10, S10-02, S10-03), sur une base réelle sous RLS, à horloge contrôlée (instant du passage
/// donné au service) : seuils DEC-05 avec le délai de prise en charge depuis la qualification (Q7-A), rejeu sans doublon,
/// passages simultanés, isolation des organisations, fonction SECURITY DEFINER, réexamen et report, point d'entrée interne.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class PlanificateurTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    // Valeur de test non sensible (au moins 32 caractères).
    private const string Jeton = "jeton-de-test-du-planificateur-non-sensible";

    private string Dossiers => Base.Replace("/inventory", "/cases", StringComparison.Ordinal);

    [Fact]
    public async Task AnUnassignedUrgentCaseRemindsManagersOnceAt75PercentEscalatesAt100PercentThenDailyWithThePersonInCharge()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Pasteur, zone);
        await QualifierAsync(dossier, zone, equipe.Responsable, "Urgente");
        var qualifie = (await LireAsync($"{Dossiers}/{dossier}", equipe.Responsable)).GetProperty("qualifieLe").GetDateTimeOffset();

        var avant = await PasserAsync(qualifie.AddHours(3).AddMinutes(-1));
        var a75 = await PasserAsync(qualifie.AddHours(3));
        var rejeu = await PasserAsync(qualifie.AddHours(3).AddMinutes(5));
        var a100 = await PasserAsync(qualifie.AddHours(4));
        using (var affecte = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", equipe.Responsable,
            new { titre = "Remplacer le bloc", intervenantCompteId = await CompteAsync(equipe.Technicien), version = await VersionAsync(dossier, equipe.Responsable) }))
        {
            Assert.Equal(HttpStatusCode.OK, affecte.StatusCode);
        }

        var lendemain = await PasserAsync(qualifie.AddDays(1).AddHours(4));

        Assert.Equal((0, qualifie.AddHours(3)), (avant.Notifications, avant.ProchainPassage));
        Assert.Equal(2, a75.Notifications); // responsable et délégué (non affecté), même le responsable qui a qualifié
        Assert.Equal(0, rejeu.Notifications); // T-04.11 : rejouer n'écrit rien
        Assert.Equal(2, a100.Notifications);
        Assert.Equal(qualifie.AddDays(1).AddHours(4), a100.ProchainPassage);
        Assert.Equal(3, lendemain.Notifications); // R/D et le technicien désormais en charge
        Assert.Equal(["PriseEnChargeDepassee", "PriseEnChargeDepassee", "PriseEnChargeProche"], await TypesAsync(equipe.Responsable, "Prise"));
        Assert.Equal(["PriseEnChargeDepassee", "Affectation"], await TypesAsync(equipe.Technicien, null));
        Assert.Empty(await TypesAsync(equipe.Pasteur, null)); // pasteur et coordination ne sont pas relancés
        Assert.Equal(7L, await Database.CountAsync("SELECT count(*) FROM maintenance.notifications WHERE cle LIKE 'rappel:pec:%'"));
    }

    [Fact]
    public async Task SimultaneousPassesWriteEachReminderOnceAndACaseLeftToQualifyRemindsManagersAfter24Hours()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Technicien, zone);
        var recu = (await LireAsync($"{Dossiers}/{dossier}", equipe.Responsable)).GetProperty("recuLe").GetDateTimeOffset();

        var passages = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PasserAsync(recu.AddHours(24))));

        Assert.Equal(2, passages.Sum(p => p.Notifications)); // Q8-A : R/D, une seule fois malgré quatre passages simultanés
        Assert.Equal(["AQualifier"], await TypesAsync(equipe.Delegue, null));
        Assert.All(passages, p => Assert.Equal(recu.AddHours(48), p.ProchainPassage ?? recu.AddHours(48)));
    }

    [Fact]
    public async Task EachOrganisationIsProcessedUnderItsOwnIsolationAndTheFunctionOnlyGivesIdentifiers()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Technicien, zone);
        var recu = (await LireAsync($"{Dossiers}/{dossier}", equipe.Responsable)).GetProperty("recuLe").GetDateTimeOffset();
        var autre = await IdentiteModule.OuvrirOrganisationAsync(ApiServices, "Saint-Pierre", "admin@autre.test", "Admin", CancellationToken.None, premierSite: "Site nord");
        var sansDossier = await IdentiteModule.OuvrirOrganisationAsync(ApiServices, "Saint-Jean", "admin@vide.test", "Admin", CancellationToken.None, premierSite: "Site sud");
        var code = await CodePublicAsync(autre.OrganisationId);
        using (var depot = await EnvoyerSansJetonAsync($"/public/sites/{code}/reports",
            new { commandeId = Guid.NewGuid(), nature = "Incident", description = "Gouttière percée", emplacement = "Parvis", danger = false, activiteBloquee = false, nom = "Paul", contact = "—" }))
        {
            Assert.Equal(HttpStatusCode.OK, depot.StatusCode);
        }

        var bilan = await PasserAsync(recu.AddHours(25));

        // Deux organisations avec un dossier ouvert (celle sans dossier n'est pas parcourue) ; chacune sous sa propre RLS :
        // les R/D de la première pour son dossier, personne dans l'autre (aucun R/D sur son site).
        Assert.Equal((2, 2, 0), (bilan.Organisations, bilan.Notifications, bilan.Echecs));
        Assert.Equal(0L, await Database.CountAsync("SELECT count(*) FROM maintenance.notifications WHERE organisation_id <> @o", ("o", Organisation)));

        await using var application = new NpgsqlConnection(Database.ApplicationConnectionString);
        await application.OpenAsync();
        var organisations = await GuidsAsync(application, "SELECT o FROM maintenance.organisations_a_planifier() AS o");
        Assert.Equal([autre.OrganisationId, Organisation], organisations.Order());
        Assert.DoesNotContain(sansDossier.OrganisationId, organisations);
        // La variable du planificateur ne sert à rien au rôle applicatif, ni au propriétaire connecté directement.
        Assert.Equal(0L, await CompterAsync(application, "SET app.planificateur = 'on'; SELECT count(*) FROM maintenance.signalements"));
        await using var proprietaire = new NpgsqlConnection(Database.MigrationsConnectionString);
        await proprietaire.OpenAsync();
        Assert.Equal(0L, await CompterAsync(proprietaire, "SET app.planificateur = 'on'; SELECT count(*) FROM maintenance.signalements"));
        var refus = await Assert.ThrowsAsync<PostgresException>(() => CompterAsync(application, "DROP FUNCTION maintenance.organisations_a_planifier()"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState);
    }

    [Fact]
    public async Task AReviewDateRemindsThePersonInChargeAt8ThenAReportIsTracedAndRestartsTheCycleUpToTheManagersEscalation()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Pasteur, zone);
        await QualifierAsync(dossier, zone, equipe.Responsable, "Basse");
        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", equipe.Responsable,
            new { titre = "Commander la pièce", intervenantCompteId = await CompteAsync(equipe.Technicien), version = await VersionAsync(dossier, equipe.Responsable) }))
        {
        }

        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/start", equipe.Technicien, new { version = await VersionAsync(dossier, equipe.Responsable) }))
        {
        }

        var aujourdhui = JourCivil.Du(DateTimeOffset.UtcNow);
        using (var attente = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/hold", equipe.Technicien,
            new { motif = "Livraison", reexamenLe = aujourdhui.AddDays(2), version = await VersionAsync(dossier, equipe.Responsable) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, attente.StatusCode);
        }

        var jour = await PasserAsync(JourCivil.A(aujourdhui.AddDays(2), new TimeOnly(8, 0)));
        using var parLePasteur = await ReporterAsync(dossier, equipe.Pasteur, aujourdhui.AddDays(5), "Pièce en retard", equipe.Responsable);
        using var sansCommentaire = await ReporterAsync(dossier, equipe.Technicien, aujourdhui.AddDays(5), null, equipe.Responsable);
        using var report = await ReporterAsync(dossier, equipe.Technicien, aujourdhui.AddDays(5), "Pièce en retard chez le fournisseur", equipe.Responsable);
        var fiche = await LireAsync($"{Dossiers}/{dossier}", equipe.Responsable);
        var escalade = await PasserAsync(JourCivil.A(aujourdhui.AddDays(8), new TimeOnly(8, 0)));
        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/resume", equipe.Technicien, new { version = await VersionAsync(dossier, equipe.Responsable) }))
        {
        }

        using var horsAttente = await ReporterAsync(dossier, equipe.Technicien, aujourdhui.AddDays(9), "Encore", equipe.Responsable);
        var apresReprise = await PasserAsync(JourCivil.A(aujourdhui.AddDays(15), new TimeOnly(8, 0)));

        Assert.Equal(1, jour.Notifications); // le technicien en charge seulement
        Assert.Equal(HttpStatusCode.Forbidden, parLePasteur.StatusCode);
        Assert.Equal("commentaire", await ChampAsync(sansCommentaire));
        Assert.Equal(HttpStatusCode.NoContent, report.StatusCode);
        var intervention = fiche.GetProperty("interventions")[0];
        Assert.Equal((aujourdhui.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), 1),
            (intervention.GetProperty("reexamenLe").GetString(), intervention.GetProperty("reportsReexamen").GetInt32()));
        var trace = fiche.GetProperty("historique").EnumerateArray().First(e => e.GetProperty("nature").GetString() == "ReportReexamen").GetProperty("detail");
        Assert.Equal(aujourdhui.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), trace.GetProperty("ancienne").GetString());
        Assert.Equal("Pièce en retard chez le fournisseur", trace.GetProperty("commentaire").GetString());
        Assert.Equal(2, escalade.Notifications); // J+3 de la nouvelle date : R/D
        Assert.Equal(["ReexamenDepasse"], await TypesAsync(equipe.Delegue, "Reexamen"));
        Assert.Equal(["ReexamenAtteint"], await TypesAsync(equipe.Technicien, "Reexamen"));
        Assert.Equal(HttpStatusCode.Conflict, horsAttente.StatusCode);
        Assert.Equal(0L, await Database.CountAsync(
            "SELECT count(*) FROM maintenance.notifications WHERE type LIKE 'Reexamen%' AND cree_le > @apres", ("apres", JourCivil.A(aujourdhui.AddDays(9), new TimeOnly(8, 0)))));
        Assert.Equal(0, apresReprise.Echecs);
    }

    [Fact]
    public async Task TheInternalEntryPointAnswersOnlyWithItsTokenAndOnlyItsRouteAnswersOnTheDedicatedPort()
    {
        await using var sansJeton = Infrastructure.CreateApi(Database);
        await using var avecJeton = Infrastructure.CreateApi(Database).WithWebHostBuilder(b => b.UseSetting("Planificateur:Jeton", Jeton));
        await using var avecPort = Infrastructure.CreateApi(Database).WithWebHostBuilder(b => b
            .UseSetting("Planificateur:Jeton", Jeton).UseSetting("Planificateur:Port", "8082")
            .ConfigureServices(s => s.AddTransient<IStartupFilter, PortSimule>()));

        using var desactive = await AppelerAsync(sansJeton, Jeton);
        using var sansEntete = await AppelerAsync(avecJeton, jeton: null);
        using var mauvais = await AppelerAsync(avecJeton, Jeton + "x");
        using var bon = await AppelerAsync(avecJeton, Jeton);
        using var autrePort = await AppelerAsync(avecPort, Jeton);
        using var portDedie = await AppelerAsync(avecPort, Jeton, port: "8082");
        using var autreRouteSurLePort = await EnvoyerSurPortAsync(avecPort, "/health", "8082");
        using var autreRouteAilleurs = await EnvoyerSurPortAsync(avecPort, "/health", port: null);

        Assert.Equal(HttpStatusCode.NotFound, desactive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sansEntete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mauvais.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bon.StatusCode);
        var bilan = await bon.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["organisations", "notifications", "echecs", "prochainPassage"], bilan.EnumerateObject().Select(p => p.Name));
        Assert.Equal(HttpStatusCode.NotFound, autrePort.StatusCode); // port dédié exigé
        Assert.Equal(HttpStatusCode.OK, portDedie.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreRouteSurLePort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, autreRouteAilleurs.StatusCode);
    }

    [Fact]
    public void AShortTokenOrAnInvalidPortIsRefusedAtStartup()
    {
        using var court = Infrastructure.CreateApi(Database).WithWebHostBuilder(b => b.UseSetting("Planificateur:Jeton", "trop-court"));
        using var port = Infrastructure.CreateApi(Database).WithWebHostBuilder(b => b.UseSetting("Planificateur:Jeton", Jeton).UseSetting("Planificateur:Port", "70000"));

        Assert.Contains("32 caractères", Assert.ThrowsAny<Exception>(() => court.CreateClient()).ToString(), StringComparison.Ordinal);
        Assert.Contains("65535", Assert.ThrowsAny<Exception>(() => port.CreateClient()).ToString(), StringComparison.Ordinal);
    }

    // Port local simulé (TestServer n'en a pas) : en-tête de test lu avant tout le pipeline de l'application.
    private sealed class PortSimule : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, suite) =>
            {
                if (int.TryParse(http.Request.Headers["X-Test-Port"], out var port))
                {
                    http.Connection.LocalPort = port;
                }

                await suite(http);
            });
            next(app);
        };
    }

    private static async Task<HttpResponseMessage> AppelerAsync(WebApplicationFactory<Program> api, string? jeton, string? port = null)
    {
        using var client = api.CreateClient();
        using var demande = new HttpRequestMessage(HttpMethod.Post, PlanificateurEndpoints.Route);
        if (jeton is not null)
        {
            demande.Headers.Add(PlanificateurEndpoints.EnteteJeton, jeton);
        }

        if (port is not null)
        {
            demande.Headers.Add("X-Test-Port", port);
        }

        return await client.SendAsync(demande);
    }

    private static async Task<HttpResponseMessage> EnvoyerSurPortAsync(WebApplicationFactory<Program> api, string chemin, string? port)
    {
        using var client = api.CreateClient();
        using var demande = new HttpRequestMessage(HttpMethod.Get, chemin);
        if (port is not null)
        {
            demande.Headers.Add("X-Test-Port", port);
        }

        return await client.SendAsync(demande);
    }

    private Task<BilanPassage> PasserAsync(DateTimeOffset maintenant) =>
        ApiServices.GetRequiredService<ServicePlanificateur>().PasserAsync(maintenant, CancellationToken.None);

    private async Task<HttpResponseMessage> ReporterAsync(Guid dossier, string jeton, DateOnly nouvelle, string? commentaire, string lecteur)
    {
        var version = await VersionAsync(dossier, lecteur);
        return await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/review-date", jeton, new { reexamenLe = nouvelle, commentaire, version });
    }

    private async Task<List<string?>> TypesAsync(string jeton, string? prefixe) =>
        [.. (await LireAsync($"/organizations/{Organisation}/me/notifications", jeton)).GetProperty("notifications").EnumerateArray()
            .Select(n => n.GetProperty("type").GetString()).Where(t => prefixe is null || t!.StartsWith(prefixe, StringComparison.Ordinal))];

    private async Task<string> CodePublicAsync(Guid organisation)
    {
        await using var connexion = new NpgsqlConnection(Database.ConnectionString);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand("SELECT code FROM identite.codes_sites_publics WHERE organisation_id = @o", connexion);
        commande.Parameters.AddWithValue("o", organisation);
        return (string)(await commande.ExecuteScalarAsync())!;
    }

    private static async Task<List<Guid>> GuidsAsync(NpgsqlConnection connexion, string sql)
    {
        await using var commande = new NpgsqlCommand(sql, connexion);
        await using var lecteur = await commande.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await lecteur.ReadAsync())
        {
            ids.Add(lecteur.GetGuid(0));
        }

        return ids;
    }

    private static async Task<long> CompterAsync(NpgsqlConnection connexion, string sql)
    {
        await using var commande = new NpgsqlCommand(sql, connexion);
        await using var lecteur = await commande.ExecuteReaderAsync();
        do
        {
            if (lecteur.FieldCount > 0 && await lecteur.ReadAsync())
            {
                return lecteur.GetInt64(0);
            }
        }
        while (await lecteur.NextResultAsync());

        return -1;
    }

    private sealed record Equipe(string Responsable, string Delegue, string Technicien, string Pasteur);

    private async Task<Equipe> EquipeAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        await MembreAsync("delegue@inventaire.test", Role.Delegue);
        await MembreAsync("coordination@inventaire.test", Role.Coordination);
        return new Equipe(
            await JetonAsync("responsable@inventaire.test"), await JetonAsync("delegue@inventaire.test"), await JetonAsync("technicien@inventaire.test"),
            await JetonAsync("pasteur@inventaire.test"));
    }

    private async Task<Guid> ZoneAsync(string jeton)
    {
        var batiment = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, jeton);
        return (await LireAsync($"{Base}/structure", jeton)).EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batiment)
            .GetProperty("zones")[0].GetProperty("id").GetGuid();
    }

    private Task<Guid> DeclarerAsync(string jeton, Guid zone) =>
        CreerAsync(Dossiers, new { commandeId = Guid.NewGuid(), zoneId = zone, description = "Bloc de secours éteint", danger = false, activiteBloquee = false }, jeton);

    private async Task QualifierAsync(Guid dossier, Guid zone, string jeton, string priorite)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", jeton,
            new { priorite, zoneId = zone, version = await VersionAsync(dossier, jeton) });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<uint> VersionAsync(Guid dossier, string jeton) => (await LireAsync($"{Dossiers}/{dossier}", jeton)).GetProperty("version").GetUInt32();

    private async Task<Guid> CompteAsync(string jeton) => (await LireAsync("/me", jeton)).GetProperty("compteId").GetGuid();

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>
/// Éditeur de plans de bâtiment (US-21, S21-07) : fond calibré enregistré avec la version (cote de calibration et de
/// contrôle), contrôles du serveur, et droits de l'appelant pour afficher les commandes d'édition. Images synthétiques.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class PlansBatimentCalibrationTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Plans => $"{Base}/building-plans";

    [Fact]
    public async Task ACalibratedFondIsSavedWithTheVersionAndKeptByARestore()
    {
        var (responsable, plan, _) = await PlanAsync();
        var fond = await FondAsync(plan, responsable);
        // Fond de 1 600 × 900 px : 1 200 px pour 12,00 m (1 cm par pixel), contrôle de 700 px pour 7,03 m (écart 0,4 %).
        // Recalé sur le niveau inférieur (S21-08) : coin haut gauche du fond en (-1,254 m ; 3,006 m), au centimètre.
        var calibre = Fond(fond, Cote((100, 100), (1300, 100), 12), Cote((100.004, 100), (100, 800.006), 7.03), (-1.254, 3.006));

        using var premiere = await EnregistrerAsync(plan, responsable, 0, calibre);
        using var deuxieme = await EnregistrerAsync(plan, responsable, 1, null); // repasse au plan vierge
        using var restauree = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/versions/1/restore", responsable,
            new { commandeId = Guid.NewGuid(), version = 2, motif = "Retour au fond calibré" });
        var v1 = await LireAsync($"{Plans}/{plan}/versions/1", responsable);
        var v2 = await LireAsync($"{Plans}/{plan}/versions/2", responsable);
        var v3 = await LireAsync($"{Plans}/{plan}", responsable);

        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deuxieme.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restauree.StatusCode);
        var lu = v1.GetProperty("contenu").GetProperty("fond");
        Assert.Equal(fond, lu.GetProperty("fondId").GetGuid());
        Assert.Equal(12, lu.GetProperty("calibration").GetProperty("distance").GetDouble());
        Assert.Equal(1300, lu.GetProperty("calibration").GetProperty("a").GetProperty("x").GetDouble());
        Assert.Equal(100, lu.GetProperty("controle").GetProperty("de").GetProperty("x").GetDouble()); // au centième de pixel
        Assert.Equal(800.01, lu.GetProperty("controle").GetProperty("a").GetProperty("y").GetDouble());
        Assert.Equal(-1.25, lu.GetProperty("decalage").GetProperty("x").GetDouble());
        Assert.Equal(3.01, lu.GetProperty("decalage").GetProperty("y").GetDouble());
        Assert.Equal(JsonValueKind.Null, v2.GetProperty("contenu").GetProperty("fond").ValueKind);
        Assert.Equal(lu.GetRawText(), v3.GetProperty("contenu").GetProperty("fond").GetRawText());
    }

    [Fact]
    public async Task TheFondAndItsDimensionsAreCheckedByTheServer()
    {
        var (responsable, plan, autrePlan) = await PlanAsync();
        var fond = await FondAsync(plan, responsable);
        var fondAilleurs = await FondAsync(autrePlan, responsable);
        var calibration = Cote((100, 100), (1300, 100), 12);

        var refus = new Dictionary<string, object?>
        {
            ["contenu.fond.fondId"] = Fond(fondAilleurs, calibration, null), // fond d'un autre plan
            ["contenu.fond.calibration"] = Fond(fond, null, null), // calibration obligatoire
            ["contenu.fond.calibration.de"] = Fond(fond, Cote((-1, 100), (1300, 100), 12), null), // hors de l'image
            ["contenu.fond.calibration.a"] = Fond(fond, Cote((100, 100), (105, 108), 12), null), // moins de 10 px
            ["contenu.fond.calibration.distance"] = Fond(fond, Cote((100, 100), (1300, 100), 0.1), null),
            ["contenu.fond.controle.a"] = Fond(fond, calibration, Cote((100, 100), (100, 901), 7)), // au-delà des 900 px
            ["contenu.fond.decalage"] = Fond(fond, calibration, null, (5000.01, 0)), // hors du repère (±5 km)
        };
        var reponses = new Dictionary<string, string?>();
        foreach (var (champ, contenu) in refus)
        {
            using var reponse = await EnregistrerAsync(plan, responsable, 0, contenu);
            reponses[champ] = await ChampAsync(reponse);
        }

        // Image de 1 600 px à 4,17 m par pixel : plus de 5 km, hors du repère.
        using var tropGrand = await EnregistrerAsync(plan, responsable, 0, Fond(fond, Cote((100, 100), (1300, 100), 5000), null));
        using var sansFondId = await EnregistrerAsync(plan, responsable, 0, new { calibration });
        using var decalageIncomplet = await EnregistrerAsync(plan, responsable, 0, new { fondId = fond, calibration, decalage = new { x = 1 } });

        Assert.All(refus.Keys, champ => Assert.Equal(champ, reponses[champ]));
        Assert.Equal("contenu.fond.calibration.distance", await ChampAsync(tropGrand));
        Assert.Equal("contenu.fond.fondId", await ChampAsync(sansFondId));
        Assert.Equal("contenu.fond.decalage", await ChampAsync(decalageIncomplet));
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM patrimoine.versions_plan_batiment"));
    }

    [Fact]
    public async Task TheRightsOfTheCallerFollowRolesAndTheNominativeRight()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var admin = await JetonAsync("admin@inventaire.test");
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var droits = $"{Plans}/rights";

        var parResponsable = await LireAsync(droits, responsable);
        var avantAccord = await LireAsync(droits, technicien);
        var membres = await LireAsync($"/organizations/{Organisation}/members", admin);
        var membre = membres.EnumerateArray().Single(m => m.GetProperty("email").GetString() == "technicien@inventaire.test").GetProperty("membreId").GetGuid();
        using var accord = await EnvoyerAsync(HttpMethod.Put, $"/organizations/{Organisation}/members/{membre}/rights/ModifierPlans/sites/{Site}", admin,
            new { motif = "Relevé du rez-de-chaussée" });
        var apresAccord = await LireAsync(droits, technicien);
        using var parAdmin = await EnvoyerAsync(HttpMethod.Get, droits, admin); // aucun rôle sur le site : aucun accès aux plans
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/building-plans/rights", responsable);
        using var sansSession = await EnvoyerAsync(HttpMethod.Get, droits, "jeton-invalide");

        Assert.True(parResponsable.GetProperty("modifierPlans").GetBoolean());
        Assert.False(avantAccord.GetProperty("modifierPlans").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, accord.StatusCode);
        Assert.True(apresAccord.GetProperty("modifierPlans").GetBoolean());
        Assert.NotEqual(HttpStatusCode.OK, parAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sansSession.StatusCode);
    }

    // Église à deux niveaux, un plan par niveau ; un responsable sur le site.
    private async Task<(string Responsable, Guid Plan, Guid AutrePlan)> PlanAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var rdc = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        var tribune = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "Tribune", ordre = 1 }, technicien);
        return (responsable, await CreerAsync(Plans, new { batimentId = eglise, niveauId = rdc }, responsable),
            await CreerAsync(Plans, new { batimentId = eglise, niveauId = tribune }, responsable));
    }

    // Fond synthétique de 1 600 × 900 px (dérivé d'affichage identique).
    private async Task<Guid> FondAsync(Guid plan, string jeton)
    {
        var partie = new ByteArrayContent(FondsPlanBatimentTests.JpegAvecPosition());
        partie.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var formulaire = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(new { commandeId = Guid.NewGuid() })), "demande" },
            { partie, "fichier", "plan.jpg" },
        };
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/backgrounds", jeton, formulaire);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> EnregistrerAsync(Guid plan, string jeton, int version, object? fond) =>
        EnvoyerAsync(HttpMethod.Put, $"{Plans}/{plan}", jeton, new { commandeId = Guid.NewGuid(), version, contenu = new { schema = 1, fond } });

    private static object Fond(Guid fondId, object? calibration, object? controle, (double X, double Y)? decalage = null) =>
        new { fondId, calibration, controle, decalage = decalage is { } d ? new { x = d.X, y = d.Y } : null };

    private static object Cote((double X, double Y) de, (double X, double Y) a, double distance) =>
        new { de = new { x = de.X, y = de.Y }, a = new { x = a.X, y = a.Y }, distance };

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

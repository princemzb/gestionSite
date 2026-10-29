using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GestionSite.Api.Tests;

/// <summary>Composants de l'inventaire (US-07, S07-02) : individuels et lots, version, retrait, historique.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class InventaireComposantsTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    [Fact]
    public async Task ComponentsAreListedFilteredAndJournaled()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (eglise, communes) = await BatimentAsync("Église", technicien);
        var (_, parking) = await BatimentAsync("Extérieurs", technicien);
        var sacristie = await CreerAsync($"{Base}/zones", new { nom = "Sacristie", batimentId = eglise }, technicien);

        var extincteurs = await CreerAsync($"{Base}/components", new
        {
            nom = "Extincteurs eau 6 L",
            corpsEtat = "SecuriteIncendie",
            zoneId = communes,
            marque = "Sicli",
            modele = "EP6",
            anneeMiseEnService = 2021,
            moisMiseEnService = 3,
            lot = new { unite = "Unite", quantite = 6, repartition = new { bon = 5, mauvais = 1 } },
        }, technicien);
        await CreerAsync($"{Base}/components", new { nom = "Armoire à vêtements", corpsEtat = "Mobilier", zoneId = sacristie }, technicien);
        await CreerAsync($"{Base}/components", new { nom = "Portail", corpsEtat = "ExterieursVoirie", zoneId = parking }, technicien);

        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var tous = await ListerAsync("", pasteur);
        var lot = tous.Single(c => c.GetProperty("id").GetGuid() == extincteurs);

        Assert.Equal(["Armoire à vêtements", "Extincteurs eau 6 L", "Portail"], tous.Select(c => c.GetProperty("nom").GetString()));
        Assert.True(lot.GetProperty("soumisAVerification").GetBoolean()); // proposé pour la sécurité incendie
        Assert.Equal(6, lot.GetProperty("lot").GetProperty("quantite").GetInt32());
        Assert.Equal(1, lot.GetProperty("lot").GetProperty("repartition").GetProperty("mauvais").GetInt32());
        Assert.Equal(["Extincteurs eau 6 L"], Noms(await ListerAsync("?verification=true", pasteur)));
        Assert.Equal(["Armoire à vêtements", "Extincteurs eau 6 L"], Noms(await ListerAsync($"?batimentId={eglise}", pasteur)));
        Assert.Equal(["Armoire à vêtements"], Noms(await ListerAsync($"?zoneId={sacristie}&corpsEtat=Mobilier", pasteur)));
        Assert.Equal(["Extincteurs eau 6 L"], Noms(await ListerAsync("?recherche=sicli", pasteur)));
        Assert.Empty(await ListerAsync("?recherche=%25", pasteur)); // « % » littéral, pas un joker
        Assert.Equal(3, (await ListerAsync("?recherche=%20&corpsEtat=", pasteur)).Length); // filtres vides : aucun filtre
        using var comptes = await EnvoyerAsync(HttpMethod.Get, $"{Base}/components/counts", pasteur);
        var parZone = (await comptes.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .ToDictionary(c => c.GetProperty("zoneId").GetGuid(), c => c.GetProperty("actifs").GetInt32());
        Assert.Equal(new Dictionary<Guid, int> { [communes] = 1, [sacristie] = 1, [parking] = 1 }, parZone);
        Assert.Equal(3, await Database.CountAsync(
            "SELECT count(*) FROM patrimoine.journal_inventaire WHERE cible = 'Composant' AND nature = 'Creation' AND auteur_compte_id IS NOT NULL"));
    }

    [Fact]
    public async Task AChangeAtTheReadVersionJournalsOnlyChangedFieldsAndAStaleOneIsRefused()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (_, communes) = await BatimentAsync("Église", technicien);
        var chaises = await CreerAsync($"{Base}/components", Chaises(communes, version: null, bon: 40), technicien);
        var version = await VersionAsync(chaises, technicien);

        using var modifie = await EnvoyerAsync(HttpMethod.Put, $"{Base}/components/{chaises}", technicien, Chaises(communes, version, bon: 32, degrade: 8));
        using var perime = await EnvoyerAsync(HttpMethod.Put, $"{Base}/components/{chaises}", technicien, Chaises(communes, version, bon: 30, degrade: 10));
        var nouvelle = await VersionAsync(chaises, technicien);
        using var identique = await EnvoyerAsync(HttpMethod.Put, $"{Base}/components/{chaises}", technicien, Chaises(communes, nouvelle, bon: 32, degrade: 8));

        Assert.Equal(HttpStatusCode.NoContent, modifie.StatusCode);
        Assert.Equal("modifie_entre_temps", await CodeAsync(perime, HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.NoContent, identique.StatusCode);
        Assert.Equal(nouvelle, await VersionAsync(chaises, technicien)); // sans changement, rien n'est écrit
        Assert.Equal(1, await Database.CountAsync(
            "SELECT count(*) FROM patrimoine.journal_inventaire WHERE nature = 'Modification' " +
            "AND avant = '{\"bon\": 40, \"degrade\": 0}'::jsonb AND apres = '{\"bon\": 32, \"degrade\": 8}'::jsonb"));
    }

    [Fact]
    public async Task AWithdrawnComponentLeavesTheListKeepsItsHistoryAndNoLongerChanges()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (_, communes) = await BatimentAsync("Église", technicien);
        var baes = await CreerAsync($"{Base}/components", new { nom = "BAES", corpsEtat = "EclairageSecurite", zoneId = communes }, technicien);
        var version = await VersionAsync(baes, technicien);

        using var sansMotif = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{baes}/withdraw", technicien, new { version });
        using var retire = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{baes}/withdraw", technicien, new { motif = "Remplacé par un bloc LED", version });
        var fiche = await FicheAsync(baes, technicien);
        using var modifie = await EnvoyerAsync(HttpMethod.Put, $"{Base}/components/{baes}", technicien, new
        {
            nom = "BAES",
            corpsEtat = "EclairageSecurite",
            zoneId = communes,
            soumisAVerification = true,
            version = fiche.GetProperty("composant").GetProperty("version").GetUInt32(),
        });

        Assert.Equal("motif", await ChampAsync(sansMotif));
        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        Assert.Empty(await ListerAsync("", technicien));
        Assert.Equal(["BAES"], Noms(await ListerAsync("?retires=true", technicien)));
        Assert.Equal("Remplacé par un bloc LED", fiche.GetProperty("composant").GetProperty("motifRetrait").GetString());
        Assert.Equal(["Retrait", "Creation"], fiche.GetProperty("historique").EnumerateArray().Select(e => e.GetProperty("nature").GetString()));
        Assert.Equal("composant_retire", await CodeAsync(modifie, HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("""{"nom":"Chaises","corpsEtat":"Mobilier","lot":{"unite":"Unite","quantite":6,"repartition":{"bon":5,"critique":2}}}""", "lot.repartition")]
    [InlineData("""{"nom":"Chaises","corpsEtat":"Mobilier","lot":{"unite":"Unite","quantite":0}}""", "lot.quantite")]
    [InlineData("""{"nom":"Chaises","corpsEtat":"Mobilier","lot":{"unite":"Litre","quantite":6}}""", "lot.unite")]
    [InlineData("""{"nom":"Chaises","corpsEtat":"17"}""", "corpsEtat")]
    [InlineData("""{"nom":"Chaudière","corpsEtat":"ChauffageVentilation","anneeMiseEnService":2999}""", "anneeMiseEnService")]
    [InlineData("""{"nom":"Chaudière","corpsEtat":"ChauffageVentilation","moisMiseEnService":4}""", "moisMiseEnService")]
    [InlineData("""{"nom":" ","corpsEtat":"Mobilier"}""", "nom")]
    public async Task InvalidComponentsAreRefusedField(string corps, string champ)
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (_, communes) = await BatimentAsync("Église", technicien);
        var demande = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(corps)!;
        demande["zoneId"] = JsonSerializer.SerializeToElement(communes);

        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components", technicien, demande);

        Assert.Equal(champ, await ChampAsync(reponse));
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM patrimoine.composants"));
    }

    [Fact]
    public async Task TheZoneMustBeOneOfTheSiteAndOnlyFieldRolesWrite()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (_, communes) = await BatimentAsync("Église", technicien);

        using var zoneInconnue = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components", technicien, new { nom = "Banc", corpsEtat = "Mobilier", zoneId = Guid.NewGuid() });
        using var sansZone = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components", technicien, new { nom = "Banc", corpsEtat = "Mobilier" });
        using var pasteur = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components", await JetonAsync("pasteur@inventaire.test"), new { nom = "Banc", corpsEtat = "17" }); // droits avant saisie
        using var introuvable = await EnvoyerAsync(HttpMethod.Get, $"{Base}/components/{Guid.NewGuid()}", technicien);
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/components", technicien);

        Assert.Equal("zoneId", await ChampAsync(zoneInconnue));
        Assert.Equal("zoneId", await ChampAsync(sansZone));
        Assert.Equal(HttpStatusCode.Forbidden, pasteur.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, introuvable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
    }

    private static object Chaises(Guid zone, uint? version, int bon, int degrade = 0) => new
    {
        nom = "Chaises",
        corpsEtat = "Mobilier",
        zoneId = zone,
        soumisAVerification = false,
        version,
        lot = new { unite = "Unite", quantite = 40, repartition = new { bon, degrade } },
    };

    private static IEnumerable<string?> Noms(IEnumerable<JsonElement> composants) => composants.Select(c => c.GetProperty("nom").GetString());

    // Bâtiment et sa zone « Parties communes ».
    private async Task<(Guid Batiment, Guid PartiesCommunes)> BatimentAsync(string nom, string jeton)
    {
        var batiment = await CreerAsync($"{Base}/buildings", new { nom }, jeton);
        using var structure = await EnvoyerAsync(HttpMethod.Get, $"{Base}/structure", jeton);
        var zones = (await structure.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Single(b => b.GetProperty("id").GetGuid() == batiment).GetProperty("zones");
        return (batiment, zones.EnumerateArray().Single(z => z.GetProperty("partiesCommunes").GetBoolean()).GetProperty("id").GetGuid());
    }

    private async Task<JsonElement[]> ListerAsync(string filtre, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Base}/components{filtre}", jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var liste = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(liste.GetProperty("tronquee").GetBoolean());
        return [.. liste.GetProperty("composants").EnumerateArray()];
    }

    private async Task<JsonElement> FicheAsync(Guid id, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Base}/components/{id}", jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<uint> VersionAsync(Guid id, string jeton) =>
        (await FicheAsync(id, jeton)).GetProperty("composant").GetProperty("version").GetUInt32();

    private static async Task<string?> CodeAsync(HttpResponseMessage reponse, HttpStatusCode attendu)
    {
        Assert.Equal(attendu, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    }
}

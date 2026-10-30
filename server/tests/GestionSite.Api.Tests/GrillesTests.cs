using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>Grilles d'évaluation (US-11, S11-01) : grilles par défaut, versions adaptées par R/D.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class GrillesTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Grilles => $"{Base}/grids";

    [Fact]
    public async Task EveryTradeHasADefaultGridReadableByInternalAccounts()
    {
        var pasteur = await JetonAsync("pasteur@inventaire.test");

        var grilles = await LireAsync(Grilles, pasteur);
        var electricite = grilles.EnumerateArray().Single(g => g.GetProperty("corpsEtat").GetString() == "CourantsForts");

        Assert.Equal(20, grilles.GetArrayLength());
        Assert.All(grilles.EnumerateArray(), g => Assert.Equal(1, g.GetProperty("version").GetInt32()));
        Assert.False(electricite.GetProperty("adaptee").GetBoolean());
        Assert.Equal(JsonValueKind.Null, electricite.GetProperty("adapteeLe").ValueKind);
        Assert.Contains("Bouton test du différentiel 30 mA : déclenche", electricite.GetProperty("criteres").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("°C", electricite.GetProperty("mesures")[0].GetProperty("unite").GetString());
    }

    [Fact]
    public async Task ManagersAdaptAGridIntoANewVersionAtTheReadVersion()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var mobilier = new { criteres = new[] { "Assise intacte", "Pieds stables", "Housse propre" }, mesures = new[] { new { libelle = "Hauteur d'assise", unite = "cm" } }, version = 1 };

        using var parTechnicien = await EnvoyerAsync(HttpMethod.Put, $"{Grilles}/Mobilier", technicien, mobilier);
        using var adaptee = await EnvoyerAsync(HttpMethod.Put, $"{Grilles}/Mobilier", responsable, mobilier);
        using var perimee = await EnvoyerAsync(HttpMethod.Put, $"{Grilles}/Mobilier", responsable, mobilier);
        using var identique = await EnvoyerAsync(HttpMethod.Put, $"{Grilles}/Mobilier", responsable, mobilier with { version = 2 });
        var lue = await LireAsync($"{Grilles}/Mobilier", technicien);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(2, (await adaptee.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        Assert.Equal(2, (await identique.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32()); // rien de neuf
        Assert.True(lue.GetProperty("adaptee").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, lue.GetProperty("adapteeLe").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, lue.GetProperty("auteurCompteId").ValueKind);
        var parDefaut = await LireAsync($"{Grilles}/Mobilier/default", technicien);
        Assert.Contains("Pieds stables, patins présents", parDefaut.GetProperty("criteres").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(["Assise intacte", "Pieds stables", "Housse propre"], lue.GetProperty("criteres").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM patrimoine.grilles"));
    }

    [Theory]
    [InlineData("""{"criteres":[],"version":1}""", "criteres")]
    [InlineData("""{"criteres":["A","a"],"version":1}""", "criteres")]
    [InlineData("""{"criteres":["Fuite"],"mesures":[{"libelle":"Pression"}],"version":1}""", "mesures")]
    [InlineData("""{"criteres":["Fuite"],"mesures":[null,{"unite":"bar"}],"version":1}""", "mesures")]
    [InlineData("""{"criteres":["Fuite"],"mesures":[{"libelle":"Pression","unite":"bar"},{"libelle":"pression","unite":"Pa"}],"version":1}""", "mesures")]
    [InlineData("""{"criteres":["Fuite"]}""", "version")]
    public async Task InvalidGridsAreRefused(string corps, string champ)
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");

        using var reponse = await EnvoyerAsync(HttpMethod.Put, $"{Grilles}/Plomberie", responsable, JsonSerializer.Deserialize<JsonElement>(corps));
        using var inconnu = await EnvoyerAsync(HttpMethod.Get, $"{Grilles}/Charpenterie", responsable);

        Assert.Equal(champ, await ChampAsync(reponse));
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
    }

    [Fact]
    public async Task AGridAdaptedOnAnotherSiteOrOrganisationIsNotUsedHere()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        foreach (var (organisation, site) in new[] { (Organisation, Guid.NewGuid()), (Guid.NewGuid(), Site) })
        {
            Assert.Equal(1, await Database.CountAsync(
                """
                WITH ajout AS (
                    INSERT INTO patrimoine.grilles (id, organisation_id, site_id, corps_etat, version, criteres, mesures, cree_le)
                    VALUES (gen_random_uuid(), @organisation, @site, 'Gaz', 5, '["Ailleurs"]', '[]', now()) RETURNING 1)
                SELECT count(*) FROM ajout
                """,
                ("organisation", organisation), ("site", site)));
        }

        var gaz = await LireAsync($"{Grilles}/Gaz", technicien);
        using var siteInconnu = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/grids", technicien);

        Assert.Equal(1, gaz.GetProperty("version").GetInt32());
        Assert.DoesNotContain("Ailleurs", gaz.GetProperty("criteres").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(HttpStatusCode.NotFound, siteInconnu.StatusCode);
    }

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

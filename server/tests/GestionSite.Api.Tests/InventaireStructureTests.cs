using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>Structure de l'inventaire d'un site (US-07, S07-01) sur l'API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class InventaireStructureTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    [Fact]
    public async Task ABuildingComesWithItsCommonAreasAndEveryChangeIsJournaled()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");

        var batiment = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var rdc = await CreerAsync($"{Base}/buildings/{batiment}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        var salle = await CreerAsync($"{Base}/zones", new { nom = "Grande salle", batimentId = batiment, niveauId = rdc }, technicien);
        using var renomme = await EnvoyerAsync(HttpMethod.Put, $"{Base}/zones/{salle}", technicien, new { nom = "Grande salle paroissiale" });
        using var structure = await EnvoyerAsync(HttpMethod.Get, $"{Base}/structure", await JetonAsync("pasteur@inventaire.test"));
        var lue = (await structure.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single();

        Assert.Equal(HttpStatusCode.NoContent, renomme.StatusCode);
        Assert.Equal("Église", lue.GetProperty("nom").GetString());
        Assert.Equal(["Rez-de-chaussée"], lue.GetProperty("niveaux").EnumerateArray().Select(n => n.GetProperty("nom").GetString()));
        Assert.Equal(["Grande salle paroissiale", "Parties communes"], lue.GetProperty("zones").EnumerateArray().Select(z => z.GetProperty("nom").GetString()));
        Assert.True(lue.GetProperty("zones")[1].GetProperty("partiesCommunes").GetBoolean());
        Assert.Equal(5, await Database.CountAsync("SELECT count(*) FROM patrimoine.journal_inventaire WHERE auteur_compte_id IS NOT NULL"));
        Assert.Equal(1, await Database.CountAsync(
            "SELECT count(*) FROM patrimoine.journal_inventaire WHERE nature = 'Modification' AND avant->>'nom' = 'Grande salle' AND apres->>'nom' = 'Grande salle paroissiale'"));
    }

    [Theory]
    [InlineData("pasteur@inventaire.test", HttpStatusCode.Forbidden)] // lit, ne modifie pas
    [InlineData("admin@inventaire.test", HttpStatusCode.Forbidden)] // administrateur seul : aucun droit métier
    public async Task OnlyFieldRolesModifyTheInventory(string compte, HttpStatusCode attendu)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Base}/buildings", await JetonAsync(compte), new { nom = "Presbytère" });

        Assert.Equal(attendu, reponse.StatusCode);
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM patrimoine.batiments"));
    }

    [Fact]
    public async Task AnotherSiteOrOrganisationIsNotFound()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");

        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/structure", technicien);
        using var autreOrganisation = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Guid.NewGuid()}/sites/{Site}/inventory/structure", technicien);

        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreOrganisation.StatusCode);
    }

    [Fact]
    public async Task NamesAreUniqueWhereTheyLiveAndCommonAreasKeepTheirName()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var salles = await CreerAsync($"{Base}/buildings", new { nom = "Salles annexes" }, technicien);
        var etage = await CreerAsync($"{Base}/buildings/{salles}/levels", new { nom = "1er étage", ordre = 1 }, technicien);
        var structure = await LireAsync(technicien);
        var communes = structure.EnumerateArray().First(b => b.GetProperty("nom").GetString() == "Église").GetProperty("zones")[0].GetProperty("id").GetGuid();

        using var doublon = await EnvoyerAsync(HttpMethod.Post, $"{Base}/buildings", technicien, new { nom = "Église" });
        using var niveauAilleurs = await EnvoyerAsync(HttpMethod.Post, $"{Base}/zones", technicien, new { nom = "Sacristie", batimentId = eglise, niveauId = etage });
        using var renommerCommunes = await EnvoyerAsync(HttpMethod.Put, $"{Base}/zones/{communes}", technicien, new { nom = "Hall" });
        using var nomVide = await EnvoyerAsync(HttpMethod.Post, $"{Base}/buildings", technicien, new { nom = " " });
        using var inconnu = await EnvoyerAsync(HttpMethod.Put, $"{Base}/buildings/{Guid.NewGuid()}", technicien, new { nom = "X" });

        Assert.Equal(HttpStatusCode.Conflict, doublon.StatusCode);
        Assert.Equal("niveauId", await ChampAsync(niveauAilleurs));
        Assert.Equal("nom", await ChampAsync(renommerCommunes));
        Assert.Equal("nom", await ChampAsync(nomVide));
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
    }

    [Fact]
    public async Task TheSameRoomNameMayExistOnEachLevelButNotTwiceOnOne()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var rdc = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        var etage = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "1er étage", ordre = 1 }, technicien);

        await CreerAsync($"{Base}/zones", new { nom = "Sanitaires", batimentId = eglise, niveauId = rdc }, technicien);
        await CreerAsync($"{Base}/zones", new { nom = "Sanitaires", batimentId = eglise, niveauId = etage }, technicien);
        using var memeNiveau = await EnvoyerAsync(HttpMethod.Post, $"{Base}/zones", technicien, new { nom = "SANITAIRES", batimentId = eglise, niveauId = rdc });
        using var casse = await EnvoyerAsync(HttpMethod.Post, $"{Base}/buildings", technicien, new { nom = "église" });

        Assert.Equal(HttpStatusCode.Conflict, memeNiveau.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, casse.StatusCode);
    }

    [Fact]
    public async Task MissingRequiredFieldsAreInputErrors()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);

        using var sansOrdre = await EnvoyerAsync(HttpMethod.Post, $"{Base}/buildings/{eglise}/levels", technicien, new { nom = "Sous-sol" });
        using var sansBatiment = await EnvoyerAsync(HttpMethod.Post, $"{Base}/zones", technicien, new { nom = "Sacristie" });

        Assert.Equal("ordre", await ChampAsync(sansOrdre));
        Assert.Equal("batimentId", await ChampAsync(sansBatiment));
    }

    private async Task<JsonElement> LireAsync(string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Base}/structure", jeton);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

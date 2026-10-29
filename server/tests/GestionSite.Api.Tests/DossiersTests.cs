using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>Dossiers d'un site (US-09, S09-01) : déclaration interne, qualification, doublons, classement sans suite.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class DossiersTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Dossiers => Base.Replace("/inventory", "/cases", StringComparison.Ordinal);

    [Fact]
    public async Task AnInternalDeclarationOpensACaseToQualifyAndAReplayKeepsIt()
    {
        var (_, communes) = await LieuAsync();
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var commande = Guid.NewGuid();

        var dossier = await DeclarerAsync(pasteur, communes, "Fuite au plafond de la sacristie\nFlaque au sol.", commande);
        var rejoue = await DeclarerAsync(pasteur, communes, "Fuite au plafond de la sacristie\nFlaque au sol.", commande);
        var lu = await LireAsync(dossier, await JetonAsync("technicien@inventaire.test"));
        var liste = await ListerAsync(pasteur);

        Assert.Equal(dossier, rejoue);
        Assert.Equal("AQualifier", lu.GetProperty("statut").GetString());
        Assert.Equal("Interne", lu.GetProperty("origine").GetString());
        Assert.Equal("Fuite au plafond de la sacristie", lu.GetProperty("titre").GetString());
        Assert.Equal("Église · Parties communes", lu.GetProperty("emplacement").GetString()); // sans précision : le libellé de la zone
        Assert.Equal(JsonValueKind.Null, lu.GetProperty("contactDeclarant").ValueKind);
        Assert.Equal(["Declaration"], lu.GetProperty("historique").EnumerateArray().Select(e => e.GetProperty("nature").GetString()));
        Assert.Equal([dossier], liste.Select(d => d.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task OnlyManagersQualifyWithAZoneOfTheSiteAndAtTheReadVersion()
    {
        var (composant, communes) = await LieuAsync();
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var dossier = await DeclarerAsync(technicien, communes, "Bloc de secours éteint", Guid.NewGuid());
        var version = (await LireAsync(dossier, responsable)).GetProperty("version").GetUInt32();

        using var parTechnicien = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", technicien, new { priorite = "Haute", zoneId = communes, version });
        using var prioriteNumerique = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", responsable, new { priorite = "1", zoneId = communes, version });
        using var autreZone = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", responsable, new { priorite = "Haute", zoneId = Guid.NewGuid(), version });
        using var qualifie = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", responsable,
            new { priorite = "Urgente", zoneId = communes, composantId = composant, version });
        using var perime = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", responsable, new { priorite = "Basse", zoneId = communes, version });
        var lu = await LireAsync(dossier, responsable);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal("priorite", await ChampAsync(prioriteNumerique));
        Assert.Equal("zoneId", await ChampAsync(autreZone));
        Assert.Equal(HttpStatusCode.NoContent, qualifie.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, perime.StatusCode);
        Assert.Equal("Urgente", lu.GetProperty("priorite").GetString());
        Assert.Equal("BAES", lu.GetProperty("lieu").GetProperty("composant").GetString());
        Assert.Equal("EclairageSecurite", lu.GetProperty("lieu").GetProperty("corpsEtat").GetString());
        Assert.Equal("Qualification", lu.GetProperty("historique")[0].GetProperty("nature").GetString());
        Assert.Equal("AQualifier", lu.GetProperty("statut").GetString()); // l'affectation (S09-02) fait passer à « Affecté »
    }

    [Fact]
    public async Task DuplicatesAreSuggestedThenAttachedToTheMainCase()
    {
        var (_, communes) = await LieuAsync();
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var principal = await DeclarerAsync(pasteur, communes, "Infiltration plafond", Guid.NewGuid());
        var doublon = await DeclarerAsync(pasteur, communes, "Eau qui coule du plafond", Guid.NewGuid());
        var lu = await LireAsync(doublon, responsable);
        var version = lu.GetProperty("version").GetUInt32();

        using var lui = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{doublon}/duplicate-of", responsable, new { principalId = doublon, version });
        using var rattache = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{doublon}/duplicate-of", responsable, new { principalId = principal, version });
        var apres = await LireAsync(doublon, responsable);
        using var encore = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{doublon}/duplicate-of", responsable,
            new { principalId = principal, version = apres.GetProperty("version").GetUInt32() });
        var principalLu = await LireAsync(principal, responsable);

        Assert.Equal([principal], lu.GetProperty("suggestions").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal("principalId", await ChampAsync(lui));
        Assert.Equal(HttpStatusCode.NoContent, rattache.StatusCode);
        Assert.Equal("RattacheDoublon", apres.GetProperty("statut").GetString());
        Assert.Equal("statut_incompatible", (await encore.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal([doublon], principalLu.GetProperty("doublons").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal([principal], (await ListerAsync(pasteur)).Select(d => d.GetProperty("id").GetGuid())); // le doublon quitte les dossiers ouverts
    }

    [Fact]
    public async Task DismissingNeedsAReasonAndACommentForOther()
    {
        var (_, communes) = await LieuAsync();
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var dossier = await DeclarerAsync(responsable, communes, "Bruit étrange", Guid.NewGuid());
        var version = (await LireAsync(dossier, responsable)).GetProperty("version").GetUInt32();

        using var sansCommentaire = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dismiss", responsable, new { motif = "Autre", version });
        using var inconnu = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dismiss", responsable, new { motif = "Flemme", version });
        using var classe = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dismiss", responsable, new { motif = "AucunProbleme", version });
        var lu = await LireAsync(dossier, responsable);

        Assert.Equal("commentaire", await ChampAsync(sansCommentaire));
        Assert.Equal("motif", await ChampAsync(inconnu));
        Assert.Equal(HttpStatusCode.NoContent, classe.StatusCode);
        Assert.Equal("ClasseSansSuite", lu.GetProperty("statut").GetString());
        Assert.Equal("AucunProbleme", lu.GetProperty("motifClassement").GetString());
        Assert.Empty(await ListerAsync(responsable));
        Assert.Equal([dossier], (await ListerAsync(responsable, clos: true)).Select(d => d.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task ADeclarationNeedsAZoneOfTheSiteAndCasesStayInTheirSite()
    {
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        await LieuAsync();

        using var sansZone = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}", pasteur, new { commandeId = Guid.NewGuid(), description = "Panne" });
        using var zoneInconnue = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}", pasteur, new { commandeId = Guid.NewGuid(), zoneId = Guid.NewGuid(), description = "Panne" });
        using var introuvable = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}/{Guid.NewGuid()}", pasteur);
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/cases", pasteur);

        Assert.Equal("zoneId", await ChampAsync(sansZone));
        Assert.Equal("zoneId", await ChampAsync(zoneInconnue));
        Assert.Equal(HttpStatusCode.NotFound, introuvable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
    }

    // Église, sa zone « Parties communes » et un composant BAES (éclairage de sécurité) dans cette zone.
    private async Task<(Guid Composant, Guid PartiesCommunes)> LieuAsync()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var batiment = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        using var structure = await EnvoyerAsync(HttpMethod.Get, $"{Base}/structure", technicien);
        var communes = (await structure.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Single(b => b.GetProperty("id").GetGuid() == batiment).GetProperty("zones")[0].GetProperty("id").GetGuid();
        var composant = await CreerAsync($"{Base}/components", new { nom = "BAES", corpsEtat = "EclairageSecurite", zoneId = communes }, technicien);
        return (composant, communes);
    }

    private async Task<Guid> DeclarerAsync(string jeton, Guid zone, string description, Guid commande) =>
        await CreerAsync(Dossiers, new { commandeId = commande, zoneId = zone, description, nature = "Incident" }, jeton);

    private async Task<JsonElement> LireAsync(Guid id, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}/{id}", jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement[]> ListerAsync(string jeton, bool clos = false)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}{(clos ? "?clos=true" : "")}", jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return [.. (await reponse.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }
}

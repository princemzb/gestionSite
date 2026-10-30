using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>Classement ERP des bâtiments et plans proposés (US-12, S12-01) sur l'API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class EtablissementTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Preventif => Base.Replace("/inventory", "/preventive", StringComparison.Ordinal);

    [Fact]
    public async Task ManagersDeclareTheClassificationAtTheReadVersionAndEveryChangeKeepsThePreviousValue()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var version = await VersionAsync(eglise, pasteur);

        using var parTechnicien = await ClasserAsync(eglise, technicien, Classement(5, version));
        using var parPasteur = await ClasserAsync(eglise, pasteur, Classement(5, version));
        using var sansCategorie = await ClasserAsync(eglise, responsable, new { erp = true, monumentHistorique = false, permisAvant1997 = "Non", version });
        using var typeInconnu = await ClasserAsync(eglise, responsable, new { erp = true, type = "Z", categorie = 5, monumentHistorique = false, permisAvant1997 = "Non", version });
        using var premier = await ClasserAsync(eglise, responsable, Classement(5, version));
        using var perime = await ClasserAsync(eglise, responsable, Classement(3, version));
        using var second = await ClasserAsync(eglise, responsable, Classement(3, await VersionAsync(eglise, pasteur)));
        using var inchange = await ClasserAsync(eglise, responsable, Classement(3, await VersionAsync(eglise, pasteur)));
        using var inchangeDepasse = await ClasserAsync(eglise, responsable, Classement(3, version)); // sans changement mais version dépassée
        var lu = (await LireAsync($"{Base}/establishment", pasteur)).GetProperty("batiments").EnumerateArray().Single();

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parPasteur.StatusCode);
        Assert.Equal("categorie", await ChampAsync(sansCategorie));
        Assert.Equal("type", await ChampAsync(typeInconnu));
        Assert.Equal(HttpStatusCode.NoContent, premier.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, perime.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, inchange.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, inchangeDepasse.StatusCode);
        var classement = lu.GetProperty("classement");
        Assert.Equal(("V", 3, "Inconnu"), (classement.GetProperty("type").GetString(), classement.GetProperty("categorie").GetInt32(),
            classement.GetProperty("permisAvant1997").GetString()));
        Assert.Equal(["L"], classement.GetProperty("typesSecondaires").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(2, await Database.CountAsync(
            "SELECT count(*) FROM patrimoine.journal_inventaire WHERE cible = 'Batiment' AND cible_id = @id AND nature = 'Modification'", ("id", eglise)));
        Assert.Equal(1, await Database.CountAsync(
            "SELECT count(*) FROM patrimoine.journal_inventaire WHERE cible_id = @id AND nature = 'Modification' " +
            "AND avant ->> 'categorie' = '5' AND apres ->> 'categorie' = '3' AND apres -> 'type' IS NULL", ("id", eglise)));
    }

    [Fact]
    public async Task EmployeesAreDeclaredOnceForTheSiteAndOtherOrganisationsSeeNothing()
    {
        await MembreAsync("delegue@inventaire.test", Role.Delegue);
        var delegue = await JetonAsync("delegue@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var vide = await LireAsync($"{Base}/establishment", technicien);

        using var sansReponse = await EnvoyerAsync(HttpMethod.Put, $"{Base}/establishment", delegue, new { version = 0 });
        using var parTechnicien = await EnvoyerAsync(HttpMethod.Put, $"{Base}/establishment", technicien, new { salariesPresents = true, version = 0 });
        using var declare = await EnvoyerAsync(HttpMethod.Put, $"{Base}/establishment", delegue, new { salariesPresents = true, version = 0 });
        using var deuxFois = await EnvoyerAsync(HttpMethod.Put, $"{Base}/establishment", delegue, new { salariesPresents = false, version = 0 });
        var lu = await LireAsync($"{Base}/establishment", technicien);
        using var retire = await EnvoyerAsync(HttpMethod.Put, $"{Base}/establishment", delegue,
            new { salariesPresents = false, version = lu.GetProperty("version").GetUInt32() });
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/establishment", delegue);
        using var autreOrganisation = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Guid.NewGuid()}/sites/{Site}/preventive/proposals", delegue);
        using var batimentInconnu = await ClasserAsync(Guid.NewGuid(), delegue, Classement(5, 1));

        Assert.Equal((JsonValueKind.Null, 0u), (vide.GetProperty("salariesPresents").ValueKind, vide.GetProperty("version").GetUInt32()));
        Assert.Equal("salariesPresents", await ChampAsync(sansReponse));
        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, declare.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deuxFois.StatusCode);
        Assert.True(lu.GetProperty("salariesPresents").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreOrganisation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, batimentInconnu.StatusCode);
        Assert.Equal(1, await Database.CountAsync(
            "SELECT count(*) FROM patrimoine.journal_inventaire WHERE cible = 'Site' AND avant ->> 'salariesPresents' = 'true' AND apres ->> 'salariesPresents' = 'false'"));
    }

    [Fact]
    public async Task ProposalsFollowTheDeclaredCategory()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var communes = (await LireAsync($"{Base}/structure", technicien)).EnumerateArray().Single().GetProperty("zones")[0].GetProperty("id").GetGuid();
        var tableau = await CreerAsync($"{Base}/components", new { nom = "Tableau général", corpsEtat = "CourantsForts", zoneId = communes }, technicien);
        await CreerAsync($"{Base}/components", new { nom = "Bancs", corpsEtat = "Mobilier", zoneId = communes }, technicien); // non soumis à vérification

        using var cinquieme = await ClasserAsync(eglise, responsable, Classement(5, await VersionAsync(eglise, pasteur)));
        var en5 = await PropositionsAsync(pasteur);
        using var troisieme = await ClasserAsync(eglise, responsable, Classement(3, await VersionAsync(eglise, pasteur)));
        var en3 = await PropositionsAsync(technicien);

        Assert.Equal(HttpStatusCode.NoContent, cinquieme.StatusCode);
        Assert.DoesNotContain(en5, p => p.GetProperty("intervenant").GetString() == "OrganismeAgree");
        Assert.DoesNotContain(en5, p => p.GetProperty("modele").GetString() == "commission-visite");
        Assert.All(en5, p => Assert.True(p.GetProperty("aConfirmer").GetBoolean()));
        Assert.All(en5, p => Assert.Equal("Proposee", p.GetProperty("statut").GetString()));
        Assert.DoesNotContain(en5, p => p.GetProperty("cible").GetProperty("libelle").GetString()!.StartsWith("Bancs", StringComparison.Ordinal));
        var electricite = en3.Single(p => p.GetProperty("modele").GetString() == "electricite-verification");
        Assert.Equal("OrganismeAgree", electricite.GetProperty("intervenant").GetString());
        Assert.Equal(tableau, electricite.GetProperty("cible").GetProperty("composantId").GetGuid());
        Assert.Equal("Tableau général (Église · Parties communes)", electricite.GetProperty("cible").GetProperty("libelle").GetString());
        Assert.Equal(("Reglementaire", "VerificationExterieure", "Glissant"), (electricite.GetProperty("base").GetString(),
            electricite.GetProperty("nature").GetString(), electricite.GetProperty("mode").GetString()));
        Assert.Equal((1, "Ans"), (electricite.GetProperty("periode").GetProperty("valeur").GetInt32(), electricite.GetProperty("periode").GetProperty("unite").GetString()));
        var commission = en3.Single(p => p.GetProperty("modele").GetString() == "commission-visite");
        Assert.Equal(("Batiment", eglise), (commission.GetProperty("cible").GetProperty("type").GetString(), commission.GetProperty("cible").GetProperty("batimentId").GetGuid()));
        Assert.Equal(communes, commission.GetProperty("cible").GetProperty("zoneId").GetGuid());
    }

    private static object Classement(int categorie, uint version) =>
        new { erp = true, categorie, typesSecondaires = new[] { "L" }, locauxSommeil = false, monumentHistorique = false, permisAvant1997 = "Inconnu", version };

    private Task<HttpResponseMessage> ClasserAsync(Guid batiment, string jeton, object demande) =>
        EnvoyerAsync(HttpMethod.Put, $"{Base}/buildings/{batiment}/classification", jeton, demande);

    private async Task<uint> VersionAsync(Guid batiment, string jeton) =>
        (await LireAsync($"{Base}/establishment", jeton)).GetProperty("batiments").EnumerateArray()
            .Single(b => b.GetProperty("id").GetGuid() == batiment).GetProperty("version").GetUInt32();

    private async Task<JsonElement[]> PropositionsAsync(string jeton) => [.. (await LireAsync($"{Preventif}/proposals", jeton)).EnumerateArray()];

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

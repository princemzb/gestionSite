using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>Affectation et démarrage des interventions (US-09, S09-02), délais DEC-05.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class InterventionsTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Dossiers => Base.Replace("/inventory", "/cases", StringComparison.Ordinal);

    [Fact]
    public async Task AQualifiedCaseIsAssignedToAFieldMemberOfTheSite()
    {
        var (dossier, communes, responsable) = await DossierAsync();
        var technicien = await IdentifiantAsync("technicien@inventaire.test");
        var pasteur = await IdentifiantAsync("pasteur@inventaire.test");

        using var nonQualifie = await AffecterAsync(dossier, responsable, new { titre = "Remplacer le bloc", intervenantCompteId = technicien });
        await QualifierAsync(dossier, communes, responsable, "Haute");
        using var parTechnicien = await AffecterAsync(dossier, await JetonAsync("technicien@inventaire.test"), new { titre = "Remplacer", intervenantCompteId = technicien });
        using var aUnPasteur = await AffecterAsync(dossier, responsable, new { titre = "Remplacer", intervenantCompteId = pasteur });
        using var prestataireSeul = await AffecterAsync(dossier, responsable, new { titre = "Couverture", prestataire = "Couverture Martin" });
        using var premiere = await AffecterAsync(dossier, responsable, new { titre = "Mise en sécurité", intervenantCompteId = technicien });
        using var seconde = await AffecterAsync(dossier, responsable, new { titre = "Remplacement des tuiles", prestataire = "Couverture Martin", referentCompteId = technicien });
        var lu = await LireAsync(dossier, responsable);

        Assert.Equal("non_qualifie", await CodeAsync(nonQualifie));
        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal("intervenantCompteId", await ChampAsync(aUnPasteur));
        Assert.Equal("referentCompteId", await ChampAsync(prestataireSeul));
        Assert.Equal(1, (await premiere.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("numero").GetInt32());
        Assert.Equal(2, (await seconde.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("numero").GetInt32());
        Assert.Equal("Affecte", lu.GetProperty("statut").GetString());
        Assert.Equal(["Mise en sécurité", "Remplacement des tuiles"], lu.GetProperty("interventions").EnumerateArray().Select(i => i.GetProperty("titre").GetString()));
        Assert.Equal("Couverture Martin", lu.GetProperty("interventions")[1].GetProperty("prestataire").GetString());
        var affecte = lu.GetProperty("affecteLe").GetDateTimeOffset();
        Assert.Equal(affecte.AddDays(1), lu.GetProperty("echeancePriseEnCharge").GetDateTimeOffset()); // Haute : 1 jour (DEC-05)
        Assert.Equal(lu.GetProperty("qualifieLe").GetDateTimeOffset().AddDays(7), lu.GetProperty("echeanceResolution").GetDateTimeOffset());
    }

    [Fact]
    public async Task ThePersonInChargeOrAManagerStartsTheIntervention()
    {
        var (dossier, communes, responsable) = await DossierAsync();
        await MembreAsync("autre-technicien@inventaire.test", Role.Technicien);
        await QualifierAsync(dossier, communes, responsable, "Urgente");
        var technicien = await JetonAsync("technicien@inventaire.test");
        using (await AffecterAsync(dossier, responsable, new { titre = "Bloc de secours", intervenantCompteId = await IdentifiantAsync("technicien@inventaire.test") }))
        {
        }

        using var parUnAutre = await DemarrerAsync(dossier, 1, await JetonAsync("autre-technicien@inventaire.test"), responsable);
        using var demarre = await DemarrerAsync(dossier, 1, technicien, responsable);
        using var encore = await DemarrerAsync(dossier, 1, technicien, responsable);
        using var inconnue = await DemarrerAsync(dossier, 9, technicien, responsable);
        var lu = await LireAsync(dossier, responsable);
        var miennes = await MesInterventionsAsync(technicien);

        Assert.Equal(HttpStatusCode.Forbidden, parUnAutre.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, demarre.StatusCode);
        Assert.Equal("statut_incompatible", await CodeAsync(encore));
        Assert.Equal(HttpStatusCode.NotFound, inconnue.StatusCode);
        Assert.Equal("EnCours", lu.GetProperty("statut").GetString());
        Assert.Equal(JsonValueKind.Null, lu.GetProperty("echeancePriseEnCharge").ValueKind); // prise en charge faite
        Assert.Equal(["Affectation", "Demarrage"], lu.GetProperty("historique").EnumerateArray().Select(e => e.GetProperty("nature").GetString())
            .Where(n => n is "Affectation" or "Demarrage").Reverse());
        Assert.Equal(["Bloc de secours"], miennes.Select(m => m.GetProperty("titre").GetString()));
        Assert.Empty(await MesInterventionsAsync(await JetonAsync("autre-technicien@inventaire.test")));
    }

    [Fact]
    public async Task AManagerIsOnlyAContractorsReferentAndAStaleVersionIsRefusedEvenWithoutChangingTheCase()
    {
        var (dossier, communes, responsable) = await DossierAsync();
        await QualifierAsync(dossier, communes, responsable, "Normale");
        var idResponsable = await IdentifiantAsync("responsable@inventaire.test");
        var technicien = await IdentifiantAsync("technicien@inventaire.test");
        var lu = await LireAsync(dossier, responsable);

        using var responsableIntervenant = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", responsable,
            new { titre = "Réparer", intervenantCompteId = idResponsable, version = lu.GetProperty("version").GetUInt32() });
        using var responsableReferent = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", responsable,
            new { titre = "Couverture", prestataire = "Couverture Martin", referentCompteId = idResponsable, version = lu.GetProperty("version").GetUInt32() });
        using var perimee = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", responsable,
            new { titre = "Seconde", intervenantCompteId = technicien, version = lu.GetProperty("version").GetUInt32() }); // version d'avant la 1re affectation
        var apres = await LireAsync(dossier, responsable);

        Assert.Equal("intervenantCompteId", await ChampAsync(responsableIntervenant));
        Assert.Equal(HttpStatusCode.OK, responsableReferent.StatusCode);
        Assert.Equal("modifie_entre_temps", await CodeAsync(perimee));
        Assert.Single(apres.GetProperty("interventions").EnumerateArray());
        Assert.Equal(1, await Database.CountAsync(
            "SELECT count(*) FROM maintenance.journal_dossiers WHERE nature = 'Affectation' AND detail ? 'referentCompteId' AND NOT detail ? 'referent'"));
    }

    [Fact]
    public async Task OnlyManagersListAssigneesAndPastorsAreNotAmongThem()
    {
        var (_, _, responsable) = await DossierAsync();

        using var parPasteur = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}/assignees", await JetonAsync("pasteur@inventaire.test"));
        using var liste = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}/assignees", responsable);
        var roles = (await liste.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .SelectMany(m => m.GetProperty("roles").EnumerateArray().Select(r => r.GetString())).ToHashSet();

        Assert.Equal(HttpStatusCode.Forbidden, parPasteur.StatusCode);
        Assert.Contains("Technicien", roles);
        Assert.Contains("Responsable", roles);
        Assert.DoesNotContain("Pasteur", roles);
    }

    // Un dossier déclaré dans les parties communes de l'église, et le jeton d'un responsable du site.
    private async Task<(Guid Dossier, Guid PartiesCommunes, string Responsable)> DossierAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var batiment = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, responsable);
        using var structure = await EnvoyerAsync(HttpMethod.Get, $"{Base}/structure", responsable);
        var communes = (await structure.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Single(b => b.GetProperty("id").GetGuid() == batiment).GetProperty("zones")[0].GetProperty("id").GetGuid();
        var dossier = await CreerAsync(Dossiers, new { commandeId = Guid.NewGuid(), zoneId = communes, description = "Bloc de secours éteint" }, responsable);
        return (dossier, communes, responsable);
    }

    private async Task QualifierAsync(Guid dossier, Guid zone, string jeton, string priorite)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", jeton, new { priorite, zoneId = zone, version = await VersionAsync(dossier, jeton) });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<HttpResponseMessage> AffecterAsync(Guid dossier, string jeton, object affectation)
    {
        var corps = JsonSerializer.SerializeToNode(affectation)!.AsObject();
        corps["version"] = await VersionAsync(dossier, jeton);
        return await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", jeton, corps);
    }

    private async Task<HttpResponseMessage> DemarrerAsync(Guid dossier, int numero, string jeton, string lecteur) =>
        await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/{numero}/start", jeton, new { version = await VersionAsync(dossier, lecteur) });

    private async Task<JsonElement[]> MesInterventionsAsync(string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}/mine", jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return [.. (await reponse.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    private async Task<uint> VersionAsync(Guid id, string jeton) => (await LireAsync(id, jeton)).GetProperty("version").GetUInt32();

    private async Task<JsonElement> LireAsync(Guid id, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}/{id}", jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> IdentifiantAsync(string email)
    {
        using var moi = await EnvoyerAsync(HttpMethod.Get, "/me", await JetonAsync(email));
        return (await moi.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("compteId").GetGuid();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage reponse) =>
        (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}

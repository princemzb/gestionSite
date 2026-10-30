using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>
/// Droit nominatif « modifier les plans du bâtiment » (US-21, S21-03, T-01.18) : accordé par l'administrateur, site par
/// site, à un membre ayant un rôle sur le site ; union avec les rôles ; retrait immédiat ; journal des habilitations.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class DroitsNominatifsTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private static readonly string[] Criteres = ["Odeur"];

    private string Membres => $"/organizations/{Organisation}/members";

    [Fact]
    public async Task ASecondTechnicianWithTheRightSavesAPlanAndLosesItAtOnceWhenRevoked()
    {
        await MembreAsync("technicien2@inventaire.test", Role.Technicien);
        var admin = await JetonAsync("admin@inventaire.test");
        var t1 = await JetonAsync("technicien@inventaire.test");
        var t2 = await JetonAsync("technicien2@inventaire.test");
        var (eglise, rdc, nef) = await LieuxAsync(t1);
        var membreT2 = await MembreIdAsync(admin, "technicien2@inventaire.test");

        using var avant = await EnvoyerAsync(HttpMethod.Post, Plans, t2, new { batimentId = eglise, niveauId = rdc });
        using var accord = await EnvoyerAsync(HttpMethod.Put, Droit(membreT2, Site), admin, new { motif = "Relevé du rez-de-chaussée" });
        using var renouvele = await EnvoyerAsync(HttpMethod.Put, Droit(membreT2, Site), admin, new { motif = "Relevé du rez-de-chaussée" });
        var plan = await CreerAsync(Plans, new { batimentId = eglise, niveauId = rdc }, t2);
        using var version = await EnvoyerAsync(HttpMethod.Put, $"{Plans}/{plan}", t2, Enregistrement(0, nef));
        using var parT1 = await EnvoyerAsync(HttpMethod.Put, $"{Plans}/{plan}", t1, Enregistrement(1, nef));
        var liste = await LireAsync(Membres, admin);
        using var retrait = await EnvoyerAsync(HttpMethod.Delete, Droit(membreT2, Site), admin, new { motif = "Relevé terminé" });
        using var apresRetrait = await EnvoyerAsync(HttpMethod.Put, $"{Plans}/{plan}", t2, Enregistrement(1, nef)); // même jeton d'accès

        Assert.Equal(HttpStatusCode.Forbidden, avant.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, accord.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, renouvele.StatusCode); // déjà accordé : rien de neuf
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parT1.StatusCode); // le premier technicien n'a rien reçu
        Assert.Equal(HttpStatusCode.NoContent, retrait.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, apresRetrait.StatusCode);
        var droitsT2 = liste.EnumerateArray().Single(m => m.GetProperty("membreId").GetGuid() == membreT2).GetProperty("droitsNominatifs");
        Assert.Equal("ModifierPlans", droitsT2[0].GetProperty("droit").GetString());
        Assert.Equal(Site, droitsT2[0].GetProperty("siteId").GetGuid());

        var journal = (await LireAsync($"{Membres}/journal", admin)).EnumerateArray().Where(e => e.GetProperty("membreId").GetGuid() == membreT2).ToList();
        Assert.Equal(["RetraitDroit", "AttributionDroit"], journal.Take(2).Select(e => e.GetProperty("nature").GetString()));
        Assert.Equal(["Relevé terminé", "Relevé du rez-de-chaussée"], journal.Take(2).Select(e => e.GetProperty("motif").GetString()));
        Assert.All(journal.Take(2), e => Assert.NotEqual(JsonValueKind.Null, e.GetProperty("auteurCompteId").ValueKind));
        Assert.Empty(journal[1].GetProperty("droitsAvant").EnumerateArray());
        Assert.Equal("ModifierPlans", journal[1].GetProperty("droitsApres")[0].GetProperty("droit").GetString());
        Assert.Equal("ModifierPlans", journal[0].GetProperty("droitsAvant")[0].GetProperty("droit").GetString()); // valeur précédente
        Assert.Empty(journal[0].GetProperty("droitsApres").EnumerateArray());
        Assert.Equal(2, journal.Count(e => e.GetProperty("nature").GetString() is "AttributionDroit" or "RetraitDroit"));
    }

    [Fact]
    public async Task TheRightOnOneSiteOpensNothingElsewhere()
    {
        await MembreAsync("technicien2@inventaire.test", Role.Technicien);
        var admin = await JetonAsync("admin@inventaire.test");
        var t1 = await JetonAsync("technicien@inventaire.test");
        var t2 = await JetonAsync("technicien2@inventaire.test");
        var membreT2 = await MembreIdAsync(admin, "technicien2@inventaire.test");
        var autreSite = await CreerSiteAsync(admin, "Site nord");
        var (eglise, rdc, _) = await LieuxAsync(t1);
        using var accord = await EnvoyerAsync(HttpMethod.Put, Droit(membreT2, Site), admin, new { motif = "Relevé" });

        using var surAutreSite = await EnvoyerAsync(HttpMethod.Post, $"/organizations/{Organisation}/sites/{autreSite}/inventory/building-plans", t2,
            new { batimentId = eglise, niveauId = rdc });
        using var autreOrganisation = await EnvoyerAsync(HttpMethod.Post, $"/organizations/{Guid.NewGuid()}/sites/{Site}/inventory/building-plans", t2,
            new { batimentId = eglise, niveauId = rdc });
        using var inventaire = await EnvoyerAsync(HttpMethod.Post, $"{Base}/zones", t2, new { nom = "Crypte", batimentId = eglise }); // rôle T : permis
        using var grilles = await EnvoyerAsync(HttpMethod.Put, $"{Base}/grids/Gaz", t2, new { criteres = Criteres, version = 1 }); // non accordé
        using var sansRole = await EnvoyerAsync(HttpMethod.Put, Droit(membreT2, autreSite), admin, new { motif = "Relevé" });

        Assert.Equal(HttpStatusCode.NoContent, accord.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, surAutreSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreOrganisation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inventaire.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grilles.StatusCode);
        Assert.Equal("membre_non_eligible", await CodeAsync(sansRole, HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task OnlyTheAdministratorGrantsAWhitelistedRightWithAReason()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var admin = await JetonAsync("admin@inventaire.test");
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var membrePasteur = await MembreIdAsync(admin, "pasteur@inventaire.test");
        var membreAdmin = await MembreIdAsync(admin, "admin@inventaire.test");
        var (eglise, rdc, _) = await LieuxAsync(technicien);

        using var parResponsable = await EnvoyerAsync(HttpMethod.Put, Droit(membrePasteur, Site), responsable, new { motif = "x" });
        using var parTechnicien = await EnvoyerAsync(HttpMethod.Put, Droit(membrePasteur, Site), technicien, new { motif = "x" });
        using var retraitParResponsable = await EnvoyerAsync(HttpMethod.Delete, Droit(membrePasteur, Site), responsable, new { motif = "x" });
        using var sansMotif = await EnvoyerAsync(HttpMethod.Put, Droit(membrePasteur, Site), admin, new { });
        using var horsListe = await EnvoyerAsync(HttpMethod.Put, $"{Membres}/{membrePasteur}/rights/ModifierInventaire/sites/{Site}", admin, new { motif = "x" });
        using var inconnu = await EnvoyerAsync(HttpMethod.Put, $"{Membres}/{membrePasteur}/rights/ToutFaire/sites/{Site}", admin, new { motif = "x" });
        using var siteInconnu = await EnvoyerAsync(HttpMethod.Put, Droit(membrePasteur, Guid.NewGuid()), admin, new { motif = "x" });
        using var membreInconnu = await EnvoyerAsync(HttpMethod.Put, Droit(Guid.NewGuid(), Site), admin, new { motif = "x" });
        using var administrateurSansRole = await EnvoyerAsync(HttpMethod.Put, Droit(membreAdmin, Site), admin, new { motif = "x" });
        using var retraitSansMotif = await EnvoyerAsync(HttpMethod.Delete, Droit(membrePasteur, Site), admin);
        using var accordPasteur = await EnvoyerAsync(HttpMethod.Put, Droit(membrePasteur, Site), admin, new { motif = "Bénévole du relevé" });
        using var planParPasteur = await EnvoyerAsync(HttpMethod.Post, Plans, pasteur, new { batimentId = eglise, niveauId = rdc });
        using var planParAdmin = await EnvoyerAsync(HttpMethod.Post, Plans, admin, new { batimentId = eglise, niveauId = rdc });

        Assert.Equal(HttpStatusCode.Forbidden, parResponsable.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, retraitParResponsable.StatusCode);
        Assert.Equal("donnee_invalide", await CodeAsync(sansMotif, HttpStatusCode.UnprocessableEntity));
        Assert.Equal("droit_non_nominatif", await CodeAsync(horsListe, HttpStatusCode.UnprocessableEntity));
        Assert.Equal("droit_non_nominatif", await CodeAsync(inconnu, HttpStatusCode.UnprocessableEntity));
        Assert.Equal("site_inconnu", await CodeAsync(siteInconnu, HttpStatusCode.UnprocessableEntity));
        Assert.Equal(HttpStatusCode.NotFound, membreInconnu.StatusCode);
        Assert.Equal("membre_non_eligible", await CodeAsync(administrateurSansRole, HttpStatusCode.UnprocessableEntity));
        Assert.Equal("donnee_invalide", await CodeAsync(retraitSansMotif, HttpStatusCode.UnprocessableEntity));
        Assert.Equal(HttpStatusCode.NoContent, accordPasteur.StatusCode);
        Assert.Equal(HttpStatusCode.OK, planParPasteur.StatusCode); // tout membre interne du site (décision du 30/10)
        Assert.Equal(HttpStatusCode.Forbidden, planParAdmin.StatusCode); // aucun droit métier implicite
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM identite.droits_nominatifs"));
    }

    [Fact]
    public async Task LosingTheSiteRoleOrTheMembershipRemovesTheRight()
    {
        await MembreAsync("technicien2@inventaire.test", Role.Technicien);
        var admin = await JetonAsync("admin@inventaire.test");
        var autreSite = await CreerSiteAsync(admin, "Site nord");
        var membreT2 = await MembreIdAsync(admin, "technicien2@inventaire.test");
        var membrePasteur = await MembreIdAsync(admin, "pasteur@inventaire.test");
        using var accordT2 = await EnvoyerAsync(HttpMethod.Put, Droit(membreT2, Site), admin, new { motif = "Relevé" });
        using var accordPasteur = await EnvoyerAsync(HttpMethod.Put, Droit(membrePasteur, Site), admin, new { motif = "Relevé" });
        var version = (await LireAsync(Membres, admin)).EnumerateArray().Single(m => m.GetProperty("membreId").GetGuid() == membreT2).GetProperty("version").GetInt32();

        using var revision = await EnvoyerAsync(HttpMethod.Put, $"{Membres}/{membreT2}", admin,
            new { roles = new[] { new { role = "Technicien", siteId = autreSite } }, version, motif = "Mutation au site nord" });
        using var revocation = await EnvoyerAsync(HttpMethod.Delete, $"{Membres}/{membrePasteur}", admin, new { motif = "Départ" });

        Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, revocation.StatusCode);
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM identite.droits_nominatifs"));
        var journal = (await LireAsync($"{Membres}/journal", admin)).EnumerateArray().ToList();
        var revise = journal.First(e => e.GetProperty("nature").GetString() == "Revision");
        var revoque = journal.First(e => e.GetProperty("nature").GetString() == "Revocation");
        Assert.Equal(Site, revise.GetProperty("droitsAvant")[0].GetProperty("siteId").GetGuid());
        Assert.Empty(revise.GetProperty("droitsApres").EnumerateArray());
        Assert.Equal(Site, revoque.GetProperty("droitsAvant")[0].GetProperty("siteId").GetGuid());
        Assert.Empty(revoque.GetProperty("droitsApres").EnumerateArray());
    }

    [Fact]
    public async Task TheApplicationCannotGrantARightOutsideTheWhitelistEvenDirectly()
    {
        var admin = await JetonAsync("admin@inventaire.test");
        var membrePasteur = await MembreIdAsync(admin, "pasteur@inventaire.test");

        var refus = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Database.CountAsync(
            """
            WITH d AS (
                INSERT INTO identite.droits_nominatifs (id, organisation_id, membre_id, site_id, droit, accorde_le)
                VALUES (gen_random_uuid(), @organisation, @membre, @site, 'ModifierGrilles', now()) RETURNING 1)
            SELECT count(*) FROM d
            """,
            ("organisation", Organisation), ("membre", membrePasteur), ("site", Site)));

        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, refus.SqlState);
    }

    private string Plans => $"{Base}/building-plans";

    private string Droit(Guid membre, Guid site) => $"{Membres}/{membre}/rights/ModifierPlans/sites/{site}";

    private static object Enregistrement(int version, Guid nef) => new
    {
        commandeId = Guid.NewGuid(),
        version,
        contenu = new
        {
            schema = 1,
            pieces = new[] { new { zoneId = nef, contour = new[] { new { x = 0, y = 0 }, new { x = 10, y = 0 }, new { x = 10, y = 8 } } } },
        },
    };

    private async Task<(Guid Eglise, Guid Rdc, Guid Nef)> LieuxAsync(string technicien)
    {
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var rdc = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        var nef = await CreerAsync($"{Base}/zones", new { nom = "Nef", batimentId = eglise, niveauId = rdc }, technicien);
        return (eglise, rdc, nef);
    }

    private async Task<Guid> MembreIdAsync(string admin, string email) =>
        (await LireAsync(Membres, admin)).EnumerateArray().Single(m => m.GetProperty("email").GetString() == email).GetProperty("membreId").GetGuid();

    private async Task<Guid> CreerSiteAsync(string admin, string nom)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"/organizations/{Organisation}/sites", admin, new { nom });
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage reponse, HttpStatusCode attendu)
    {
        Assert.Equal(attendu, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    }

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

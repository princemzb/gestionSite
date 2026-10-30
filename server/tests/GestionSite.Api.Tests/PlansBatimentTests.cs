using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>Plans de bâtiment (US-21, S21-02) : versions immuables, conflit à la version lue, restauration, droits et isolation.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class PlansBatimentTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Plans => $"{Base}/building-plans";

    [Fact]
    public async Task SuccessiveSavesCreateVersionsAndAStaleSaveNamesTheWinner()
    {
        var (responsable, lieux) = await PreparerAsync();
        var plan = await CreerAsync(Plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, responsable);
        var vide = await LireAsync($"{Plans}/{plan}", responsable);

        using var premiere = await EnregistrerAsync(plan, responsable, version: 0, Nef(lieux, largeur: 10));
        var deuxiemeEnvoi = Guid.NewGuid();
        using var deuxieme = await EnregistrerAsync(plan, responsable, version: 1, Nef(lieux, largeur: 12), deuxiemeEnvoi, motif: "Relevé du 30/10");
        using var perimee = await EnregistrerAsync(plan, responsable, version: 1, Nef(lieux, largeur: 11));
        using var renvoi = await EnregistrerAsync(plan, responsable, version: 1, Nef(lieux, largeur: 12), deuxiemeEnvoi, motif: "Relevé du 30/10");
        using var identique = await EnregistrerAsync(plan, responsable, version: 2, Nef(lieux, largeur: 12));

        Assert.Equal(0, vide.GetProperty("version").GetInt32());
        Assert.Empty(vide.GetProperty("contenu").GetProperty("pieces").EnumerateArray());
        Assert.Equal(1, await VersionAsync(premiere));
        Assert.Equal(2, await VersionAsync(deuxieme));
        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        var conflit = await perimee.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("version_depassee", conflit.GetProperty("code").GetString());
        Assert.Equal(2, conflit.GetProperty("versionCourante").GetInt32());
        Assert.Equal("Rose Responsable", conflit.GetProperty("auteur").GetString());
        Assert.NotEqual(JsonValueKind.Null, conflit.GetProperty("modifieLe").ValueKind);
        Assert.Equal(2, await VersionAsync(renvoi)); // même envoi : même version, rien de neuf
        Assert.Equal(3, await VersionAsync(identique)); // chaque enregistrement crée une version, même sans changement
        Assert.Equal(3, await Database.CountAsync("SELECT count(*) FROM patrimoine.versions_plan_batiment"));

        var courante = await LireAsync($"{Plans}/{plan}/versions/2", responsable);
        var historique = await LireAsync($"{Plans}/{plan}/versions", responsable);
        var page = await LireAsync($"{Plans}/{plan}/versions?avant=2&limite=1", responsable);
        var ancienne = await LireAsync($"{Plans}/{plan}/versions/1", responsable);
        Assert.Equal(2, courante.GetProperty("version").GetInt32());
        Assert.Equal(1, courante.GetProperty("schema").GetInt32());
        Assert.Equal("Relevé du 30/10", courante.GetProperty("motif").GetString());
        Assert.Equal(12, courante.GetProperty("contenu").GetProperty("pieces")[0].GetProperty("contour")[1].GetProperty("x").GetDouble());
        Assert.Equal(1, courante.GetProperty("contenu").GetProperty("schema").GetInt32()); // contenu renvoyable tel quel
        Assert.Equal([3, 2, 1], historique.EnumerateArray().Select(v => v.GetProperty("version").GetInt32()));
        Assert.Equal([1], page.EnumerateArray().Select(v => v.GetProperty("version").GetInt32()));
        Assert.Equal(10, ancienne.GetProperty("contenu").GetProperty("pieces")[0].GetProperty("contour")[1].GetProperty("x").GetDouble());
        var plans = await LireAsync(Plans, responsable);
        Assert.Equal(3, plans.EnumerateArray().Single().GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task ConcurrentSavesFromTheSameVersionKeepOneAndRefuseTheOther()
    {
        var (responsable, lieux) = await PreparerAsync();
        var plan = await CreerAsync(Plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, responsable);

        var reponses = await Task.WhenAll(
            EnregistrerAsync(plan, responsable, 0, Nef(lieux, largeur: 10)), EnregistrerAsync(plan, responsable, 0, Nef(lieux, largeur: 11)));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], reponses.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM patrimoine.versions_plan_batiment"));
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    [Fact]
    public async Task RestoringCopiesAnOldVersionIntoANewOneWithAMandatoryReason()
    {
        var (responsable, lieux) = await PreparerAsync();
        var technicien = await JetonAsync("technicien@inventaire.test");
        var banc = await CreerAsync($"{Base}/components", new { nom = "Banc", corpsEtat = "Mobilier", zoneId = lieux.Nef }, technicien);
        var coffre = await CreerAsync($"{Base}/components", new { nom = "Coffre", corpsEtat = "Mobilier", zoneId = lieux.Nef }, technicien);
        var plan = await CreerAsync(Plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, responsable);
        var avecBanc = Nef(lieux, largeur: 10) with
        {
            Composants = [new { composantId = banc, position = new { x = 2, y = 2 } }, new { composantId = coffre, position = new { x = 3, y = 3 } }],
        };
        using var premiere = await EnregistrerAsync(plan, responsable, version: 0, avecBanc);
        using var deuxieme = await EnregistrerAsync(plan, responsable, version: 1, Nef(lieux, largeur: 12));
        using var retrait = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{banc}/withdraw", technicien, new { motif = "Cédé", version = await XminAsync(banc) });
        using var reclasse = await EnvoyerAsync(HttpMethod.Put, $"{Base}/components/{coffre}", technicien,
            new { nom = "Coffre", corpsEtat = "EquipementsCulte", zoneId = lieux.Nef, soumisAVerification = false, version = await XminAsync(coffre) });

        using var sansMotif = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/versions/1/restore", responsable, new { commandeId = Guid.NewGuid(), version = 2 });
        using var inconnue = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/versions/9/restore", responsable, new { commandeId = Guid.NewGuid(), version = 2, motif = "Retour" });
        using var restauree = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/versions/1/restore", responsable, new { commandeId = Guid.NewGuid(), version = 2, motif = "Erreur de relevé" });
        var v1 = await LireAsync($"{Plans}/{plan}/versions/1", responsable);
        var v3 = await LireAsync($"{Plans}/{plan}", responsable);

        Assert.Equal(HttpStatusCode.NoContent, retrait.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reclasse.StatusCode);
        Assert.Equal("motif", await ChampAsync(sansMotif));
        Assert.Equal(HttpStatusCode.NotFound, inconnue.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restauree.StatusCode);
        var resultat = await restauree.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, resultat.GetProperty("version").GetInt32());
        Assert.Equal(["marqueur_retire", "marqueur_retire"], resultat.GetProperty("avertissements").EnumerateArray().Select(a => a.GetProperty("code").GetString()));
        // Retiré, ou devenu objet de valeur : signalé ; la position de l'objet de valeur n'est plus servie.
        Assert.Equal([banc, coffre], v1.GetProperty("marqueursObsoletes").EnumerateArray().Select(m => m.GetGuid()));
        Assert.Equal([banc], v1.GetProperty("contenu").GetProperty("composants").EnumerateArray().Select(m => m.GetProperty("composantId").GetGuid()));
        Assert.Equal(1, v3.GetProperty("restaureDe").GetInt32());
        Assert.Equal("Erreur de relevé", v3.GetProperty("motif").GetString());
        Assert.Equal(v1.GetProperty("contenu").GetProperty("pieces").GetRawText(), v3.GetProperty("contenu").GetProperty("pieces").GetRawText());
        Assert.Empty(v3.GetProperty("contenu").GetProperty("composants").EnumerateArray());
    }

    [Fact]
    public async Task GeometryAndReferencesAreCheckedByTheServer()
    {
        var (responsable, lieux) = await PreparerAsync();
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (presbytere, _) = await BatimentAsync("Presbytère", technicien);
        var cuisine = await CreerAsync($"{Base}/zones", new { nom = "Cuisine", batimentId = presbytere }, technicien);
        var extincteur = await CreerAsync($"{Base}/components", new { nom = "Extincteur", corpsEtat = "SecuriteIncendie", zoneId = lieux.Nef }, technicien);
        var calice = await CreerAsync($"{Base}/components", new { nom = "Calice", corpsEtat = "EquipementsCulte", zoneId = lieux.Nef }, technicien);
        var plan = await CreerAsync(Plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, responsable);

        using var autoSecant = await EnregistrerAsync(plan, responsable, 0, Contenu(Piece(lieux.Nef, (0, 0), (10, 10), (10, 0), (0, 10))));
        using var autreNiveau = await EnregistrerAsync(plan, responsable, 0, Contenu(Piece(lieux.Tribune, (0, 0), (10, 0), (10, 8))));
        using var autreBatiment = await EnregistrerAsync(plan, responsable, 0, Contenu(Piece(cuisine, (0, 0), (10, 0), (10, 8))));
        using var chevauchement = await EnregistrerAsync(plan, responsable, 0, Contenu(
            Piece(lieux.Nef, (0, 0), (10, 0), (10, 8), (0, 8)), Piece(lieux.Choeur, (9.9, 0), (14, 0), (14, 8), (9.9, 8))));
        using var objetDeValeur = await EnregistrerAsync(plan, responsable, 0, Nef(lieux, 10) with
        {
            Composants = [new { composantId = calice, position = new { x = 2, y = 2 } }],
        });
        using var nonNumerique = await EnvoyerAsync(HttpMethod.Put, $"{Plans}/{plan}", responsable, new StringContent(
            $$$"""{"commandeId":"{{{Guid.NewGuid()}}}","version":0,"contenu":{"schema":1,"pieces":[{"zoneId":"{{{lieux.Nef}}}","contour":[{"x":"NaN","y":0},{"x":10,"y":0},{"x":10,"y":8}]}]}}""",
            Encoding.UTF8, "application/json"));
        using var horsZone = await EnregistrerAsync(plan, responsable, 0, Nef(lieux, 10) with
        {
            Composants = [new { composantId = extincteur, position = new { x = 20, y = 2 } }],
        });

        Assert.Equal("contenu.pieces[0].contour", await ChampAsync(autoSecant));
        Assert.Equal("contenu.pieces[0].zoneId", await ChampAsync(autreNiveau));
        Assert.Equal("contenu.pieces[0].zoneId", await ChampAsync(autreBatiment));
        Assert.Equal("contenu.pieces[1].contour", await ChampAsync(chevauchement));
        Assert.Equal("contenu.composants[0].composantId", await ChampAsync(objetDeValeur));
        Assert.Equal("contenu.pieces[0].contour[0]", await ChampAsync(nonNumerique)); // « NaN » lu en texte, refusé par la validation
        Assert.Equal(HttpStatusCode.OK, horsZone.StatusCode);
        var avertissement = (await horsZone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("avertissements")[0];
        Assert.Equal("marqueur_hors_zone", avertissement.GetProperty("code").GetString());
        Assert.Equal(extincteur, avertissement.GetProperty("composantId").GetGuid());
    }

    [Fact]
    public async Task APlanPerLevelOrASitePlanForABuildingWithoutLevels()
    {
        var (responsable, lieux) = await PreparerAsync();
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (exterieurs, _) = await BatimentAsync("Extérieurs", technicien);
        var autreNiveau = await CreerAsync($"{Base}/buildings/{exterieurs}/levels", new { nom = "Rez", ordre = 0 }, technicien);

        var etage = await CreerAsync(Plans, new { batimentId = lieux.Eglise, niveauId = lieux.Etage }, responsable);
        using var double_ = await EnvoyerAsync(HttpMethod.Post, Plans, responsable, new { batimentId = lieux.Eglise, niveauId = lieux.Etage });
        using var masseAvecNiveaux = await EnvoyerAsync(HttpMethod.Post, Plans, responsable, new { batimentId = lieux.Eglise });
        using var niveauEtranger = await EnvoyerAsync(HttpMethod.Post, Plans, responsable, new { batimentId = lieux.Eglise, niveauId = autreNiveau });
        using var batimentInconnu = await EnvoyerAsync(HttpMethod.Post, Plans, responsable, new { batimentId = Guid.NewGuid() });
        using var sansBatiment = await EnvoyerAsync(HttpMethod.Post, Plans, responsable, new { });
        var (parvis, _) = await BatimentAsync("Parvis", technicien);
        var masse = await CreerAsync(Plans, new { batimentId = parvis }, responsable);

        Assert.Equal(HttpStatusCode.Conflict, double_.StatusCode);
        var existant = await double_.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("plan_existant", existant.GetProperty("code").GetString());
        Assert.Equal(etage, existant.GetProperty("planId").GetGuid());
        Assert.Equal("niveauId", await ChampAsync(masseAvecNiveaux));
        Assert.Equal("niveauId", await ChampAsync(niveauEtranger));
        Assert.Equal(HttpStatusCode.NotFound, batimentInconnu.StatusCode);
        Assert.Equal("batimentId", await ChampAsync(sansBatiment));
        var plans = await LireAsync(Plans, responsable);
        Assert.Equal([etage, masse], plans.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.Null, plans[1].GetProperty("niveauId").ValueKind);
    }

    [Fact]
    public async Task OnlyManagersWriteInternalAccountsReadAndNothingLeaksAcrossOrganisations()
    {
        var (responsable, lieux) = await PreparerAsync();
        var technicien = await JetonAsync("technicien@inventaire.test");
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var admin = await JetonAsync("admin@inventaire.test");
        var plan = await CreerAsync(Plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, responsable);
        using var premiere = await EnregistrerAsync(plan, responsable, 0, Nef(lieux, 10));
        var etranger = await PlanEtrangerAsync();

        using var parTechnicien = await EnregistrerAsync(plan, technicien, 1, Nef(lieux, 12));
        using var creationPasteur = await EnvoyerAsync(HttpMethod.Post, Plans, pasteur, new { batimentId = lieux.Eglise, niveauId = lieux.Etage });
        using var restaurationTechnicien = await EnvoyerAsync(HttpMethod.Post, $"{Plans}/{plan}/versions/1/restore", technicien, new { commandeId = Guid.NewGuid(), version = 1, motif = "x" });
        using var lecturePasteur = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}", pasteur);
        using var lectureAdmin = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}", admin); // aucun droit métier implicite
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/building-plans/{plan}", pasteur);
        using var planEtranger = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{etranger}", pasteur);
        using var historiqueEtranger = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{etranger}/versions", pasteur);
        using var sansSession = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}", "jeton-invalide");
        var liste = await LireAsync(Plans, pasteur);
        await Database.CountAsync(
            "WITH r AS (UPDATE identite.membres SET revoque_le = now() WHERE compte_id = (SELECT id FROM identite.comptes WHERE email = 'pasteur@inventaire.test') RETURNING 1) SELECT count(*) FROM r");
        using var apresRevocation = await EnvoyerAsync(HttpMethod.Get, $"{Plans}/{plan}", pasteur);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, creationPasteur.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, restaurationTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lecturePasteur.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, lectureAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, planEtranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, historiqueEtranger.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sansSession.StatusCode);
        Assert.Equal([plan], liste.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, apresRevocation.StatusCode);
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM patrimoine.versions_plan_batiment"));
    }

    [Fact]
    public async Task TheApplicationRoleCannotRewriteAVersion()
    {
        var (responsable, lieux) = await PreparerAsync();
        var plan = await CreerAsync(Plans, new { batimentId = lieux.Eglise, niveauId = lieux.Rdc }, responsable);
        using var premiere = await EnregistrerAsync(plan, responsable, 0, Nef(lieux, 10));

        Assert.Equal(0, await Database.CountAsync(
            """
            SELECT count(*) FROM information_schema.role_table_grants
            WHERE grantee = 'gestionsite_app' AND table_schema = 'patrimoine'
              AND table_name IN ('plans_batiment', 'versions_plan_batiment') AND privilege_type IN ('UPDATE', 'DELETE')
            """));
    }

    // Église : rez-de-chaussée (nef, chœur) et tribune à l'étage ; un responsable sur le site.
    private async Task<(string Responsable, Lieux Lieux)> PreparerAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (eglise, _) = await BatimentAsync("Église", technicien);
        var rdc = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "Rez-de-chaussée", ordre = 0 }, technicien);
        var etage = await CreerAsync($"{Base}/buildings/{eglise}/levels", new { nom = "Tribune", ordre = 1 }, technicien);
        var nef = await CreerAsync($"{Base}/zones", new { nom = "Nef", batimentId = eglise, niveauId = rdc }, technicien);
        var choeur = await CreerAsync($"{Base}/zones", new { nom = "Chœur", batimentId = eglise, niveauId = rdc }, technicien);
        var tribune = await CreerAsync($"{Base}/zones", new { nom = "Tribune", batimentId = eglise, niveauId = etage }, technicien);
        await Database.CountAsync(
            "WITH r AS (UPDATE identite.comptes SET nom_affiche = 'Rose Responsable' WHERE email = 'responsable@inventaire.test' RETURNING 1) SELECT count(*) FROM r");
        return (await JetonAsync("responsable@inventaire.test"), new Lieux(eglise, rdc, etage, nef, choeur, tribune));
    }

    private async Task<(Guid Batiment, Guid PartiesCommunes)> BatimentAsync(string nom, string jeton)
    {
        var batiment = await CreerAsync($"{Base}/buildings", new { nom }, jeton);
        using var structure = await EnvoyerAsync(HttpMethod.Get, $"{Base}/structure", jeton);
        var zones = (await structure.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Single(b => b.GetProperty("id").GetGuid() == batiment).GetProperty("zones");
        return (batiment, zones.EnumerateArray().Single(z => z.GetProperty("partiesCommunes").GetBoolean()).GetProperty("id").GetGuid());
    }

    // Plan d'une autre organisation, écrit directement en base (avec son bâtiment).
    private async Task<Guid> PlanEtrangerAsync()
    {
        var plan = Guid.NewGuid();
        Assert.Equal(1, await Database.CountAsync(
            """
            WITH b AS (
                INSERT INTO patrimoine.batiments (id, organisation_id, site_id, nom, cree_le)
                VALUES (gen_random_uuid(), @organisation, @site, 'Ailleurs', now()) RETURNING id, organisation_id, site_id),
            p AS (
                INSERT INTO patrimoine.plans_batiment (id, organisation_id, site_id, batiment_id, cree_le)
                SELECT @plan, organisation_id, site_id, id, now() FROM b RETURNING 1)
            SELECT count(*) FROM p
            """,
            ("organisation", Guid.NewGuid()), ("site", Site), ("plan", plan)));
        return plan;
    }

    private async Task<uint> XminAsync(Guid composant)
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        return (await LireAsync($"{Base}/components/{composant}", technicien)).GetProperty("composant").GetProperty("version").GetUInt32();
    }

    private Task<HttpResponseMessage> EnregistrerAsync(
        Guid plan, string jeton, int version, ContenuEssai contenu, Guid? commandeId = null, string? motif = null) =>
        EnvoyerAsync(HttpMethod.Put, $"{Plans}/{plan}", jeton, new { commandeId = commandeId ?? Guid.NewGuid(), version, motif, contenu });

    private static async Task<int> VersionAsync(HttpResponseMessage reponse)
    {
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();
    }

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static ContenuEssai Nef(Lieux lieux, double largeur) => Contenu(Piece(lieux.Nef, (0, 0), (largeur, 0), (largeur, 8), (0, 8)));

    private static ContenuEssai Contenu(params object[] pieces) => new(1, pieces, [], [], []);

    private static object Piece(Guid zone, params (double X, double Y)[] sommets) =>
        new { zoneId = zone, contour = sommets.Select(s => new { x = s.X, y = s.Y }).ToArray() };

    private sealed record Lieux(Guid Eglise, Guid Rdc, Guid Etage, Guid Nef, Guid Choeur, Guid Tribune);

    private sealed record ContenuEssai(int Schema, object[] Pieces, object[] Ouvertures, object[] Escaliers, object[] Composants);
}

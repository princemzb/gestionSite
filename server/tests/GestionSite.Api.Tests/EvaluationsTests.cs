using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Api.Tests;

/// <summary>Évaluations des composants (US-11, S11-02) : état ou répartition, grille en vigueur, photos, rejeu.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class EvaluationsTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private static readonly string[] CapotFerme = ["Capot fermé"];

    [Fact]
    public async Task AnIndividualComponentIsEvaluatedAgainstTheGridInForce()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var (tableau, _) = await ComposantsAsync(technicien);

        using var parPasteur = await EvaluerAsync(tableau, Individuel("Degrade"), pasteur);
        using var reponse = await EvaluerAsync(tableau, Individuel("Degrade", constats: [true, true, false], mesures: [48.500m]), technicien);
        var resultat = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        var lue = (await ListerAsync(tableau, pasteur)).Single();
        var points = lue.GetProperty("points").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.Forbidden, parPasteur.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(5m, resultat.GetProperty("note").GetDecimal());
        Assert.Equal("Degrade", resultat.GetProperty("libelle").GetString());
        Assert.False(resultat.GetProperty("proposerDossier").GetBoolean());
        Assert.Equal("Degrade", lue.GetProperty("etat").GetString());
        Assert.Equal(1, lue.GetProperty("grilleVersion").GetInt32());
        Assert.Equal(5, points.Count); // grille Courants forts : 5 points, tous gardés avec leur texte
        Assert.Equal("Pas de trace d'échauffement (noircissement, odeur)", points[2].GetProperty("texte").GetString());
        Assert.False(points[2].GetProperty("conforme").GetBoolean());
        Assert.Equal(JsonValueKind.Null, points[4].GetProperty("conforme").ValueKind);
        Assert.Equal("°C", lue.GetProperty("mesures")[0].GetProperty("unite").GetString());
        Assert.Equal("48.5", lue.GetProperty("mesures")[0].GetProperty("valeur").GetRawText()); // zéros de fin retirés
        Assert.Equal(0, lue.GetProperty("photos").GetInt32());
    }

    [Fact]
    public async Task ALotDistributionBecomesTheInventoryDistributionAndCriticalProposesACase()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (_, chaises) = await ComposantsAsync(technicien);
        var repartition = new { bon = 30, degrade = 8, critique = 2 };

        using var sansPhoto = await EvaluerAsync(chaises, Lot(repartition), technicien);
        using var somme = await EvaluerAsync(chaises, Lot(new { bon = 30 }), technicien, photo: true);
        using var etat = await EvaluerAsync(chaises, Individuel("Bon"), technicien);
        using var reponse = await EvaluerAsync(chaises, Lot(repartition), technicien, photo: true);
        var resultat = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        using var memeRepartition = await EvaluerAsync(chaises, Lot(repartition), technicien, photo: true);
        var fiche = await LireAsync($"{Base}/components/{chaises}", technicien);
        var lue = (await ListerAsync(chaises, technicien))[^1];
        using var photo = await EnvoyerAsync(HttpMethod.Get, $"{Base}/components/{chaises}/evaluations/{lue.GetProperty("id").GetGuid()}/photos/1", technicien);
        using var absente = await EnvoyerAsync(HttpMethod.Get, $"{Base}/components/{chaises}/evaluations/{lue.GetProperty("id").GetGuid()}/photos/2", technicien);

        Assert.Equal("photos", await ChampAsync(sansPhoto));
        Assert.Equal("repartition", await ChampAsync(somme));
        Assert.Equal("repartition", await ChampAsync(etat));
        Assert.Equal(7.8m, resultat.GetProperty("note").GetDecimal()); // (30×9 + 8×5 + 2×1) / 40, DEC-01
        Assert.Equal("Satisfaisant", resultat.GetProperty("libelle").GetString());
        Assert.True(resultat.GetProperty("proposerDossier").GetBoolean());
        var lot = fiche.GetProperty("composant").GetProperty("lot").GetProperty("repartition");
        Assert.Equal((30, 8, 2), (lot.GetProperty("bon").GetInt32(), lot.GetProperty("degrade").GetInt32(), lot.GetProperty("critique").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, memeRepartition.StatusCode);
        Assert.Equal(2, fiche.GetProperty("historique").GetArrayLength()); // création, une seule évaluation qui change le lot
        var evenement = fiche.GetProperty("historique")[0];
        Assert.Equal("Evaluation", evenement.GetProperty("nature").GetString());
        Assert.Equal(40, evenement.GetProperty("avant").GetProperty("bon").GetInt32());
        Assert.Equal(30, evenement.GetProperty("apres").GetProperty("bon").GetInt32());
        Assert.Equal(1, lue.GetProperty("photos").GetInt32());
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, absente.StatusCode);
    }

    [Fact]
    public async Task AReplayGivesTheSameEvaluationAndAStaleGridIsRefused()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (tableau, _) = await ComposantsAsync(technicien);
        var commande = Guid.NewGuid();

        using var premiere = await EvaluerAsync(tableau, Individuel("Bon", commande), technicien);
        using var rejouee = await EvaluerAsync(tableau, Individuel("Bon", commande), technicien);
        using var reutilisee = await EvaluerAsync(tableau, Individuel("Satisfaisant", commande), technicien);
        using var adaptation = await EnvoyerAsync(HttpMethod.Put, $"{Base}/grids/CourantsForts", responsable, new { criteres = CapotFerme, version = 1 });
        using var perimee = await EvaluerAsync(tableau, Individuel("Bon"), technicien);
        using var rejoueeApres = await EvaluerAsync(tableau, Individuel("Bon", commande), technicien);
        using var critiqueSansPhoto = await EvaluerAsync(tableau, Individuel("Critique", version: 2), technicien);
        using var critique = await EvaluerAsync(tableau, Individuel("Critique", version: 2), technicien, photo: true);

        var id = (await premiere.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(id, (await rejouee.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal("commande_reutilisee", await CodeAsync(reutilisee));
        Assert.Equal(HttpStatusCode.OK, adaptation.StatusCode);
        Assert.Equal("grille_modifiee", await CodeAsync(perimee));
        Assert.Equal(id, (await rejoueeApres.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal("photos", await ChampAsync(critiqueSansPhoto));
        Assert.True((await critique.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proposerDossier").GetBoolean());
        var lues = await ListerAsync(tableau, technicien);
        Assert.Equal(["Critique", "Bon"], lues.Select(e => e.GetProperty("etat").GetString()));
        Assert.Equal(["Capot fermé"], lues[0].GetProperty("points").EnumerateArray().Select(p => p.GetProperty("texte").GetString()));
        Assert.Equal(5, lues[1].GetProperty("points").GetArrayLength()); // l'ancienne évaluation garde la grille présentée
    }

    [Fact]
    public async Task InvalidEvaluationsAreRefusedAfterAccess()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (tableau, _) = await ComposantsAsync(technicien);

        using var parPasteur = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{tableau}/evaluations", await JetonAsync("pasteur@inventaire.test"),
            new StringContent("{illisible", System.Text.Encoding.UTF8, "application/json"));
        using var inconnu = await EvaluerAsync(Guid.NewGuid(), Individuel("Bon"), technicien);
        using var autreSite = await EnvoyerAsync(
            HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/components/{tableau}/evaluations", technicien);
        using var sansCommande = await EvaluerAsync(tableau, new { grilleVersion = 1, etat = "Bon" }, technicien);
        using var sansVersion = await EvaluerAsync(tableau, new { commandeId = Guid.NewGuid(), etat = "Bon" }, technicien);
        using var sansEtat = await EvaluerAsync(tableau, Individuel(""), technicien);
        using var tropDeConstats = await EvaluerAsync(tableau, Individuel("Bon", constats: [true, true, true, true, true, true]), technicien);
        using var mesure = await EvaluerAsync(tableau, Individuel("Bon", mesures: [1.2345m]), technicien);
        using var retrait = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{tableau}/withdraw", technicien,
            new { motif = "Remplacé", version = (await LireAsync($"{Base}/components/{tableau}", technicien)).GetProperty("composant").GetProperty("version").GetUInt32() });
        using var retire = await EvaluerAsync(tableau, Individuel("Bon"), technicien);

        Assert.Equal(HttpStatusCode.Forbidden, parPasteur.StatusCode); // accès contrôlé avant la lecture du corps
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal("commandeId", await ChampAsync(sansCommande));
        Assert.Equal("grilleVersion", await ChampAsync(sansVersion));
        Assert.Equal("etat", await ChampAsync(sansEtat));
        Assert.Equal("constats", await ChampAsync(tropDeConstats));
        Assert.Equal("mesures", await ChampAsync(mesure));
        Assert.Equal(HttpStatusCode.NoContent, retrait.StatusCode);
        Assert.Equal("composant_retire", await CodeAsync(retire));
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM patrimoine.evaluations"));
    }

    [Fact]
    public async Task TheSiteScoreIsRecomputedFromEvaluationsWithTheWorstZoneFirst()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var pasteur = await JetonAsync("pasteur@inventaire.test");
        var (tableau, chaises) = await ComposantsAsync(technicien);
        var sacristie = await CreerAsync($"{Base}/zones", new { batimentId = await BatimentAsync(technicien), nom = "Sacristie" }, technicien);
        var armoire = await CreerAsync($"{Base}/components", new { nom = "Armoire", corpsEtat = "Mobilier", zoneId = sacristie }, technicien);
        var vide = await LireAsync($"{Base}/score", pasteur);

        using var lot = await EvaluerAsync(chaises, Lot(new { bon = 30, degrade = 8, critique = 2 }), technicien, photo: true);
        using var individuel = await EvaluerAsync(tableau, Individuel("Degrade"), technicien);
        var score = await LireAsync($"{Base}/score", pasteur);
        var zones = score.GetProperty("zones").EnumerateArray().ToList();
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/inventory/score", pasteur);

        Assert.Equal(JsonValueKind.Null, vide.GetProperty("note").ValueKind);
        Assert.Equal(3, vide.GetProperty("couverture").GetProperty("actifs").GetInt32());
        Assert.Equal(6.4m, score.GetProperty("note").GetDecimal()); // seule zone évaluée : (7,8 + 5) / 2
        Assert.Equal("Satisfaisant", score.GetProperty("libelle").GetString());
        Assert.Equal((2, 3), (score.GetProperty("couverture").GetProperty("evalues").GetInt32(), score.GetProperty("couverture").GetProperty("actifs").GetInt32()));
        Assert.Equal(1, score.GetProperty("critiques").GetInt32());
        Assert.Equal("Parties communes", zones[0].GetProperty("nom").GetString()); // notée d'abord, puis non évaluées
        Assert.Equal(0, zones[0].GetProperty("aEvaluer").GetInt32());
        Assert.Equal("Sacristie", zones[1].GetProperty("nom").GetString());
        Assert.Equal(1, zones[1].GetProperty("aEvaluer").GetInt32());
        Assert.Equal(12, score.GetProperty("historique").GetArrayLength());
        Assert.Equal(6.4m, score.GetProperty("historique")[11].GetProperty("note").GetDecimal());
        Assert.Equal(12, zones[0].GetProperty("historique").GetArrayLength());
        Assert.Equal(6.4m, zones[0].GetProperty("historique")[11].GetProperty("note").GetDecimal());
        Assert.Equal(JsonValueKind.Null, zones[1].GetProperty("historique")[11].GetProperty("note").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);

        using var mauvaise = await EvaluerAsync(armoire, Individuel("Mauvais"), technicien, photo: true);
        var apres = (await LireAsync($"{Base}/score", pasteur)).GetProperty("zones");
        Assert.Equal(["Sacristie", "Parties communes"], apres.EnumerateArray().Select(z => z.GetProperty("nom").GetString())); // 3 avant 6,4
    }

    [Fact]
    public async Task TheLatestEvaluationBeforeTheHistoryWindowStillCountsAsOld()
    {
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (tableau, _) = await ComposantsAsync(technicien);
        var zone = (await LireAsync($"{Base}/components/{tableau}", technicien)).GetProperty("composant").GetProperty("zoneId").GetGuid();
        foreach (var (etat, ans) in new[] { ("Bon", 3), ("Mauvais", 2) })
        {
            Assert.Equal(1, await Database.CountAsync(
                """
                WITH ajout AS (
                    INSERT INTO patrimoine.evaluations (id, organisation_id, site_id, composant_id, zone_id, corps_etat, commande_id, grille_version,
                        points, mesures, etat, evaluateur_compte_id, evalue_le)
                    VALUES (gen_random_uuid(), @organisation, @site, @composant, @zone, 'CourantsForts', gen_random_uuid(), 1,
                        '[]', '[]', @etat, gen_random_uuid(), now() - make_interval(years => @ans)) RETURNING 1)
                SELECT count(*) FROM ajout
                """,
                ("organisation", Organisation), ("site", Site), ("composant", tableau), ("zone", zone), ("etat", etat), ("ans", ans)));
        }

        var score = await LireAsync($"{Base}/score", technicien);
        var communes = score.GetProperty("zones")[0];

        Assert.Equal(3m, score.GetProperty("note").GetDecimal()); // la plus récente (Mauvais), il y a 2 ans
        Assert.Equal(1, communes.GetProperty("anciennes").GetInt32());
        Assert.Equal(1m, score.GetProperty("partAncienne").GetDecimal());
        Assert.Equal(3m, score.GetProperty("historique")[11].GetProperty("note").GetDecimal());
        Assert.Equal(JsonValueKind.Null, score.GetProperty("historique")[10].GetProperty("note").ValueKind); // composant créé ce mois-ci
    }

    private static object Individuel(string etat, Guid? commande = null, int version = 1, bool?[]? constats = null, decimal?[]? mesures = null) =>
        new { commandeId = commande ?? Guid.NewGuid(), grilleVersion = version, etat, constats, mesures, remarque = "Relevé sur place" };

    private static object Lot(object repartition) => new { commandeId = Guid.NewGuid(), grilleVersion = 1, repartition };

    private async Task<Guid> BatimentAsync(string jeton) =>
        (await LireAsync($"{Base}/structure", jeton)).EnumerateArray().Single().GetProperty("id").GetGuid();

    // Tableau divisionnaire (individuel) et 40 chaises (lot, toutes « bon »), dans les parties communes d'un bâtiment.
    private async Task<(Guid Tableau, Guid Chaises)> ComposantsAsync(string jeton)
    {
        var batiment = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, jeton);
        var structure = await LireAsync($"{Base}/structure", jeton);
        var zone = structure.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batiment).GetProperty("zones")
            .EnumerateArray().Single(z => z.GetProperty("partiesCommunes").GetBoolean()).GetProperty("id").GetGuid();
        var tableau = await CreerAsync($"{Base}/components", new { nom = "Tableau divisionnaire", corpsEtat = "CourantsForts", zoneId = zone }, jeton);
        var chaises = await CreerAsync($"{Base}/components", new
        {
            nom = "Chaises",
            corpsEtat = "Mobilier",
            zoneId = zone,
            lot = new { unite = "Unite", quantite = 40, repartition = new { bon = 40 } },
        }, jeton);
        return (tableau, chaises);
    }

    private async Task<HttpResponseMessage> EvaluerAsync(Guid composant, object demande, string jeton, bool photo = false)
    {
        using var formulaire = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(demande)), "demande" } };
        if (photo)
        {
            using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 64, 48) { Format = ImageMagick.MagickFormat.Jpeg };
            var contenu = new ByteArrayContent(image.ToByteArray());
            contenu.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formulaire.Add(contenu, "photos", "constat.jpg");
        }

        return await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{composant}/evaluations", jeton, formulaire);
    }

    private async Task<JsonElement[]> ListerAsync(Guid composant, string jeton) =>
        [.. (await LireAsync($"{Base}/components/{composant}/evaluations", jeton)).EnumerateArray()];

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage reponse)
    {
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    }
}

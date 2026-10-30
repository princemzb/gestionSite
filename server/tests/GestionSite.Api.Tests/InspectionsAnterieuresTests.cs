using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Api.Tests;

/// <summary>
/// Correctifs d'audit US-12 : l'évaluation d'une inspection saisie après coup est datée de sa réalisation et ne masque
/// jamais une évaluation plus récente (score, points critiques, répartition d'un lot) ; rejeu concurrent d'une réalisation.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class InspectionsAnterieuresTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Preventif => Base.Replace("/inventory", "/preventive", StringComparison.Ordinal);

    private static DateOnly Aujourdhui => JourCivil.Du(DateTimeOffset.UtcNow);

    [Fact]
    public async Task ABackdatedInspectionNeverHidesAMoreRecentEvaluation()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (tableau, chaises) = await ComposantsAsync(technicien);
        using var critique = await EvaluerAsync(tableau, new { commandeId = Guid.NewGuid(), grilleVersion = 1, etat = "Critique" }, technicien, photo: true);
        using var lot = await EvaluerAsync(chaises, new { commandeId = Guid.NewGuid(), grilleVersion = 1, repartition = new { bon = 30, critique = 10 } }, technicien, photo: true);
        var planTableau = await PlanAsync(responsable, tableau);
        var planChaises = await PlanAsync(responsable, chaises);
        var anterieure = Aujourdhui.AddDays(-40);

        using var inspectionTableau = await RealiserAsync(planTableau, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance = Aujourdhui,
            realiseLe = anterieure,
            resultat = "Conforme",
            grilleVersion = 1,
            etat = "Bon",
        });
        using var inspectionChaises = await RealiserAsync(planChaises, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance = Aujourdhui,
            realiseLe = anterieure,
            resultat = "Conforme",
            grilleVersion = 1,
            repartition = new { bon = 40 },
        });
        var score = await LireAsync($"{Base}/score", technicien);
        var evaluations = (await LireAsync($"{Base}/components/{tableau}/evaluations", technicien)).EnumerateArray().ToList();
        var repartition = (await LireAsync($"{Base}/components/{chaises}", technicien)).GetProperty("composant").GetProperty("lot").GetProperty("repartition");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (critique.StatusCode, lot.StatusCode));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (inspectionTableau.StatusCode, inspectionChaises.StatusCode));
        Assert.Equal(["Critique", "Bon"], evaluations.Select(e => e.GetProperty("etat").GetString())); // la plus récente d'abord
        Assert.Equal(anterieure, JourCivil.Du(evaluations[1].GetProperty("evalueLe").GetDateTimeOffset())); // datée de la réalisation
        Assert.Contains(score.GetProperty("critiques").EnumerateArray(), c => c.GetProperty("id").GetGuid() == tableau); // toujours critique
        Assert.Equal((30, 10), (repartition.GetProperty("bon").GetInt32(), repartition.GetProperty("critique").GetInt32())); // lot non réécrit
    }

    [Fact]
    public async Task ConcurrentReplaysOfTheSameRealisationGetTheSameAnswer()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        var responsable = await JetonAsync("responsable@inventaire.test");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var (tableau, _) = await ComposantsAsync(technicien);
        var plan = await PlanAsync(responsable, tableau, "Entretien");
        var demande = new { commandeId = Guid.NewGuid(), echeance = Aujourdhui, realiseLe = Aujourdhui, resultat = "Conforme", compteRendu = "Serrage" };

        var reponses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RealiserAsync(plan, responsable, demande)));
        var ids = new List<Guid>();
        foreach (var reponse in reponses)
        {
            using (reponse)
            {
                Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
                ids.Add((await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            }
        }

        Assert.Single(ids.Distinct());
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM maintenance.controles_preventifs"));
    }

    private async Task<(Guid Tableau, Guid Chaises)> ComposantsAsync(string technicien)
    {
        await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var communes = (await LireAsync($"{Base}/structure", technicien)).EnumerateArray().Single().GetProperty("zones")[0].GetProperty("id").GetGuid();
        var tableau = await CreerAsync($"{Base}/components", new { nom = "Tableau général", corpsEtat = "CourantsForts", zoneId = communes }, technicien);
        var chaises = await CreerAsync($"{Base}/components", new
        {
            nom = "Chaises",
            corpsEtat = "Mobilier",
            zoneId = communes,
            soumisAVerification = true,
            lot = new { unite = "Unite", quantite = 40, repartition = new { bon = 40 } },
        }, technicien);
        return (tableau, chaises);
    }

    private async Task<Guid> PlanAsync(string jeton, Guid composant, string nature = "InspectionInterne")
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", jeton, new
        {
            commandeId = Guid.NewGuid(),
            cible = new { type = "Composant", composantId = composant },
            nature,
            libelle = "Contrôle",
            @base = "BonnePratique",
            periode = new { valeur = 1, unite = "Ans" },
            premiereEcheance = Aujourdhui,
        });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> EvaluerAsync(Guid composant, object demande, string jeton, bool photo)
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

    private async Task<HttpResponseMessage> RealiserAsync(Guid plan, string jeton, object demande)
    {
        using var formulaire = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(demande)), "demande" } };
        return await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{plan}/perform", jeton, formulaire);
    }

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Api.Tests;

/// <summary>Non-conformités vers les dossiers et levée des réserves (US-12, S12-05, RG-09, T-03.19, T-03.20, PRD 14).</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class ConstatsInspectionTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private static readonly System.Globalization.CultureInfo Invariante = System.Globalization.CultureInfo.InvariantCulture;

    private string Preventif => Base.Replace("/inventory", "/preventive", StringComparison.Ordinal);

    private string Dossiers => Base.Replace("/inventory", "/cases", StringComparison.Ordinal);

    private static DateOnly Aujourdhui => JourCivil.Du(DateTimeOffset.UtcNow);

    [Fact]
    public async Task ADefectOpensACaseAndTheSameDefectLaterIsAttachedWithItsDangerAlert()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var (tableau, communes) = await TableauAsync(technicien);
        var plan = await PlanAsync(responsable, new { type = "Composant", composantId = tableau }, "InspectionInterne");
        var idTechnicien = (await LireAsync("/me", technicien)).GetProperty("compteId").GetGuid();
        object Inspection(Guid commande, DateOnly echeance, bool danger) => new
        {
            commandeId = commande,
            echeance,
            realiseLe = Aujourdhui,
            resultat = "NonConforme",
            grilleVersion = 1,
            etat = "Degrade",
            points = new object[] { new { conforme = true, danger = false }, new { conforme = true, danger = false }, new { conforme = false, danger } },
        };
        var premiere = await EcheanceAsync(plan, pasteur);
        using var affecte = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{plan}/assign", responsable, new { echeance = premiere, intervenantCompteId = idTechnicien, version = 0 });
        var commande = Guid.NewGuid();

        using var inspection = await RealiserAsync(plan, technicien, Inspection(commande, premiere, danger: false));
        using var rejoue = await RealiserAsync(plan, technicien, Inspection(commande, premiere, danger: false));
        var dossiers = (await LireAsync(Dossiers, pasteur)).EnumerateArray().ToList();
        var dossier = await LireAsync($"{Dossiers}/{dossiers.Single().GetProperty("id").GetGuid()}", pasteur);
        var seconde = await EcheanceAsync(plan, pasteur);
        using var dangereuse = await RealiserAsync(plan, responsable, Inspection(Guid.NewGuid(), seconde, danger: true)); // même défaut, un an plus tôt saisi
        var apres = (await LireAsync(Dossiers, pasteur)).EnumerateArray().ToList();
        var rattache = await LireAsync($"{Dossiers}/{dossiers.Single().GetProperty("id").GetGuid()}", responsable);
        var controle = await LireAsync($"{Preventif}/controls/{(await dangereuse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()}", pasteur);

        Assert.Equal(HttpStatusCode.NoContent, affecte.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inspection.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejoue.StatusCode);
        Assert.Single(dossiers); // rejeu : aucun second dossier
        Assert.Equal(("AQualifier", "Inspection"), (dossier.GetProperty("statut").GetString(), dossier.GetProperty("origine").GetString()));
        Assert.Equal(tableau, dossier.GetProperty("lieu").GetProperty("composantId").GetGuid());
        Assert.Equal(communes, dossier.GetProperty("lieu").GetProperty("zoneId").GetGuid());
        Assert.StartsWith("Contrôle : Pas de trace d'échauffement", dossier.GetProperty("titre").GetString(), StringComparison.Ordinal);
        Assert.Equal(idTechnicien, dossier.GetProperty("historique")[0].GetProperty("auteurCompteId").GetGuid()); // l'auteur du constat
        Assert.Equal(JsonValueKind.Null, dossier.GetProperty("contactDeclarant").ValueKind);
        Assert.False(dossier.GetProperty("danger").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, dossier.GetProperty("controleId").ValueKind);
        // T-03.19 / T-03.20 : rattaché au dossier ouvert, aucun nouveau dossier, alerte danger enregistrée.
        Assert.Single(apres);
        Assert.True(apres[0].GetProperty("nouveauConstat").GetBoolean());
        Assert.Equal("ConstatInspection", rattache.GetProperty("historique")[0].GetProperty("nature").GetString());
        Assert.True(rattache.GetProperty("nouveauConstat").GetBoolean());
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM maintenance.alertes_danger WHERE constat_id IS NOT NULL"));
        Assert.Equal(2, await Database.CountAsync("SELECT count(*) FROM maintenance.constats_controles"));
        var constat = controle.GetProperty("constats").EnumerateArray().Single();
        Assert.True(constat.GetProperty("rattache").GetBoolean());
        Assert.True(constat.GetProperty("danger").GetBoolean());
        Assert.False(controle.GetProperty("reservesLevees").GetBoolean());

        // US-09 inchangé : le dossier d'inspection se qualifie comme les autres ; la qualification efface la marque.
        using var qualifie = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{rattache.GetProperty("id").GetGuid()}/qualify", responsable,
            new { priorite = "Haute", zoneId = communes, composantId = tableau, version = rattache.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.NoContent, qualifie.StatusCode);
        Assert.False((await LireAsync(Dossiers, pasteur)).EnumerateArray().Single().GetProperty("nouveauConstat").GetBoolean());
    }

    [Fact]
    public async Task ObservationsOpenCasesInTheirZoneAndReservationsAreLiftedWhenTheyAreClosed()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var (_, communes) = await TableauAsync(technicien);
        var eglise = (await LireAsync($"{Base}/structure", technicien)).EnumerateArray().Single().GetProperty("id").GetGuid();
        var sacristie = await CreerAsync($"{Base}/zones", new { batimentId = eglise, nom = "Sacristie" }, technicien);
        var plan = await PlanAsync(responsable, new { type = "Batiment", batimentId = eglise }, "VerificationExterieure");
        var echeance = await EcheanceAsync(plan, pasteur);

        using var verification = await RealiserAsync(plan, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance,
            realiseLe = Aujourdhui,
            resultat = "AvecObservations",
            prestataire = "Socotec",
            observations = new object[]
            {
                new { libelle = "Prise arrachée", zoneId = sacristie, danger = true },
                new { libelle = "Absence de plan d'évacuation", danger = false },
                new { libelle = "  prise   ARRACHÉE ", zoneId = sacristie, danger = false }, // même écart, même zone : un seul dossier
            },
        });
        var resultat = await verification.Content.ReadFromJsonAsync<JsonElement>();
        var dossiers = (await LireAsync(Dossiers, pasteur)).EnumerateArray().ToList();
        var parZone = dossiers.ToDictionary(d => d.GetProperty("zoneId").GetGuid());

        Assert.Equal("RealiseeAvecReserves", resultat.GetProperty("statut").GetString());
        Assert.Equal(2, dossiers.Count);
        Assert.True(parZone[sacristie].GetProperty("danger").GetBoolean());
        Assert.Contains(communes, parZone.Keys);
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM maintenance.alertes_danger"));

        foreach (var dossier in dossiers)
        {
            var lu = await LireAsync($"{Dossiers}/{dossier.GetProperty("id").GetGuid()}", responsable);
            using var classe = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{lu.GetProperty("id").GetGuid()}/dismiss", responsable,
                new { motif = "DejaTraite", version = lu.GetProperty("version").GetUInt32() });
            Assert.Equal(HttpStatusCode.NoContent, classe.StatusCode);
        }

        var fiche = await LireAsync($"{Preventif}/plans/{plan}", pasteur);
        var controle = await LireAsync($"{Preventif}/controls/{resultat.GetProperty("id").GetGuid()}", pasteur);
        Assert.Equal("ReservesLevees", fiche.GetProperty("occurrences")[0].GetProperty("statut").GetString());
        Assert.True(controle.GetProperty("reservesLevees").GetBoolean());
        Assert.Equal(3, controle.GetProperty("constats").GetArrayLength());
    }

    private async Task<(string Responsable, string Technicien, string Pasteur)> ComptesAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        return (await JetonAsync("responsable@inventaire.test"), await JetonAsync("technicien@inventaire.test"), await JetonAsync("pasteur@inventaire.test"));
    }

    private async Task<(Guid Tableau, Guid Communes)> TableauAsync(string technicien)
    {
        await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var communes = (await LireAsync($"{Base}/structure", technicien)).EnumerateArray().First().GetProperty("zones")[0].GetProperty("id").GetGuid();
        return (await CreerAsync($"{Base}/components", new { nom = "Tableau général", corpsEtat = "CourantsForts", zoneId = communes }, technicien), communes);
    }

    private async Task<Guid> PlanAsync(string jeton, object cible, string nature)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", jeton, new
        {
            commandeId = Guid.NewGuid(),
            cible,
            nature,
            libelle = "Contrôle",
            @base = "BonnePratique",
            periode = new { valeur = 1, unite = "Ans" },
            premiereEcheance = Aujourdhui,
        });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<DateOnly> EcheanceAsync(Guid plan, string jeton) =>
        DateOnly.Parse((await LireAsync($"{Preventif}/plans/{plan}", jeton)).GetProperty("plan").GetProperty("prochaine").GetProperty("echeance").GetString()!, Invariante);

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

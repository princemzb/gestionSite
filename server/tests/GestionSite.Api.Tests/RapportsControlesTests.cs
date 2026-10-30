using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Api.Tests;

/// <summary>Vérification extérieure et rapport PDF (US-12, S12-04) : signature, taille, obligation, accès, observations.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class RapportsControlesTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    private string Preventif => Base.Replace("/inventory", "/preventive", StringComparison.Ordinal);

    private static DateOnly Aujourdhui => JourCivil.Du(DateTimeOffset.UtcNow);

    [Fact]
    public async Task ARegulatoryVerificationByAContractorNeedsARealPdfReport()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var (eglise, communes) = await EgliseAsync(technicien);
        var plan = await PlanAsync(responsable, eglise, "Reglementaire");
        var echeance = await EcheanceAsync(plan, pasteur);
        object Verification(Guid commande, string resultat = "AvecObservations", object[]? observations = null) => new
        {
            commandeId = commande,
            echeance,
            realiseLe = Aujourdhui,
            resultat,
            prestataire = "Bureau Véritas",
            numeroAgrement = "AG-2026-17",
            observations = observations ?? [new { libelle = "Absence de repérage des circuits au tableau", danger = false }],
        };

        using var sansRapport = await RealiserAsync(plan, responsable, Verification(Guid.NewGuid()));
        using var pasUnPdf = await RealiserAsync(plan, responsable, Verification(Guid.NewGuid()), rapport: Encoding.ASCII.GetBytes("<html>%PDF-</html>"));
        using var tropGros = await RealiserAsync(plan, responsable, Verification(Guid.NewGuid()), rapport: [.. Pdf, .. new byte[10 * 1024 * 1024]]);
        using var zoneInconnue = await RealiserAsync(plan, responsable, Verification(Guid.NewGuid(), observations: [new { libelle = "Fissure", zoneId = Guid.NewGuid() }]), Pdf);
        using var conformeAvecObservation = await RealiserAsync(plan, responsable, Verification(Guid.NewGuid(), resultat: "Conforme"), Pdf);
        using var realise = await RealiserAsync(plan, responsable, Verification(Guid.NewGuid(), observations:
        [
            new { libelle = "Absence de repérage des circuits au tableau", danger = false },
            new { libelle = "Prise arrachée dans la sacristie", zoneId = communes, danger = true },
        ]), Pdf);
        var resultat = await realise.Content.ReadFromJsonAsync<JsonElement>();
        var id = resultat.GetProperty("id").GetGuid();
        var controle = await LireAsync($"{Preventif}/controls/{id}", pasteur);
        using var telechargement = await EnvoyerAsync(HttpMethod.Get, $"{Preventif}/controls/{id}/report", pasteur);
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/preventive/controls/{id}/report", pasteur);
        using var autreOrganisation = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Guid.NewGuid()}/sites/{Site}/preventive/controls/{id}/report", pasteur);

        Assert.Equal("rapport", await ChampAsync(sansRapport));
        Assert.Equal("rapport", await ChampAsync(pasUnPdf));
        Assert.Equal("rapport", await ChampAsync(tropGros));
        Assert.Equal("observations", await ChampAsync(zoneInconnue));
        Assert.Equal("resultat", await ChampAsync(conformeAvecObservation));
        Assert.Equal(HttpStatusCode.OK, realise.StatusCode);
        Assert.Equal("RealiseeAvecReserves", resultat.GetProperty("statut").GetString());
        Assert.Equal(Pdf.Length, controle.GetProperty("rapport").GetProperty("taille").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Pdf)), controle.GetProperty("rapport").GetProperty("empreinte").GetString());
        Assert.Equal(("Bureau Véritas", "AG-2026-17"), (controle.GetProperty("prestataire").GetString(), controle.GetProperty("numeroAgrement").GetString()));
        var observations = controle.GetProperty("observations").EnumerateArray().ToList();
        Assert.Equal(2, observations.Count);
        Assert.Equal(communes, observations[0].GetProperty("zoneId").GetGuid()); // zone du plan à défaut : parties communes du bâtiment
        Assert.True(observations[1].GetProperty("danger").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, telechargement.StatusCode);
        Assert.Equal("application/pdf", telechargement.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", telechargement.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", telechargement.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", telechargement.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(Pdf, await telechargement.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreOrganisation.StatusCode);
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM maintenance.controles_preventifs"));
    }

    [Fact]
    public async Task TheReferentEntersTheReportWhichStaysOptionalOutsideRegulatoryContractorChecks()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        await MembreAsync("autre@inventaire.test", Role.Technicien);
        var autre = await JetonAsync("autre@inventaire.test");
        var (eglise, _) = await EgliseAsync(technicien);
        var pratique = await PlanAsync(responsable, eglise, "BonnePratique");
        var reglementaire = await PlanAsync(responsable, eglise, "Reglementaire", libelle: "Commission");
        var idTechnicien = (await LireAsync("/me", technicien)).GetProperty("compteId").GetGuid();
        var echeance = await EcheanceAsync(pratique, pasteur);
        using var affecte = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{pratique}/assign", responsable,
            new { echeance, prestataire = "Paratonnerres de l'Est", referentCompteId = idTechnicien, version = 0 });
        var demande = new { commandeId = Guid.NewGuid(), echeance, realiseLe = Aujourdhui, resultat = "Conforme", prestataire = "Paratonnerres de l'Est" };

        using var parAutre = await RealiserAsync(pratique, autre, demande);
        using var parReferent = await RealiserAsync(pratique, technicien, demande);
        var id = (await parReferent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var sansRapport = await EnvoyerAsync(HttpMethod.Get, $"{Preventif}/controls/{id}/report", pasteur);
        using var interne = await RealiserAsync(reglementaire, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance = await EcheanceAsync(reglementaire, pasteur),
            realiseLe = Aujourdhui,
            resultat = "Conforme",
        });
        using var observationEntretien = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", responsable, new
        {
            commandeId = Guid.NewGuid(),
            cible = new { type = "Batiment", batimentId = eglise },
            nature = "Entretien",
            libelle = "Chéneaux",
            @base = "BonnePratique",
            periode = new { valeur = 1, unite = "Ans" },
            premiereEcheance = Aujourdhui,
        });
        var entretien = (await observationEntretien.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var refusee = await RealiserAsync(entretien, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance = Aujourdhui,
            realiseLe = Aujourdhui,
            resultat = "AvecObservations",
            compteRendu = "Nettoyé",
            observations = new[] { new { libelle = "Tuile cassée" } },
        });

        Assert.Equal(HttpStatusCode.NoContent, affecte.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parAutre.StatusCode);
        Assert.Equal(HttpStatusCode.OK, parReferent.StatusCode); // bonne pratique : rapport facultatif
        Assert.Equal(HttpStatusCode.NotFound, sansRapport.StatusCode);
        Assert.Equal(HttpStatusCode.OK, interne.StatusCode); // réglementaire mais faite par un compte interne : facultatif
        Assert.Equal("observations", await ChampAsync(refusee));
    }

    private async Task<(string Responsable, string Technicien, string Pasteur)> ComptesAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        return (await JetonAsync("responsable@inventaire.test"), await JetonAsync("technicien@inventaire.test"), await JetonAsync("pasteur@inventaire.test"));
    }

    private async Task<(Guid Eglise, Guid Communes)> EgliseAsync(string technicien)
    {
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        return (eglise, (await LireAsync($"{Base}/structure", technicien)).EnumerateArray().Single().GetProperty("zones")[0].GetProperty("id").GetGuid());
    }

    private async Task<Guid> PlanAsync(string jeton, Guid batiment, string fondement, string libelle = "Vérification électrique")
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", jeton, new
        {
            commandeId = Guid.NewGuid(),
            cible = new { type = "Batiment", batimentId = batiment },
            nature = "VerificationExterieure",
            libelle,
            @base = fondement,
            periode = new { valeur = 1, unite = "Ans" },
            premiereEcheance = Aujourdhui,
        });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<DateOnly> EcheanceAsync(Guid plan, string jeton) =>
        DateOnly.Parse((await LireAsync($"{Preventif}/plans/{plan}", jeton)).GetProperty("plan").GetProperty("prochaine").GetProperty("echeance").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> RealiserAsync(Guid plan, string jeton, object demande, byte[]? rapport = null)
    {
        using var formulaire = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(demande)), "demande" } };
        if (rapport is not null)
        {
            var contenu = new ByteArrayContent(rapport);
            contenu.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            formulaire.Add(contenu, "rapport", "rapport.pdf");
        }

        return await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{plan}/perform", jeton, formulaire);
    }

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

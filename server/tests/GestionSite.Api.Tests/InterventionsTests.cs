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
    public async Task OnHoldSuspendsTheResolutionDeadlineUntilTheWorkResumes()
    {
        var (dossier, communes, responsable) = await DossierAsync();
        await QualifierAsync(dossier, communes, responsable, "Normale");
        var technicien = await JetonAsync("technicien@inventaire.test");
        var idTechnicien = await IdentifiantAsync("technicien@inventaire.test");
        using (await AffecterAsync(dossier, responsable, new { titre = "Pompe de la chaudière", intervenantCompteId = idTechnicien }))
        {
        }

        using (await AffecterAsync(dossier, responsable, new { titre = "Purge des radiateurs", intervenantCompteId = idTechnicien }))
        {
        }

        using (await DemarrerAsync(dossier, 1, technicien, responsable))
        {
        }

        using (await DemarrerAsync(dossier, 2, technicien, responsable))
        {
        }

        var reexamen = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using var sansDate = await AttendreAsync(dossier, 1, technicien, responsable, new { motif = "Livraison" });
        using var autreSansCommentaire = await AttendreAsync(dossier, 1, technicien, responsable, new { motif = "Autre", reexamenLe = reexamen });
        using var premiere = await AttendreAsync(dossier, 1, technicien, responsable, new { motif = "Livraison", reexamenLe = reexamen });
        var uneEnCours = await LireAsync(dossier, responsable);
        using var seconde = await AttendreAsync(dossier, 2, technicien, responsable, new { motif = "Prestataire", reexamenLe = reexamen });
        var enAttente = await LireAsync(dossier, responsable);
        using var reprise = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/resume", technicien, new { version = await VersionAsync(dossier, responsable) });
        var repris = await LireAsync(dossier, responsable);

        Assert.Equal("reexamenLe", await ChampAsync(sansDate));
        Assert.Equal("commentaire", await ChampAsync(autreSansCommentaire));
        Assert.Equal(HttpStatusCode.NoContent, premiere.StatusCode);
        Assert.Equal("EnCours", uneEnCours.GetProperty("statut").GetString()); // l'intervention 2 avance encore
        Assert.Equal(HttpStatusCode.NoContent, seconde.StatusCode);
        Assert.Equal("EnAttente", enAttente.GetProperty("statut").GetString());
        Assert.Equal(JsonValueKind.Null, enAttente.GetProperty("echeanceResolution").ValueKind); // délai suspendu (RG-07)
        Assert.Equal(reexamen, enAttente.GetProperty("reexamenLe").GetString()); // le plus proche des réexamens
        Assert.Equal("Livraison", enAttente.GetProperty("interventions")[0].GetProperty("motifAttente").GetString());
        Assert.Equal(HttpStatusCode.NoContent, reprise.StatusCode);
        Assert.Equal("EnCours", repris.GetProperty("statut").GetString());
        // La durée d'attente exacte s'ajoute au délai de 30 jours (Normale).
        var attendue = repris.GetProperty("qualifieLe").GetDateTimeOffset().AddDays(30).AddSeconds(repris.GetProperty("dureeAttenteSecondes").GetInt64());
        Assert.InRange(repris.GetProperty("echeanceResolution").GetDateTimeOffset() - attendue, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private async Task<HttpResponseMessage> AttendreAsync(Guid dossier, int numero, string jeton, string lecteur, object attente)
    {
        var corps = JsonSerializer.SerializeToNode(attente)!.AsObject();
        corps["version"] = await VersionAsync(dossier, lecteur);
        return await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/{numero}/hold", jeton, corps);
    }

    [Fact]
    public async Task ClosingTheLastInterventionClosesTheCaseAndARiskTradeNeedsAPhoto()
    {
        var (dossier, communes, responsable) = await DossierAsync();
        var technicien = await JetonAsync("technicien@inventaire.test");
        var baes = await CreerAsync($"{Base}/components", new { nom = "BAES sortie nord", corpsEtat = "EclairageSecurite", zoneId = communes }, technicien);
        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", responsable,
            new { priorite = "Urgente", zoneId = communes, composantId = baes, version = await VersionAsync(dossier, responsable) }))
        {
        }

        using (await AffecterAsync(dossier, responsable, new { titre = "Remplacer la batterie", intervenantCompteId = await IdentifiantAsync("technicien@inventaire.test") }))
        {
        }

        using var nonDemarree = await CloturerAsync(dossier, technicien, responsable, photo: false);
        using (await DemarrerAsync(dossier, 1, technicien, responsable))
        {
        }

        using var sansPhoto = await CloturerAsync(dossier, technicien, responsable, photo: false);
        var avant = await LireAsync(dossier, responsable);
        using var cloture = await CloturerAsync(dossier, technicien, responsable, photo: true);
        var lu = await LireAsync(dossier, responsable);
        using var photo = await EnvoyerAsync(HttpMethod.Get, $"{Dossiers}/{dossier}/interventions/1/photos/1", responsable);

        Assert.Equal("statut_incompatible", await CodeAsync(nonDemarree));
        Assert.True(avant.GetProperty("interventions")[0].GetProperty("photoExigee").GetBoolean());
        Assert.Equal("photos", await ChampAsync(sansPhoto));
        Assert.Equal(HttpStatusCode.NoContent, cloture.StatusCode);
        Assert.Equal("Cloture", lu.GetProperty("statut").GetString());
        Assert.Equal(JsonValueKind.String, lu.GetProperty("clotureLe").ValueKind);
        var intervention = lu.GetProperty("interventions")[0];
        Assert.Equal(("Cloturee", "Resolu", "Batterie remplacée, essai de 1 h"),
            (intervention.GetProperty("statut").GetString(), intervention.GetProperty("etatFinal").GetString(), intervention.GetProperty("actions").GetString()));
        Assert.Equal([1], intervention.GetProperty("photos").EnumerateArray().Select(r => r.GetInt32()));
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Cloture", lu.GetProperty("historique").EnumerateArray().Select(e => e.GetProperty("nature").GetString()));
    }

    [Fact]
    public async Task OnlyManagersReopenAClosedOrDismissedCaseWithAReason()
    {
        var (dossier, communes, responsable) = await DossierAsync();
        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dismiss", responsable, new { motif = "DejaTraite", version = await VersionAsync(dossier, responsable) }))
        {
        }

        using var parTechnicien = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/reopen", await JetonAsync("technicien@inventaire.test"),
            new { motif = "Revu sur place", version = await VersionAsync(dossier, responsable) });
        using var sansMotif = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/reopen", responsable, new { version = await VersionAsync(dossier, responsable) });
        using var rouvert = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/reopen", responsable,
            new { motif = "La fuite est revenue", version = await VersionAsync(dossier, responsable) });
        using var encore = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/reopen", responsable,
            new { motif = "Encore", version = await VersionAsync(dossier, responsable) });
        var lu = await LireAsync(dossier, responsable);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal("motif", await ChampAsync(sansMotif));
        Assert.Equal(HttpStatusCode.NoContent, rouvert.StatusCode);
        Assert.Equal("statut_incompatible", await CodeAsync(encore));
        Assert.Equal("AQualifier", lu.GetProperty("statut").GetString());
        Assert.Equal(JsonValueKind.Null, lu.GetProperty("motifClassement").ValueKind);
        Assert.Equal("Reouverture", lu.GetProperty("historique")[0].GetProperty("nature").GetString());
    }

    private async Task<HttpResponseMessage> CloturerAsync(Guid dossier, string jeton, string lecteur, bool photo)
    {
        var demande = JsonSerializer.Serialize(new { actions = "Batterie remplacée, essai de 1 h", etatFinal = "Resolu", version = await VersionAsync(dossier, lecteur) });
        using var formulaire = new MultipartFormDataContent { { new StringContent(demande), "demande" } };
        if (photo)
        {
            using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.OrangeRed, 64, 48) { Format = ImageMagick.MagickFormat.Jpeg };
            var contenu = new ByteArrayContent(image.ToByteArray());
            contenu.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formulaire.Add(contenu, "photos", "apres.jpg");
        }

        return await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/close", jeton, formulaire);
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

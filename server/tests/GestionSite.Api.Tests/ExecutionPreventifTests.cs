using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Api.Tests;

/// <summary>Exécution des occurrences (US-12, S12-03) : affectation, saut, inspection avec évaluation US-11, rejeu, droits.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class ExecutionPreventifTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private static readonly System.Globalization.CultureInfo Invariante = System.Globalization.CultureInfo.InvariantCulture;
    private static readonly string[] RobinetAccessible = ["Robinet accessible"];

    private string Preventif => Base.Replace("/inventory", "/preventive", StringComparison.Ordinal);

    private static DateOnly Aujourdhui => JourCivil.Du(DateTimeOffset.UtcNow);

    [Fact]
    public async Task TheAssignedTechnicianInspectsAComponentAndTheEvaluationIsRecordedOnce()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        await MembreAsync("autre@inventaire.test", Role.Technicien);
        var autre = await JetonAsync("autre@inventaire.test");
        var tableau = await TableauAsync(technicien);
        var plan = await PlanAsync(responsable, new { type = "Composant", composantId = tableau }, "InspectionInterne");
        var echeance = await EcheanceAsync(plan, pasteur);
        var idTechnicien = await CompteAsync("technicien@inventaire.test");

        using var parTechnicien = await AffecterAsync(plan, technicien, new { echeance, intervenantCompteId = idTechnicien, version = 0 });
        using var mauvaiseEcheance = await AffecterAsync(plan, responsable, new { echeance = echeance.AddDays(1), intervenantCompteId = idTechnicien, version = 0 });
        using var sansVersion = await AffecterAsync(plan, responsable, new { echeance, intervenantCompteId = idTechnicien });
        using var affecte = await AffecterAsync(plan, responsable, new { echeance, intervenantCompteId = idTechnicien, version = 0 });
        using var perime = await AffecterAsync(plan, responsable, new { echeance, intervenantCompteId = idTechnicien, version = 0 });
        var prochaine = (await LireAsync($"{Preventif}/plans/{plan}", pasteur)).GetProperty("plan").GetProperty("prochaine");
        var mine = await LireAsync($"{Preventif}/mine", technicien);
        var commande = Guid.NewGuid();
        object Inspection(string resultat) => new
        {
            commandeId = commande,
            echeance,
            realiseLe = Aujourdhui,
            resultat,
            grilleVersion = 1,
            points = new object[] { new { conforme = true, danger = false }, new { conforme = false, danger = true } },
            mesures = new decimal?[] { 41.5m },
            etat = "Mauvais",
            compteRendu = "Échauffement d'un bornier",
        };

        using var parPasteur = await RealiserAsync(plan, pasteur, Inspection("NonConforme"), photo: true);
        using var parAutre = await RealiserAsync(plan, autre, Inspection("NonConforme"), photo: true);
        using var sansPhoto = await RealiserAsync(plan, technicien, Inspection("NonConforme"));
        using var realise = await RealiserAsync(plan, technicien, Inspection("NonConforme"), photo: true);
        var resultat = await realise.Content.ReadFromJsonAsync<JsonElement>();
        using var rejoue = await RealiserAsync(plan, technicien, Inspection("NonConforme"), photo: true);
        using var reutilisee = await RealiserAsync(plan, technicien, Inspection("AvecObservations"), photo: true);
        var fiche = await LireAsync($"{Preventif}/plans/{plan}", pasteur);
        var controle = await LireAsync($"{Preventif}/controls/{resultat.GetProperty("id").GetGuid()}", pasteur);
        var historique = await LireAsync($"{Preventif}/controls?composantId={tableau}", pasteur);
        using var photo = await EnvoyerAsync(HttpMethod.Get, $"{Preventif}/controls/{resultat.GetProperty("id").GetGuid()}/photos/1", pasteur);
        using var absente = await EnvoyerAsync(HttpMethod.Get, $"{Preventif}/controls/{resultat.GetProperty("id").GetGuid()}/photos/2", pasteur);
        var evaluations = (await LireAsync($"{Base}/components/{tableau}/evaluations", pasteur)).EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal("echeance", await ChampAsync(mauvaiseEcheance));
        Assert.Equal("version", await ChampAsync(sansVersion));
        Assert.Equal(HttpStatusCode.NoContent, affecte.StatusCode);
        Assert.Equal("modifie_entre_temps", await CodeAsync(perime));
        Assert.Equal(("Affectee", idTechnicien), (prochaine.GetProperty("statut").GetString(), prochaine.GetProperty("intervenant").GetProperty("compteId").GetGuid()));
        Assert.Equal([plan], mine.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, parPasteur.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parAutre.StatusCode); // technicien non affecté
        Assert.Equal("photos", await ChampAsync(sansPhoto)); // « Mauvais » : photo exigée par l'évaluation US-11
        Assert.Equal(HttpStatusCode.OK, realise.StatusCode);
        Assert.Equal(("RealiseeAvecReserves", 3m, "Mauvais"), (resultat.GetProperty("statut").GetString(), resultat.GetProperty("note").GetDecimal(),
            resultat.GetProperty("libelle").GetString()));
        Assert.Equal(resultat.GetProperty("id").GetGuid(), (await rejoue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal("commande_reutilisee", await CodeAsync(reutilisee));
        Assert.Single(evaluations); // rejeu : ni second contrôle ni seconde évaluation
        Assert.Equal("Mauvais", evaluations[0].GetProperty("etat").GetString());
        Assert.Equal(resultat.GetProperty("evaluationId").GetGuid(), evaluations[0].GetProperty("id").GetGuid());
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM maintenance.controles_preventifs"));
        var suivante = fiche.GetProperty("plan").GetProperty("prochaine");
        Assert.Equal(Aujourdhui.AddYears(1), DateOnly.Parse(suivante.GetProperty("echeance").GetString()!, Invariante)); // glissant : réalisation + 1 an
        Assert.Equal(JsonValueKind.Null, suivante.GetProperty("statut").ValueKind);
        Assert.Equal(["RealiseeAvecReserves"], fiche.GetProperty("occurrences").EnumerateArray().Select(o => o.GetProperty("statut").GetString()));
        Assert.Equal(["Realisation", "Affectation", "Creation"], fiche.GetProperty("historique").EnumerateArray().Select(e => e.GetProperty("nature").GetString()));
        Assert.Equal(1, controle.GetProperty("grilleVersion").GetInt32());
        Assert.False(controle.GetProperty("points")[1].GetProperty("conforme").GetBoolean());
        Assert.True(controle.GetProperty("points")[1].GetProperty("danger").GetBoolean());
        Assert.Equal(5, controle.GetProperty("points").GetArrayLength()); // grille Courants forts : 5 points figés
        Assert.Equal([1], controle.GetProperty("photos").EnumerateArray().Select(p => p.GetInt32()));
        Assert.Equal(idTechnicien, controle.GetProperty("intervenantCompteId").GetGuid());
        Assert.Single(historique.EnumerateArray());
        Assert.Equal(1, historique[0].GetProperty("defauts").GetInt32());
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absente.StatusCode);
    }

    [Fact]
    public async Task AReplayAfterAPartialFailureReusesTheEvaluationAlreadyRecorded()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var tableau = await TableauAsync(technicien);
        var plan = await PlanAsync(responsable, new { type = "Composant", composantId = tableau }, "InspectionInterne");
        var echeance = await EcheanceAsync(plan, pasteur);
        var commande = Guid.NewGuid();
        // L'évaluation de l'inspection a été enregistrée (commande dérivée), puis l'enregistrement du contrôle a échoué.
        using var evaluation = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{tableau}/evaluations", responsable,
            new { commandeId = Commandes.Derivee(commande, "evaluation"), grilleVersion = 1, etat = "Bon" });

        using var realise = await RealiserAsync(plan, responsable, new
        {
            commandeId = commande,
            echeance,
            realiseLe = Aujourdhui.AddDays(-2),
            resultat = "Conforme",
            grilleVersion = 1,
            etat = "Bon",
        });

        Assert.Equal(HttpStatusCode.OK, evaluation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, realise.StatusCode); // R/D réalise une occurrence non affectée (saisie après coup)
        Assert.Equal("Realisee", (await realise.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("statut").GetString());
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM patrimoine.evaluations"));
        var suivante = (await LireAsync($"{Preventif}/plans/{plan}", pasteur)).GetProperty("plan").GetProperty("prochaine");
        Assert.Equal(Aujourdhui.AddDays(-2).AddYears(1), DateOnly.Parse(suivante.GetProperty("echeance").GetString()!, Invariante));
    }

    [Fact]
    public async Task ASkipWithAReasonMovesTheSlidingDueDateAndAMaintenanceNeedsAReport()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var tableau = await TableauAsync(technicien);
        var entretien = await PlanAsync(responsable, new { type = "Composant", composantId = tableau }, "Entretien", premiere: Aujourdhui.AddDays(-20));
        var echeance = await EcheanceAsync(entretien, pasteur);
        var idTechnicien = await CompteAsync("technicien@inventaire.test");

        using var parTechnicienNonAffecte = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{entretien}/skip", technicien, new { echeance, motif = "Inaccessible" });
        using var affecte = await AffecterAsync(entretien, responsable, new { echeance, intervenantCompteId = idTechnicien, version = 0 });
        using var sansCommentaire = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{entretien}/skip", technicien, new { echeance, motif = "Autre" });
        using var saute = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{entretien}/skip", technicien, new { echeance, motif = "Inaccessible" });
        using var rejoue = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{entretien}/skip", technicien, new { echeance, motif = "Inaccessible" });
        var fiche = await LireAsync($"{Preventif}/plans/{entretien}", pasteur);
        var suivante = DateOnly.Parse(fiche.GetProperty("plan").GetProperty("prochaine").GetProperty("echeance").GetString()!, Invariante);

        using var sansCompteRendu = await RealiserAsync(entretien, responsable, new { commandeId = Guid.NewGuid(), echeance = suivante, realiseLe = Aujourdhui, resultat = "Conforme" });
        using var avecGrille = await RealiserAsync(entretien, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance = suivante,
            realiseLe = Aujourdhui,
            resultat = "Conforme",
            compteRendu = "Serrage",
            grilleVersion = 1,
        });
        using var fait = await RealiserAsync(entretien, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance = suivante,
            realiseLe = Aujourdhui,
            resultat = "Conforme",
            compteRendu = "Serrage des bornes",
            releves = new[] { new { libelle = "Isolement", unite = "MΩ", valeur = 250m } },
            prestataire = "Électricité Martin",
        });
        var controle = await LireAsync($"{Preventif}/controls/{(await fait.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()}", pasteur);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicienNonAffecte.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, affecte.StatusCode);
        Assert.Equal("commentaire", await ChampAsync(sansCommentaire));
        Assert.Equal(HttpStatusCode.NoContent, saute.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, rejoue.StatusCode);
        Assert.Equal(Aujourdhui.AddYears(1), suivante); // glissant : repart de la date du saut
        Assert.Equal(("Sautee", "Inaccessible"), (fiche.GetProperty("occurrences")[0].GetProperty("statut").GetString(),
            fiche.GetProperty("occurrences")[0].GetProperty("motifSaut").GetString()));
        Assert.Equal("compteRendu", await ChampAsync(sansCompteRendu));
        Assert.Equal("points", await ChampAsync(avecGrille));
        Assert.Equal(HttpStatusCode.OK, fait.StatusCode);
        Assert.Equal(("Électricité Martin", "Isolement"), (controle.GetProperty("prestataire").GetString(), controle.GetProperty("mesures")[0].GetProperty("libelle").GetString()));
        Assert.Equal(JsonValueKind.Null, controle.GetProperty("intervenantCompteId").ValueKind);
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM patrimoine.evaluations")); // un entretien n'évalue pas
    }

    [Fact]
    public async Task AZoneInspectionUsesTheGridInForceAndSuspendedPlansCannotBeExecuted()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var tableau = await TableauAsync(technicien);
        var communes = (await LireAsync($"{Base}/structure", technicien)).EnumerateArray().First().GetProperty("zones")[0].GetProperty("id").GetGuid();
        var zone = await PlanAsync(responsable, new { type = "ZoneCorpsEtat", zoneId = communes, corpsEtat = "Gaz" }, "InspectionInterne");
        var echeance = await EcheanceAsync(zone, pasteur);
        using var adaptation = await EnvoyerAsync(HttpMethod.Put, $"{Base}/grids/Gaz", responsable, new { criteres = RobinetAccessible, version = 1 });

        using var perimee = await RealiserAsync(zone, responsable, new { commandeId = Guid.NewGuid(), echeance, realiseLe = Aujourdhui, resultat = "Conforme", grilleVersion = 1 });
        using var faite = await RealiserAsync(zone, responsable, new
        {
            commandeId = Guid.NewGuid(),
            echeance,
            realiseLe = Aujourdhui,
            resultat = "Conforme",
            grilleVersion = 2,
            points = new[] { new { conforme = true, danger = false } },
        });
        var composant = await PlanAsync(responsable, new { type = "Composant", composantId = tableau }, "InspectionInterne");
        using var retrait = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{tableau}/withdraw", technicien,
            new { motif = "Remplacé", version = (await LireAsync($"{Base}/components/{tableau}", technicien)).GetProperty("composant").GetProperty("version").GetUInt32() });
        using var suspendu = await RealiserAsync(composant, responsable, new { commandeId = Guid.NewGuid(), echeance = Aujourdhui, realiseLe = Aujourdhui, resultat = "Conforme", grilleVersion = 1 });
        using var inconnu = await RealiserAsync(Guid.NewGuid(), responsable, new { commandeId = Guid.NewGuid() });
        using var autreSite = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Organisation}/sites/{Guid.NewGuid()}/preventive/controls", pasteur);

        Assert.Equal(HttpStatusCode.OK, adaptation.StatusCode);
        Assert.Equal("grille_modifiee", await CodeAsync(perimee));
        Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        Assert.Equal(0, await Database.CountAsync("SELECT count(*) FROM patrimoine.evaluations")); // zone : pas d'évaluation de composant
        Assert.Equal(HttpStatusCode.NoContent, retrait.StatusCode);
        Assert.Equal("statut_incompatible", await CodeAsync(suspendu));
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, autreSite.StatusCode);
    }

    private async Task<(string Responsable, string Technicien, string Pasteur)> ComptesAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        return (await JetonAsync("responsable@inventaire.test"), await JetonAsync("technicien@inventaire.test"), await JetonAsync("pasteur@inventaire.test"));
    }

    private async Task<Guid> TableauAsync(string technicien)
    {
        await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var communes = (await LireAsync($"{Base}/structure", technicien)).EnumerateArray().First().GetProperty("zones")[0].GetProperty("id").GetGuid();
        return await CreerAsync($"{Base}/components", new { nom = "Tableau général", corpsEtat = "CourantsForts", zoneId = communes }, technicien);
    }

    private async Task<Guid> PlanAsync(string jeton, object cible, string nature, DateOnly? premiere = null)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", jeton, new
        {
            commandeId = Guid.NewGuid(),
            cible,
            nature,
            libelle = "Contrôle",
            @base = "BonnePratique",
            periode = new { valeur = 1, unite = "Ans" },
            premiereEcheance = premiere ?? Aujourdhui,
        });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<DateOnly> EcheanceAsync(Guid plan, string jeton) =>
        DateOnly.Parse((await LireAsync($"{Preventif}/plans/{plan}", jeton)).GetProperty("plan").GetProperty("prochaine").GetProperty("echeance").GetString()!, Invariante);

    private async Task<Guid> CompteAsync(string email) => (await LireAsync("/me", await JetonAsync(email))).GetProperty("compteId").GetGuid();

    private Task<HttpResponseMessage> AffecterAsync(Guid plan, string jeton, object demande) =>
        EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{plan}/assign", jeton, demande);

    private async Task<HttpResponseMessage> RealiserAsync(Guid plan, string jeton, object demande, bool photo = false)
    {
        using var formulaire = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(demande)), "demande" } };
        if (photo)
        {
            using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 64, 48) { Format = ImageMagick.MagickFormat.Jpeg };
            var contenu = new ByteArrayContent(image.ToByteArray());
            contenu.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            formulaire.Add(contenu, "photos", "constat.jpg");
        }

        return await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{plan}/perform", jeton, formulaire);
    }

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

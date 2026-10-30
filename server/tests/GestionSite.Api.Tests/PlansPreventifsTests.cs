using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Api.Tests;

/// <summary>Plans préventifs et échéances calculées (US-12, S12-02) sur l'API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class PlansPreventifsTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Preventif => Base.Replace("/inventory", "/preventive", StringComparison.Ordinal);

    private static DateOnly Aujourdhui => JourCivil.Du(DateTimeOffset.UtcNow);

    [Fact]
    public async Task AConfirmedProposalBecomesOnePlanEvenWhenReplayed()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var (_, tableau) = await EgliseAsync(technicien);
        var cle = $"electricite-verification:composant:{tableau:N}";
        var commande = Guid.NewGuid();

        using var parTechnicien = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/confirm", technicien, new { commandeId = commande, cle });
        using var inconnue = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/confirm", responsable, new { commandeId = commande, cle = "x:y:z" });
        var plan = await CreeAsync($"{Preventif}/proposals/confirm", responsable, new { commandeId = commande, cle });
        var rejoue = await CreeAsync($"{Preventif}/proposals/confirm", responsable, new { commandeId = commande, cle });
        using var autreCommande = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/confirm", responsable, new { commandeId = Guid.NewGuid(), cle });
        using var ecartApres = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/dismiss", responsable, new { cle, motif = "NonConcerne" });
        var propositions = await LireAsync($"{Preventif}/proposals", pasteur);
        var fiche = await LireAsync($"{Preventif}/plans/{plan}", pasteur);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal("cle", await ChampAsync(inconnue));
        Assert.Equal(plan, rejoue);
        Assert.Equal("deja_planifiee", await CodeAsync(autreCommande));
        Assert.Equal("deja_planifiee", await CodeAsync(ecartApres));
        Assert.DoesNotContain(propositions.EnumerateArray(), p => p.GetProperty("cle").GetString() == cle);
        Assert.Contains(propositions.EnumerateArray(), p => p.GetProperty("modele").GetString() == "inspection-interne");
        var lu = fiche.GetProperty("plan");
        Assert.Equal(("electricite-verification", "TechnicienCompetent", "Reglementaire"),
            (lu.GetProperty("modele").GetString(), lu.GetProperty("typeIntervenant").GetString(), lu.GetProperty("base").GetString()));
        Assert.EndsWith("à confirmer par un bureau de contrôle ou le SDIS", lu.GetProperty("reference").GetString(), StringComparison.Ordinal);
        Assert.Equal(Aujourdhui, DateOnly.Parse(lu.GetProperty("prochaine").GetProperty("echeance").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("AFaire", lu.GetProperty("prochaine").GetProperty("etat").GetString());
        Assert.Equal(tableau, lu.GetProperty("cible").GetProperty("composantId").GetGuid());
        Assert.Equal(["Creation"], fiche.GetProperty("historique").EnumerateArray().Select(e => e.GetProperty("nature").GetString()));

        // Réponse perdue puis composant retiré : le même envoi rejoué rend toujours le plan (reconnu avant toute revalidation).
        using var retrait = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{tableau}/withdraw", technicien,
            new { motif = "Remplacé", version = (await LireAsync($"{Base}/components/{tableau}", technicien)).GetProperty("composant").GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.NoContent, retrait.StatusCode);
        Assert.Equal(plan, await CreeAsync($"{Preventif}/proposals/confirm", responsable, new { commandeId = commande, cle }));
        using var autreCle = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/confirm", responsable, new { commandeId = commande, cle = "autre:cle" });
        Assert.Equal("commande_reutilisee", await CodeAsync(autreCle));
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM maintenance.plans_preventifs"));
    }

    [Fact]
    public async Task ADismissedProposalStaysVisibleWithItsReasonAndCanStillBeConfirmed()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var (eglise, _) = await EgliseAsync(technicien);
        var cle = $"cheneaux-nettoyage:batiment:{eglise:N}";

        using var sansCommentaire = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/dismiss", responsable, new { cle, motif = "Autre" });
        using var ecartee = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/dismiss", responsable, new { cle, motif = "SuiviAilleurs", commentaire = "Contrat du couvreur" });
        using var deuxFois = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/proposals/dismiss", responsable, new { cle, motif = "NonConcerne" });
        var lue = (await LireAsync($"{Preventif}/proposals", pasteur)).EnumerateArray().Single(p => p.GetProperty("cle").GetString() == cle);
        var plan = await CreeAsync($"{Preventif}/proposals/confirm", responsable, new { commandeId = Guid.NewGuid(), cle, donneurOrdre = "Commune" });
        var fiche = (await LireAsync($"{Preventif}/plans/{plan}", pasteur)).GetProperty("plan");

        Assert.Equal("commentaire", await ChampAsync(sansCommentaire));
        Assert.Equal(HttpStatusCode.NoContent, ecartee.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deuxFois.StatusCode);
        Assert.Equal("Ecartee", lue.GetProperty("statut").GetString());
        Assert.Equal(("SuiviAilleurs", "Contrat du couvreur"), (lue.GetProperty("ecart").GetProperty("motif").GetString(), lue.GetProperty("ecart").GetProperty("commentaire").GetString()));
        Assert.Equal(("Fixe", "Commune", "Batiment"), (fiche.GetProperty("mode").GetString(), fiche.GetProperty("donneurOrdre").GetString(),
            fiche.GetProperty("cible").GetProperty("type").GetString()));
        Assert.Equal(10, DateOnly.Parse(fiche.GetProperty("prochaine").GetProperty("echeance").GetString()!, System.Globalization.CultureInfo.InvariantCulture).Month);
        Assert.Equal(1, await Database.CountAsync("SELECT count(*) FROM maintenance.propositions_ecartees"));
    }

    [Fact]
    public async Task AManualPlanIsValidatedAgainstTheInventoryAndTheSiteMembers()
    {
        var (responsable, technicien, _) = await ComptesAsync();
        var (eglise, tableau) = await EgliseAsync(technicien);
        var communes = await CommunesAsync(technicien);
        var bancs = await CreerAsync($"{Base}/components", new { nom = "Bancs", corpsEtat = "Mobilier", zoneId = communes }, technicien);
        var idTechnicien = await CompteAsync("technicien@inventaire.test");
        var idPasteur = await CompteAsync("pasteur@inventaire.test");
        var commande = Guid.NewGuid();

        using var inspectionBatiment = await PlanAsync(responsable, new { type = "Batiment", batimentId = eglise }, "InspectionInterne");
        using var nonSoumis = await PlanAsync(responsable, new { type = "Composant", composantId = bancs }, "Entretien");
        using var corpsInconnu = await PlanAsync(responsable, new { type = "ZoneCorpsEtat", zoneId = communes, corpsEtat = "Electricite" }, "InspectionInterne");
        using var zoneInconnue = await PlanAsync(responsable, new { type = "ZoneCorpsEtat", zoneId = Guid.NewGuid(), corpsEtat = "Gaz" }, "InspectionInterne");
        using var batimentInconnu = await PlanAsync(responsable, new { type = "Batiment", batimentId = Guid.NewGuid() }, "Entretien");
        using var parPasteur = await PlanAsync(responsable, new { type = "Composant", composantId = tableau }, "Entretien", intervenant: idPasteur);
        using var sansReferent = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", responsable,
            Demande(new { type = "Composant", composantId = tableau }, "Entretien", prestataire: "Électricité Martin"));
        using var cree = await PlanAsync(responsable, new { type = "ZoneCorpsEtat", zoneId = communes, corpsEtat = "Gaz" }, "InspectionInterne", intervenant: idTechnicien, commande: commande);
        using var rejoue = await PlanAsync(responsable, new { type = "ZoneCorpsEtat", zoneId = communes, corpsEtat = "Gaz" }, "InspectionInterne", intervenant: idTechnicien, commande: commande);
        using var reutilisee = await PlanAsync(responsable, new { type = "Batiment", batimentId = eglise }, "Entretien", commande: commande);
        var plan = (await cree.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var lu = (await LireAsync($"{Preventif}/plans/{plan}", technicien)).GetProperty("plan");

        Assert.Equal("nature", await ChampAsync(inspectionBatiment));
        Assert.Equal("cible.composantId", await ChampAsync(nonSoumis));
        Assert.Equal("cible.corpsEtat", await ChampAsync(corpsInconnu));
        Assert.Equal("cible.zoneId", await ChampAsync(zoneInconnue));
        Assert.Equal("cible.batimentId", await ChampAsync(batimentInconnu));
        Assert.Equal("intervenantCompteId", await ChampAsync(parPasteur));
        Assert.Equal("referentCompteId", await ChampAsync(sansReferent));
        Assert.Equal(plan, (await rejoue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal("commande_reutilisee", await CodeAsync(reutilisee));
        Assert.Equal("Église · Parties communes — Gaz", lu.GetProperty("cible").GetProperty("libelle").GetString());
        Assert.Equal(idTechnicien, lu.GetProperty("intervenant").GetProperty("compteId").GetGuid());
        Assert.Equal(eglise, lu.GetProperty("cible").GetProperty("batimentId").GetGuid());
    }

    [Fact]
    public async Task ACalendarChangeIsTracedAndSuspensionsRemoveThePlanFromTheSchedule()
    {
        var (responsable, technicien, pasteur) = await ComptesAsync();
        var (eglise, tableau) = await EgliseAsync(technicien);
        var retard = await CreeAsync($"{Preventif}/plans", responsable, Demande(new { type = "Batiment", batimentId = eglise }, "Entretien", premiere: Aujourdhui.AddDays(-3), libelle: "Registre"));
        var bientot = await CreeAsync($"{Preventif}/plans", responsable, Demande(new { type = "Composant", composantId = tableau }, "Entretien", premiere: Aujourdhui.AddDays(5), libelle: "Serrage"));
        var loin = await CreeAsync($"{Preventif}/plans", responsable, Demande(new { type = "Batiment", batimentId = eglise }, "Entretien", premiere: Aujourdhui.AddDays(200), libelle: "Toiture"));
        var echeancier = (await LireAsync($"{Preventif}/schedule", pasteur)).EnumerateArray().ToList();
        var version = (await LireAsync($"{Preventif}/plans/{loin}", responsable)).GetProperty("plan").GetProperty("version").GetUInt32();

        using var parTechnicien = await EnvoyerAsync(HttpMethod.Put, $"{Preventif}/plans/{loin}", technicien, Demande(null, null, premiere: Aujourdhui.AddDays(10), version: version));
        using var modifie = await EnvoyerAsync(HttpMethod.Put, $"{Preventif}/plans/{loin}", responsable,
            Demande(null, null, premiere: Aujourdhui.AddDays(10), libelle: "Toiture", version: version, valeur: 6, unite: "Mois"));
        using var perime = await EnvoyerAsync(HttpMethod.Put, $"{Preventif}/plans/{loin}", responsable, Demande(null, null, premiere: Aujourdhui.AddDays(20), version: version));
        var fiche = await LireAsync($"{Preventif}/plans/{loin}", responsable);
        var nouvelle = fiche.GetProperty("plan").GetProperty("version").GetUInt32();
        using var suspendu = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{loin}/suspend", responsable, new { motif = "Échafaudage", version = nouvelle });
        var apresSuspension = (await LireAsync($"{Preventif}/plans/{loin}", responsable)).GetProperty("plan");
        using var deuxFois = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{loin}/suspend", responsable,
            new { motif = "Encore", version = apresSuspension.GetProperty("version").GetUInt32() });
        using var repris = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{loin}/resume", responsable,
            new { version = apresSuspension.GetProperty("version").GetUInt32() });
        using var retrait = await EnvoyerAsync(HttpMethod.Post, $"{Base}/components/{tableau}/withdraw", technicien,
            new { motif = "Remplacé", version = (await LireAsync($"{Base}/components/{tableau}", technicien)).GetProperty("composant").GetProperty("version").GetUInt32() });
        var automatique = (await LireAsync($"{Preventif}/plans/{bientot}", pasteur)).GetProperty("plan");
        var apres = (await LireAsync($"{Preventif}/schedule", pasteur)).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        using var autreOrganisation = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{Guid.NewGuid()}/sites/{Site}/preventive/plans/{loin}", pasteur);
        using var inconnu = await EnvoyerAsync(HttpMethod.Get, $"{Preventif}/plans/{Guid.NewGuid()}", pasteur);

        Assert.Equal([retard, bientot, loin], echeancier.Select(p => p.GetProperty("id").GetGuid()));
        Assert.Equal(["EnRetard", "AFaire", "AVenir"], echeancier.Select(p => p.GetProperty("prochaine").GetProperty("etat").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, modifie.StatusCode);
        Assert.Equal("modifie_entre_temps", await CodeAsync(perime));
        var modification = fiche.GetProperty("historique")[0];
        Assert.Equal("Modification", modification.GetProperty("nature").GetString());
        Assert.Equal(1, modification.GetProperty("detail").GetProperty("avant").GetProperty("periode").GetInt32());
        Assert.Equal(6, modification.GetProperty("detail").GetProperty("apres").GetProperty("periode").GetInt32());
        Assert.Equal("Ans", modification.GetProperty("detail").GetProperty("avant").GetProperty("unite").GetString());
        Assert.Equal(HttpStatusCode.NoContent, suspendu.StatusCode);
        Assert.True(apresSuspension.GetProperty("suspendu").GetBoolean());
        Assert.Equal(JsonValueKind.Null, apresSuspension.GetProperty("prochaine").ValueKind);
        Assert.Equal("statut_incompatible", await CodeAsync(deuxFois));
        Assert.Equal(HttpStatusCode.NoContent, repris.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retrait.StatusCode);
        Assert.True(automatique.GetProperty("suspensionAutomatique").GetBoolean());
        Assert.Equal("Composant retiré de l'inventaire", automatique.GetProperty("motifSuspension").GetString());
        Assert.Equal([retard, loin], apres);
        Assert.Equal(HttpStatusCode.NotFound, autreOrganisation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inconnu.StatusCode);
    }

    private async Task<(string Responsable, string Technicien, string Pasteur)> ComptesAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        return (await JetonAsync("responsable@inventaire.test"), await JetonAsync("technicien@inventaire.test"), await JetonAsync("pasteur@inventaire.test"));
    }

    // Église non classée, tableau électrique soumis à vérification dans ses parties communes.
    private async Task<(Guid Eglise, Guid Tableau)> EgliseAsync(string technicien)
    {
        var eglise = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, technicien);
        var tableau = await CreerAsync($"{Base}/components", new { nom = "Tableau général", corpsEtat = "CourantsForts", zoneId = await CommunesAsync(technicien) }, technicien);
        await MembreAsync("delegue@inventaire.test", Role.Delegue);
        var delegue = await JetonAsync("delegue@inventaire.test");
        using var salaries = await EnvoyerAsync(HttpMethod.Put, $"{Base}/establishment", delegue, new { salariesPresents = true, version = 0 });
        Assert.Equal(HttpStatusCode.NoContent, salaries.StatusCode); // électricité du lieu de travail proposée
        return (eglise, tableau);
    }

    private async Task<Guid> CommunesAsync(string jeton) =>
        (await LireAsync($"{Base}/structure", jeton)).EnumerateArray().First().GetProperty("zones")[0].GetProperty("id").GetGuid();

    private async Task<Guid> CompteAsync(string email) =>
        (await LireAsync("/me", await JetonAsync(email))).GetProperty("compteId").GetGuid();

    private Task<HttpResponseMessage> PlanAsync(string jeton, object cible, string nature, Guid? intervenant = null, Guid? commande = null) =>
        EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", jeton, Demande(cible, nature, intervenant: intervenant, commande: commande));

    private static object Demande(
        object? cible, string? nature, DateOnly? premiere = null, string libelle = "Contrôle", Guid? intervenant = null, string? prestataire = null,
        Guid? commande = null, uint? version = null, int valeur = 1, string unite = "Ans") => new
        {
            commandeId = commande ?? Guid.NewGuid(),
            cible,
            nature,
            libelle,
            @base = "BonnePratique",
            intervenantCompteId = intervenant,
            prestataire,
            periode = new { valeur, unite },
            premiereEcheance = premiere ?? JourCivil.Du(DateTimeOffset.UtcNow),
            version,
        };

    private async Task<Guid> CreeAsync(string chemin, string jeton, object corps)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, chemin, jeton, corps);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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

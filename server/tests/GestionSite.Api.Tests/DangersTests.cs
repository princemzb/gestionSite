using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Maintenance.Planificateur;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Suivi d'un danger (US-10, S10-04) sur l'API et PostgreSQL réels : prise en compte unique, mise en sécurité déclarée dès
/// l'alerte (Q-M1), vérifiée ou refusée (Q-M2), levée et écartement motivés (Q-M4), journal de chaque étape, droits par rôle,
/// attente refusée sous danger non maîtrisé (Q6-A), rappels à horloge contrôlée (Q4-A) sans doublon au rejeu, alertes
/// épinglées (Q-M8), consigne du site (Q-M6), isolation des organisations.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class DangersTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private string Dossiers => Base.Replace("/inventory", "/cases", StringComparison.Ordinal);

    private string Dangers => Base.Replace("/inventory", "/dangers", StringComparison.Ordinal);

    private string Centre => $"/organizations/{Organisation}/me/notifications";

    [Fact]
    public async Task ADangerIsTakenIntoAccountOnceThenSecuredVerifiedAndLiftedWithEveryStepInTheJournal()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Pasteur, zone, danger: true);
        var danger = await DangerIdAsync(equipe.Coordination);

        // Q-M1 : mise en sécurité déclarée par un technicien dès l'alerte ouverte ; notifiée aux R/D (personne ne l'a prise en compte).
        using var declaree = await AgirAsync(dossier, danger, "securing", equipe.Technicien, new { action = "AccesCondamne", commentaire = "Porte de la sacristie fermée à clé" });
        Assert.Equal(HttpStatusCode.NoContent, declaree.StatusCode);
        Assert.Equal("Ouvert", (await DangerAsync(equipe.Responsable)).GetProperty("etat").GetString()); // toujours à prendre en compte
        Assert.Contains("MiseEnSecuriteDeclaree", await TypesAsync(equipe.Delegue));
        using var verifieeAvant = await AgirAsync(dossier, danger, "securing/verify", equipe.Responsable, new { suffisante = true });
        Assert.Equal("non_pris_en_compte", await CodeAsync(verifieeAvant));

        // Deux prises en compte simultanées à la même version : une seule retenue.
        var version = (await DangerAsync(equipe.Responsable)).GetProperty("version").GetUInt32();
        var simultanees = await Task.WhenAll(
            EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dangers/{danger}/acknowledge", equipe.Responsable, new { version }),
            EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dangers/{danger}/acknowledge", equipe.Delegue, new { version }));
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], simultanees.Select(r => r.StatusCode).Order());
        foreach (var reponse in simultanees)
        {
            reponse.Dispose();
        }

        using var parLePasteur = await AgirAsync(dossier, danger, "securing/verify", equipe.Pasteur, new { suffisante = true });
        using var sansMotif = await AgirAsync(dossier, danger, "securing/verify", equipe.Delegue, new { suffisante = false });
        using var insuffisante = await AgirAsync(dossier, danger, "securing/verify", equipe.Delegue, new { suffisante = false, motif = "La porte ne ferme pas." });
        Assert.Equal(HttpStatusCode.Forbidden, parLePasteur.StatusCode); // Q5-A : le pasteur déclare, il ne vérifie pas
        Assert.Equal("motif", await ChampAsync(sansMotif));
        Assert.Equal(HttpStatusCode.NoContent, insuffisante.StatusCode);
        var pris = await DangerAsync(equipe.Responsable);
        Assert.Equal(("PrisEnCompte", JsonValueKind.Null), (pris.GetProperty("etat").GetString(), pris.GetProperty("miseEnSecurite").ValueKind));
        Assert.Equal(["MiseEnSecuriteInsuffisante"], await TypesAsync(equipe.Technicien)); // Q-M2 : le déclarant est prévenu

        // Nouvelle déclaration par le pasteur : notifiée au seul R/D qui suit le danger (Q-M3), puis vérifiée suffisante.
        var preneur = pris.GetProperty("prisEnCompteParCompteId").GetGuid();
        var (suiveur, autre) = preneur == await CompteAsync(equipe.Responsable) ? (equipe.Responsable, equipe.Delegue) : (equipe.Delegue, equipe.Responsable);
        var avant = (await TypesAsync(autre)).Count;
        using var sansCommentaire = await AgirAsync(dossier, danger, "securing", equipe.Pasteur, new { action = "Autre" });
        using var energie = await AgirAsync(dossier, danger, "securing", equipe.Pasteur, new { action = "EnergieCoupee", commentaire = "Disjoncteur général coupé" });
        using var deuxFois = await AgirAsync(dossier, danger, "securing", equipe.Technicien, new { action = "Evacuation" });
        Assert.Equal("commentaire", await ChampAsync(sansCommentaire));
        Assert.Equal(HttpStatusCode.NoContent, energie.StatusCode);
        Assert.Equal("securite_deja_declaree", await CodeAsync(deuxFois));
        Assert.Equal(2, (await TypesAsync(suiveur)).Count(t => t == "MiseEnSecuriteDeclaree"));
        Assert.Equal(avant, (await TypesAsync(autre)).Count);
        using var verifiee = await AgirAsync(dossier, danger, "securing/verify", suiveur, new { suffisante = true });
        Assert.Equal(HttpStatusCode.NoContent, verifiee.StatusCode);
        Assert.Equal("MiseEnSecuriteVerifiee", (await DangerAsync(equipe.Technicien)).GetProperty("etat").GetString());
        Assert.Equal("MiseEnSecuriteVerifiee", (await LireAsync(Dossiers, equipe.Technicien)).EnumerateArray().Single().GetProperty("etatDanger").GetString());

        // Levée : motif vérifié (dossier non clôturé « Résolu »), « Autre » commenté, technicien refusé.
        using var nonResolu = await AgirAsync(dossier, danger, "lift", suiveur, new { motif = "ReparationDossierResolu" });
        using var autreSansCommentaire = await AgirAsync(dossier, danger, "lift", suiveur, new { motif = "Autre" });
        using var parLeTechnicien = await AgirAsync(dossier, danger, "lift", equipe.Technicien, new { motif = "ReparationConstatee" });
        using var levee = await AgirAsync(dossier, danger, "lift", suiveur, new { motif = "ReparationConstatee", commentaire = "Câble remplacé, constaté sur place" });
        using var deuxiemeLevee = await AgirAsync(dossier, danger, "lift", suiveur, new { motif = "ReparationConstatee" });
        Assert.Equal("dossier_non_resolu", await CodeAsync(nonResolu));
        Assert.Equal("commentaire", await ChampAsync(autreSansCommentaire));
        Assert.Equal(HttpStatusCode.Forbidden, parLeTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, levee.StatusCode);
        Assert.Equal("danger_clos", await CodeAsync(deuxiemeLevee));
        Assert.Empty((await LireAsync(Dangers, equipe.Technicien)).EnumerateArray());

        var fiche = await LireAsync($"{Dossiers}/{dossier}", equipe.Pasteur);
        var suivi = fiche.GetProperty("dangers").EnumerateArray().Single();
        Assert.Equal(("Leve", "ReparationConstatee", "Câble remplacé, constaté sur place"),
            (suivi.GetProperty("etat").GetString(), suivi.GetProperty("fin").GetProperty("motif").GetString(), suivi.GetProperty("fin").GetProperty("commentaire").GetString()));
        Assert.Equal(("EnergieCoupee", "Disjoncteur général coupé"),
            (suivi.GetProperty("miseEnSecurite").GetProperty("action").GetString(), suivi.GetProperty("miseEnSecurite").GetProperty("commentaire").GetString()));
        var journal = fiche.GetProperty("historique").EnumerateArray().Reverse()
            .Where(e => e.GetProperty("nature").GetString() is not "Declaration").ToList();
        Assert.Equal(
            ["MiseEnSecuriteDeclaree", "DangerPrisEnCompte", "MiseEnSecuriteInsuffisante", "MiseEnSecuriteDeclaree", "MiseEnSecuriteVerifiee", "DangerLeve"],
            journal.Select(e => e.GetProperty("nature").GetString()));
        Assert.All(journal, e => Assert.NotEqual(JsonValueKind.Null, e.GetProperty("auteurCompteId").ValueKind));
        Assert.Equal("La porte ne ferme pas.", journal[2].GetProperty("detail").GetProperty("motif").GetString());
        Assert.Equal(danger, journal[5].GetProperty("detail").GetProperty("alerte").GetGuid());
    }

    [Fact]
    public async Task AnUnacknowledgedDangerRealertsEveryoneAfter15MinutesThenHourlyAndTheOwnerIsRemindedHourlyUntilSecured()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Technicien, zone, danger: true);
        var cree = (await DangerAsync(equipe.Responsable)).GetProperty("creeLe").GetDateTimeOffset();

        var avant = await PasserAsync(cree.AddMinutes(14));
        var a15 = await PasserAsync(cree.AddMinutes(15));
        var rejeu = await PasserAsync(cree.AddMinutes(20));
        var heureSuivante = await PasserAsync(cree.AddMinutes(75));

        Assert.Equal((0, cree.AddMinutes(15)), (avant.Notifications, avant.ProchainPassage));
        Assert.Equal((4, cree.AddMinutes(75)), (a15.Notifications, a15.ProchainPassage)); // R, D, P, C (le déclarant aussi, s'il en était)
        Assert.Equal(0, rejeu.Notifications);
        Assert.Equal(4, heureSuivante.Notifications);
        Assert.Equal(["DangerNonPrisEnCompte", "DangerNonPrisEnCompte", "DangerSignale"], await TypesAsync(equipe.Coordination));
        Assert.Empty(await TypesAsync(equipe.Technicien)); // sans intervention, le technicien n'est pas destinataire

        // Q-M8 : toutes lues, la dernière alerte reste épinglée tant que le danger n'est pas pris en compte.
        using (await EnvoyerAsync(HttpMethod.Post, $"{Centre}/read", equipe.Delegue))
        {
        }

        var centre = await LireAsync(Centre, equipe.Delegue);
        Assert.Equal(0, centre.GetProperty("nonLues").GetInt32());
        var epinglee = centre.GetProperty("epinglees").EnumerateArray().Single();
        Assert.Equal(("DangerNonPrisEnCompte", true), (epinglee.GetProperty("type").GetString(), epinglee.GetProperty("epinglee").GetBoolean()));
        Assert.All(centre.GetProperty("notifications").EnumerateArray(), n => Assert.True(n.GetProperty("epinglee").GetBoolean()));

        using (var prise = await AgirAsync(dossier, await DangerIdAsync(equipe.Responsable), "acknowledge", equipe.Responsable, new { }))
        {
            Assert.Equal(HttpStatusCode.NoContent, prise.StatusCode);
        }

        var pec = (await DangerAsync(equipe.Responsable)).GetProperty("prisEnCompteLe").GetDateTimeOffset();
        Assert.Empty((await LireAsync(Centre, equipe.Delegue)).GetProperty("epinglees").EnumerateArray());
        var h1 = await PasserAsync(pec.AddHours(1));
        var h2 = await PasserAsync(pec.AddHours(2).AddMinutes(30));
        var h4 = await PasserAsync(pec.AddHours(4));
        var rejeuH4 = await PasserAsync(pec.AddHours(4).AddMinutes(59));

        Assert.Equal((1, pec.AddHours(2)), (h1.Notifications, h1.ProchainPassage)); // celui qui l'a pris en compte, seul
        Assert.Equal(1, h2.Notifications);
        Assert.Equal(2, h4.Notifications); // au bout de 4 h, les autres R/D aussi
        Assert.Equal(0, rejeuH4.Notifications);
        Assert.Equal(["MiseEnSecuriteAFaire"], await TypesAsync(equipe.Delegue, "MiseEnSecurite"));
        Assert.Equal(3, (await TypesAsync(equipe.Responsable)).Count(t => t == "MiseEnSecuriteAFaire"));
        Assert.DoesNotContain("MiseEnSecuriteAFaire", await TypesAsync(equipe.Pasteur));

        using (await AgirAsync(dossier, await DangerIdAsync(equipe.Responsable), "securing", equipe.Technicien, new { action = "PublicTenuADistance" }))
        {
        }

        var apresDeclaration = await PasserAsync(pec.AddHours(6));
        Assert.Equal(0, apresDeclaration.Notifications); // déclarée : plus de rappel (la vérification n'est pas rappelée)
    }

    [Fact]
    public async Task HoldIsRefusedUntilSecuringIsDeclaredAndAClosedCaseKeepsItsDangerInProgressUntilLifted()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Pasteur, zone, danger: true);
        var danger = await DangerIdAsync(equipe.Responsable);
        await QualifierAsync(dossier, zone, equipe.Responsable);
        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", equipe.Responsable,
            new { titre = "Remplacer le câble", intervenantCompteId = await CompteAsync(equipe.Technicien), version = await VersionAsync(dossier, equipe.Responsable) }))
        {
        }

        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/start", equipe.Technicien, new { version = await VersionAsync(dossier, equipe.Responsable) }))
        {
        }

        var reexamen = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        using var attenteRefusee = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/hold", equipe.Technicien,
            new { motif = "Livraison", reexamenLe = reexamen, version = await VersionAsync(dossier, equipe.Responsable) });
        Assert.Equal("danger_non_maitrise", await CodeAsync(attenteRefusee));

        // Technicien en charge d'une intervention ouverte : destinataire des nouvelles alertes (Q3-A).
        var cree = (await DangerAsync(equipe.Responsable)).GetProperty("creeLe").GetDateTimeOffset();
        Assert.Equal(5, (await PasserAsync(cree.AddMinutes(15))).Notifications);
        Assert.Contains("DangerNonPrisEnCompte", await TypesAsync(equipe.Technicien));

        using (await AgirAsync(dossier, danger, "securing", equipe.Technicien, new { action = "ProtectionProvisoire", commentaire = "Goulotte posée" }))
        {
        }

        using (var attente = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/hold", equipe.Technicien,
            new { motif = "Livraison", reexamenLe = reexamen, version = await VersionAsync(dossier, equipe.Responsable) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, attente.StatusCode);
        }

        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/resume", equipe.Technicien, new { version = await VersionAsync(dossier, equipe.Responsable) }))
        {
        }

        using var formulaire = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(new { actions = "Câble remplacé", etatFinal = "Resolu", version = await VersionAsync(dossier, equipe.Responsable) })), "demande" },
        };
        using (var cloture = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/close", equipe.Technicien, formulaire))
        {
            Assert.Equal(HttpStatusCode.NoContent, cloture.StatusCode);
        }

        // Dossier clôturé : le danger reste en cours (la clôture ne le lève pas) et continue d'alerter.
        var enCours = (await LireAsync(Dangers, equipe.Pasteur)).EnumerateArray().Single();
        Assert.Equal(("Cloture", "Ouvert"), (enCours.GetProperty("statutDossier").GetString(), enCours.GetProperty("danger").GetProperty("etat").GetString()));
        Assert.Equal(4, (await PasserAsync(cree.AddMinutes(75))).Notifications); // le technicien n'a plus d'intervention ouverte

        using var leveeAvantPrise = await AgirAsync(dossier, danger, "lift", equipe.Delegue, new { motif = "ReparationDossierResolu" });
        using (await AgirAsync(dossier, danger, "acknowledge", equipe.Delegue, new { }))
        {
        }

        using var ecarteeSecurisee = await AgirAsync(dossier, danger, "dismiss", equipe.Delegue, new { motif = "AucunDangerConstate" });
        using var levee = await AgirAsync(dossier, danger, "lift", equipe.Delegue, new { motif = "ReparationDossierResolu" });
        Assert.Equal("non_pris_en_compte", await CodeAsync(leveeAvantPrise));
        Assert.Equal("securite_declaree", await CodeAsync(ecarteeSecurisee));
        Assert.Equal(HttpStatusCode.NoContent, levee.StatusCode);
        Assert.Empty((await LireAsync(Dangers, equipe.Pasteur)).EnumerateArray());
        Assert.Equal(0, (await PasserAsync(cree.AddMinutes(135))).Notifications);
    }

    [Fact]
    public async Task AManagerDismissesADangerWithAReasonAndOtherRolesOrOrganisationsCannotActOrSee()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var dossier = await DeclarerAsync(equipe.Technicien, zone, danger: true);
        var autreDossier = await DeclarerAsync(equipe.Technicien, zone, danger: false);
        var danger = await DangerIdAsync(equipe.Responsable);
        var autre = await IdentiteModule.OuvrirOrganisationAsync(ApiServices, "Saint-Pierre", "admin@autre.test", "Admin", CancellationToken.None, premierSite: "Site nord");
        using (await EnvoyerSansJetonAsync("/auth/activate", new { jeton = autre.JetonActivation, motDePasse = MotDePasse }))
        {
        }

        var etranger = await JetonAsync("admin@autre.test");
        var administrateur = await JetonAsync("admin@inventaire.test");

        using var listeEtrangere = await EnvoyerAsync(HttpMethod.Get, Dangers, etranger);
        using var priseEtrangere = await AgirAsync(dossier, danger, "acknowledge", etranger, new { version = 0u });
        using var listeAdministrateur = await EnvoyerAsync(HttpMethod.Get, Dangers, administrateur);
        using var parLaCoordination = await AgirAsync(dossier, danger, "acknowledge", equipe.Coordination, new { });
        using var parLeTechnicien = await AgirAsync(dossier, danger, "dismiss", equipe.Technicien, new { motif = "AucunDangerConstate" });
        using var mauvaisDossier = await AgirAsync(autreDossier, danger, "acknowledge", equipe.Responsable, new { version = 0u });
        using var sansVersion = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dangers/{danger}/acknowledge", equipe.Responsable, new { });
        using var versionPerimee = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dangers/{danger}/acknowledge", equipe.Responsable, new { version = 1u });
        using var motifInconnu = await AgirAsync(dossier, danger, "dismiss", equipe.Delegue, new { motif = "Inconnu" });
        using var autreSansCommentaire = await AgirAsync(dossier, danger, "dismiss", equipe.Delegue, new { motif = "Autre" });
        using var ecarte = await AgirAsync(dossier, danger, "dismiss", equipe.Delegue, new { motif = "Autre", commentaire = "Simple reflet sur le tableau" });
        using var priseApres = await AgirAsync(dossier, danger, "acknowledge", equipe.Responsable, new { });

        Assert.Equal(HttpStatusCode.NotFound, listeEtrangere.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, priseEtrangere.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listeAdministrateur.StatusCode); // sans rôle de site, aucun accès aux opérations (non implicite)
        Assert.Equal(HttpStatusCode.Forbidden, parLaCoordination.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, parLeTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mauvaisDossier.StatusCode);
        Assert.Equal("version", await ChampAsync(sansVersion));
        Assert.Equal("modifie_entre_temps", await CodeAsync(versionPerimee));
        Assert.Equal("motif", await ChampAsync(motifInconnu));
        Assert.Equal("commentaire", await ChampAsync(autreSansCommentaire));
        Assert.Equal(HttpStatusCode.NoContent, ecarte.StatusCode);
        Assert.Equal("danger_clos", await CodeAsync(priseApres));
        var suivi = (await LireAsync($"{Dossiers}/{dossier}", equipe.Pasteur)).GetProperty("dangers")[0];
        Assert.Equal(("Ecarte", "Ecarte", "Autre"),
            (suivi.GetProperty("etat").GetString(), suivi.GetProperty("fin").GetProperty("nature").GetString(), suivi.GetProperty("fin").GetProperty("motif").GetString()));
        Assert.Equal(0L, await Database.CountAsync("SELECT count(*) FROM maintenance.alertes_danger WHERE organisation_id <> @o", ("o", Organisation)));
        Assert.Equal(0, (await PasserAsync(DateTimeOffset.UtcNow.AddMinutes(30))).Notifications);

        // Ouverture réservée à la fonction du planificateur : ni le rôle applicatif ni le propriétaire connecté directement
        // ne lisent les alertes d'une organisation en posant la variable eux-mêmes.
        await using var application = new NpgsqlConnection(Database.ApplicationConnectionString);
        await application.OpenAsync();
        await using var proprietaire = new NpgsqlConnection(Database.MigrationsConnectionString);
        await proprietaire.OpenAsync();
        Assert.Equal(0L, await CompterAsync(application, "SET app.planificateur = 'on'; SELECT count(*) FROM maintenance.alertes_danger"));
        Assert.Equal(0L, await CompterAsync(proprietaire, "SET app.planificateur = 'on'; SELECT count(*) FROM maintenance.alertes_danger"));
        var refus = await Assert.ThrowsAsync<PostgresException>(() => CompterAsync(application,
            $"SET app.organisation_id = '{Organisation}'; DELETE FROM maintenance.alertes_danger"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState); // ni suppression…
        var colonne = await Assert.ThrowsAsync<PostgresException>(() => CompterAsync(application,
            $"SET app.organisation_id = '{Organisation}'; UPDATE maintenance.alertes_danger SET signalement_id = '{autreDossier}'"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, colonne.SqlState); // …ni changement de dossier
    }

    private static async Task<long> CompterAsync(NpgsqlConnection connexion, string sql)
    {
        await using var commande = new NpgsqlCommand(sql, connexion);
        await using var lecteur = await commande.ExecuteReaderAsync();
        do
        {
            if (lecteur.FieldCount > 0 && await lecteur.ReadAsync())
            {
                return lecteur.GetInt64(0);
            }
        }
        while (await lecteur.NextResultAsync());

        return -1;
    }

    [Fact]
    public async Task TheSiteDangerInstructionsDefaultToTheFramingTextAndOnlyTheAdministratorChangesThem()
    {
        var equipe = await EquipeAsync();
        var administrateur = await JetonAsync("admin@inventaire.test");
        var chemin = $"/organizations/{Organisation}/sites/{Site}/danger-instructions";

        var defaut = await LireAsync(chemin, equipe.Technicien);
        using var parLeResponsable = await EnvoyerAsync(HttpMethod.Put, chemin, equipe.Responsable, new { consigne = "Autre" });
        using var tropLongue = await EnvoyerAsync(HttpMethod.Put, chemin, administrateur, new { consigne = new string('x', 501) });
        using var modifiee = await EnvoyerAsync(HttpMethod.Put, chemin, administrateur, new { consigne = "Fermez la grille du clocher.\nAppelez le 18." });
        var propre = await LireAsync(chemin, administrateur);
        using var retablie = await EnvoyerAsync(HttpMethod.Put, chemin, administrateur, new { consigne = "  " });
        var apres = await LireAsync(chemin, equipe.Pasteur);
        var autre = await IdentiteModule.OuvrirOrganisationAsync(ApiServices, "Saint-Pierre", "admin@autre.test", "Admin", CancellationToken.None, premierSite: "Site nord");
        using (await EnvoyerSansJetonAsync("/auth/activate", new { jeton = autre.JetonActivation, motDePasse = MotDePasse }))
        {
        }

        using var etrangere = await EnvoyerAsync(HttpMethod.Get, chemin, await JetonAsync("admin@autre.test"));

        Assert.StartsWith("Éloignez les personnes de la zone", defaut.GetProperty("consigne").GetString(), StringComparison.Ordinal);
        Assert.True(defaut.GetProperty("parDefaut").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, parLeResponsable.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tropLongue.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, modifiee.StatusCode);
        Assert.Equal(("Fermez la grille du clocher.\nAppelez le 18.", false), (propre.GetProperty("consigne").GetString(), propre.GetProperty("parDefaut").GetBoolean()));
        Assert.Equal(HttpStatusCode.NoContent, retablie.StatusCode);
        Assert.True(apres.GetProperty("parDefaut").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, etrangere.StatusCode);
    }

    [Fact]
    public async Task TheMigrationClosesEarlierAlertsOfClosedCasesAndKeepsThoseOfOpenCasesInProgress()
    {
        var equipe = await EquipeAsync();
        var zone = await ZoneAsync(equipe.Technicien);
        var ouvert = await DeclarerAsync(equipe.Technicien, zone, danger: true);
        var classe = await DeclarerAsync(equipe.Technicien, zone, danger: true);
        using (var classement = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{classe}/dismiss", equipe.Responsable,
            new { motif = "AucunProbleme", version = await VersionAsync(classe, equipe.Responsable) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, classement.StatusCode);
        }

        // Retour à l'état d'avant le suivi (migration précédente), puis reprise : comme sur une base déjà en service.
        await using (var proprietaire = NpgsqlDataSource.Create(Database.MigrationsConnectionString))
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<GestionSite.Modules.Maintenance.Persistence.MaintenanceDbContext>();
            GestionSite.SharedKernel.Persistence.PersistanceModule.Configurer(options, proprietaire, "maintenance", "__ef_migrations", requete: null);
            await using var context = new GestionSite.Modules.Maintenance.Persistence.MaintenanceDbContext(options.Options, new GestionSite.SharedKernel.OrganisationCourante());
            var migrateur = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(context);
            await migrateur.MigrateAsync("20261103064227_RappelsPlanificateur");
            await migrateur.MigrateAsync();
        }

        var fiche = await LireAsync($"{Dossiers}/{classe}", equipe.Pasteur);
        var close = fiche.GetProperty("dangers")[0];
        Assert.Equal(("Ecarte", "Autre", JsonValueKind.Null),
            (close.GetProperty("etat").GetString(), close.GetProperty("fin").GetProperty("motif").GetString(), close.GetProperty("fin").GetProperty("parCompteId").ValueKind));
        var enCours = (await LireAsync(Dangers, equipe.Pasteur)).EnumerateArray().Single();
        Assert.Equal((ouvert, "Ouvert"), (enCours.GetProperty("dossierId").GetGuid(), enCours.GetProperty("danger").GetProperty("etat").GetString()));
    }

    private Task<BilanPassage> PasserAsync(DateTimeOffset maintenant) =>
        ApiServices.GetRequiredService<ServicePlanificateur>().PasserAsync(maintenant, CancellationToken.None);

    // Étape d'un danger à la version lue (sauf version donnée dans le corps).
    private async Task<HttpResponseMessage> AgirAsync(Guid dossier, Guid danger, string etape, string jeton, object corps)
    {
        var champs = JsonSerializer.SerializeToElement(corps).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        if (!champs.ContainsKey("version"))
        {
            var fiche = await LireAsync($"{Dossiers}/{dossier}", (await JetonLecteurAsync()));
            champs["version"] = fiche.GetProperty("dangers").EnumerateArray().FirstOrDefault(d => d.GetProperty("id").GetGuid() == danger) is { ValueKind: JsonValueKind.Object } d
                ? d.GetProperty("version").GetUInt32()
                : 0u;
        }

        return await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/dangers/{danger}/{etape}", jeton, champs);
    }

    private string? _lecteur;

    private async Task<string> JetonLecteurAsync() => _lecteur ??= await JetonAsync("pasteur@inventaire.test");

    private async Task<JsonElement> DangerAsync(string jeton)
    {
        var dangers = await LireAsync(Dangers, jeton);
        return dangers.EnumerateArray().Single().GetProperty("danger");
    }

    private async Task<Guid> DangerIdAsync(string jeton) => (await DangerAsync(jeton)).GetProperty("id").GetGuid();

    private static async Task<string?> CodeAsync(HttpResponseMessage reponse)
    {
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    }

    private async Task<List<string?>> TypesAsync(string jeton, string? prefixe = null) =>
        [.. (await LireAsync(Centre, jeton)).GetProperty("notifications").EnumerateArray()
            .Select(n => n.GetProperty("type").GetString()).Where(t => prefixe is null || t!.StartsWith(prefixe, StringComparison.Ordinal))];

    private sealed record Equipe(string Responsable, string Delegue, string Technicien, string Pasteur, string Coordination);

    private async Task<Equipe> EquipeAsync()
    {
        await MembreAsync("responsable@inventaire.test", Role.Responsable);
        await MembreAsync("delegue@inventaire.test", Role.Delegue);
        await MembreAsync("coordination@inventaire.test", Role.Coordination);
        return new Equipe(
            await JetonAsync("responsable@inventaire.test"), await JetonAsync("delegue@inventaire.test"), await JetonAsync("technicien@inventaire.test"),
            await JetonAsync("pasteur@inventaire.test"), await JetonAsync("coordination@inventaire.test"));
    }

    private async Task<Guid> ZoneAsync(string jeton)
    {
        var batiment = await CreerAsync($"{Base}/buildings", new { nom = "Église" }, jeton);
        return (await LireAsync($"{Base}/structure", jeton)).EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batiment)
            .GetProperty("zones")[0].GetProperty("id").GetGuid();
    }

    private Task<Guid> DeclarerAsync(string jeton, Guid zone, bool danger) =>
        CreerAsync(Dossiers, new { commandeId = Guid.NewGuid(), zoneId = zone, description = "Câble dénudé près de l'autel", danger, activiteBloquee = false }, jeton);

    private async Task QualifierAsync(Guid dossier, Guid zone, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", jeton,
            new { priorite = "Urgente", zoneId = zone, version = await VersionAsync(dossier, jeton) });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<uint> VersionAsync(Guid dossier, string jeton) => (await LireAsync($"{Dossiers}/{dossier}", jeton)).GetProperty("version").GetUInt32();

    private async Task<Guid> CompteAsync(string jeton) => (await LireAsync("/me", jeton)).GetProperty("compteId").GetGuid();

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

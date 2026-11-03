using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;
using Npgsql;

namespace GestionSite.Api.Tests;

/// <summary>
/// Centre de notifications et émission dans la transaction de l'événement (US-10, S10-01) : destinataires de Q3-A, auteur
/// exclu, aucun doublon au rejeu, lecture et marquage par le seul destinataire, isolation des organisations, T-03.11, T-03.19.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class NotificationsTests(InfrastructureFixture infrastructure) : InventaireApiTests(infrastructure)
{
    private static readonly System.Globalization.CultureInfo Invariante = System.Globalization.CultureInfo.InvariantCulture;

    private string Centre => $"/organizations/{Organisation}/me/notifications";

    private string Dossiers => Base.Replace("/inventory", "/cases", StringComparison.Ordinal);

    private string Preventif => Base.Replace("/inventory", "/preventive", StringComparison.Ordinal);

    [Fact]
    public async Task APublicDangerNotifiesManagersAndPastoralStaffOnceWithoutTheDeclarantsDetails()
    {
        var jetons = await EquipeAsync();
        var depot = new
        {
            commandeId = Guid.NewGuid(),
            nature = "Incident",
            description = "Odeur de brûlé au tableau électrique.",
            emplacement = "Sacristie",
            danger = true,
            activiteBloquee = true,
            nom = "Jeanne Témoin",
            contact = "06 11 22 33 44",
        };

        using var premier = await DeposerAsync(depot);
        using var rejeu = await DeposerAsync(depot);
        var responsable = await CentreAsync(jetons.Responsable);
        var brut = await BrutAsync(jetons.Pasteur);

        Assert.Equal(HttpStatusCode.OK, premier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejeu.StatusCode);
        // Q3-A : R, D, P, C du site ; ni le technicien sans intervention ni l'administrateur sans rôle ; une seule fois (rejeu).
        Assert.Equal(4L, await Database.CountAsync("SELECT count(*) FROM maintenance.notifications"));
        foreach (var jeton in new[] { jetons.Delegue, jetons.Pasteur, jetons.Coordination })
        {
            Assert.Equal(["DangerSignale"], (await CentreAsync(jeton)).GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("type").GetString()));
        }

        Assert.Empty((await CentreAsync(jetons.Technicien)).GetProperty("notifications").EnumerateArray());
        Assert.Empty((await CentreAsync(await JetonAsync("admin@inventaire.test"))).GetProperty("notifications").EnumerateArray());
        var notification = responsable.GetProperty("notifications").EnumerateArray().Single();
        Assert.Equal(1, responsable.GetProperty("nonLues").GetInt32());
        Assert.Equal(("DangerSignale", "Dossier"), (notification.GetProperty("type").GetString(), notification.GetProperty("objet").GetProperty("type").GetString()));
        Assert.Equal(Site, notification.GetProperty("siteId").GetGuid());
        Assert.Equal("Odeur de brûlé au tableau électrique.", notification.GetProperty("objet").GetProperty("titre").GetString());
        Assert.Equal(JsonValueKind.Null, notification.GetProperty("lueLe").ValueKind);
        // Danger et activité bloquée : une seule notification, et rien du déclarant (ni nom ni contact) dans le centre.
        Assert.DoesNotContain("Jeanne", brut, StringComparison.Ordinal);
        Assert.DoesNotContain("06 11", brut, StringComparison.Ordinal);
        Assert.Equal(0L, await Database.CountAsync(
            "SELECT count(*) FROM maintenance.notifications WHERE cle LIKE '%Jeanne%' OR cle LIKE '%06 11%'"));
    }

    [Fact]
    public async Task TheAuthorIsNeverNotifiedAndABlockedActivityWithoutDangerGoesToManagersOnly()
    {
        var jetons = await EquipeAsync();
        var zone = await ZoneAsync(jetons.Technicien);

        await DeclarerAsync(jetons.Pasteur, zone, danger: true, activiteBloquee: false);
        await DeclarerAsync(jetons.Technicien, zone, danger: false, activiteBloquee: true);
        await DeclarerAsync(jetons.Responsable, zone, danger: false, activiteBloquee: false);

        Assert.Equal(["ActiviteBloquee", "DangerSignale"], Types(await CentreAsync(jetons.Responsable)));
        Assert.Equal(["ActiviteBloquee", "DangerSignale"], Types(await CentreAsync(jetons.Delegue)));
        Assert.Equal(["DangerSignale"], Types(await CentreAsync(jetons.Coordination)));
        Assert.Empty(Types(await CentreAsync(jetons.Pasteur))); // déclarant du danger
        Assert.Empty(Types(await CentreAsync(jetons.Technicien)));
    }

    [Fact]
    public async Task AssignmentNotifiesThePersonInChargeAndTheAutomaticClosureNotifiesManagers()
    {
        var jetons = await EquipeAsync();
        var zone = await ZoneAsync(jetons.Technicien);
        var dossier = await DeclarerAsync(jetons.Pasteur, zone, danger: false, activiteBloquee: false);
        var technicien = await CompteAsync(jetons.Technicien);
        await QualifierAsync(dossier, zone, jetons.Responsable);
        using (var affecte = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", jetons.Responsable,
            new { titre = "Remplacer le disjoncteur", intervenantCompteId = technicien, version = await VersionAsync(dossier, jetons.Responsable) }))
        {
            Assert.Equal(HttpStatusCode.OK, affecte.StatusCode);
        }

        using (var demarre = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/start", jetons.Technicien,
            new { version = await VersionAsync(dossier, jetons.Responsable) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, demarre.StatusCode);
        }

        using var formulaire = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(new { actions = "Disjoncteur remplacé", etatFinal = "Resolu", version = await VersionAsync(dossier, jetons.Responsable) })), "demande" },
        };
        using var cloture = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions/1/close", jetons.Technicien, formulaire);

        Assert.Equal(HttpStatusCode.NoContent, cloture.StatusCode);
        Assert.Equal(["Affectation"], Types(await CentreAsync(jetons.Technicien))); // pas notifié de sa propre clôture
        // T-03.11 : dossier clôturé automatiquement, R/D notifiés ; le responsable n'est pas notifié de sa propre affectation.
        Assert.Equal(["ClotureAutomatique"], Types(await CentreAsync(jetons.Responsable)));
        Assert.Equal(["ClotureAutomatique"], Types(await CentreAsync(jetons.Delegue)));
        Assert.Empty(Types(await CentreAsync(jetons.Pasteur)));
    }

    [Fact]
    public async Task AnAttachedFindingNotifiesManagersAndADangerousOneAlsoThePersonInCharge()
    {
        var jetons = await EquipeAsync();
        await CreerAsync($"{Base}/buildings", new { nom = "Église" }, jetons.Technicien);
        var zone = (await LireAsync($"{Base}/structure", jetons.Technicien)).EnumerateArray().First().GetProperty("zones")[0].GetProperty("id").GetGuid();
        var tableau = await CreerAsync($"{Base}/components", new { nom = "Tableau général", corpsEtat = "CourantsForts", zoneId = zone }, jetons.Technicien);
        var plan = await PlanAsync(jetons.Responsable, tableau);

        await InspecterAsync(plan, jetons.Responsable, danger: false); // nouveau dossier sans danger : aucune notification
        var dossier = (await LireAsync(Dossiers, jetons.Pasteur)).EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Equal(0L, await Database.CountAsync("SELECT count(*) FROM maintenance.notifications"));
        await QualifierAsync(dossier, zone, jetons.Responsable, tableau);
        using (await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/interventions", jetons.Responsable,
            new { titre = "Resserrer", intervenantCompteId = await CompteAsync(jetons.Technicien), version = await VersionAsync(dossier, jetons.Responsable) }))
        {
        }

        await InspecterAsync(plan, jetons.Responsable, danger: false); // T-03.19 : rattaché
        await InspecterAsync(plan, jetons.Responsable, danger: true); // T-03.20 : rattaché avec danger

        Assert.Equal(["DangerSignale", "ConstatRattache"], Types(await CentreAsync(jetons.Delegue)));
        Assert.Equal(["DangerSignale", "Affectation"], Types(await CentreAsync(jetons.Technicien))); // en charge d'une intervention ouverte
        Assert.Equal(["DangerSignale"], Types(await CentreAsync(jetons.Pasteur)));
        Assert.Empty(Types(await CentreAsync(jetons.Responsable))); // auteur des trois contrôles et de l'affectation
    }

    [Fact]
    public async Task OnlyTheRecipientReadsAndMarksItsNotificationsAndAnotherOrganisationGets404()
    {
        var jetons = await EquipeAsync();
        var zone = await ZoneAsync(jetons.Technicien);
        for (var i = 0; i < 3; i++)
        {
            await DeclarerAsync(jetons.Pasteur, zone, danger: true, activiteBloquee: false);
        }

        var page = await CentreAsync(jetons.Responsable, "?limite=2");
        var ids = page.GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()).ToList();
        var suite = await CentreAsync(jetons.Responsable, $"?limite=2&avant={ids[^1]}");
        var duDelegue = (await CentreAsync(jetons.Delegue)).GetProperty("notifications")[0].GetProperty("id").GetGuid();

        using var parUnAutre = await EnvoyerAsync(HttpMethod.Post, $"{Centre}/{duDelegue}/read", jetons.Responsable);
        using var lue = await EnvoyerAsync(HttpMethod.Post, $"{Centre}/{ids[0]}/read", jetons.Responsable);
        using var relue = await EnvoyerAsync(HttpMethod.Post, $"{Centre}/{ids[0]}/read", jetons.Responsable);
        using var limite = await EnvoyerAsync(HttpMethod.Get, $"{Centre}?limite=0", jetons.Responsable);
        var nonLues = await CentreAsync(jetons.Responsable, "?nonLues=true");
        var plusAncienne = suite.GetProperty("notifications")[0].GetProperty("id").GetGuid();
        using var jusqua = await EnvoyerAsync(HttpMethod.Post, $"{Centre}/read?jusqua={plusAncienne}", jetons.Responsable);
        var restante = await CentreAsync(jetons.Responsable, "?nonLues=true");

        Assert.Equal(2, ids.Count);
        Assert.Equal(3, page.GetProperty("nonLues").GetInt32());
        Assert.Single(suite.GetProperty("notifications").EnumerateArray());
        Assert.True(ids[0].CompareTo(ids[1]) > 0); // du plus récent au plus ancien
        Assert.Equal(HttpStatusCode.NotFound, parUnAutre.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, lue.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, relue.StatusCode);
        Assert.Equal("limite", await ChampAsync(limite));
        Assert.Equal(2, nonLues.GetProperty("notifications").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, jusqua.StatusCode);
        // « Tout marquer lu » jusqu'à la plus récente affichée (ici la plus ancienne) : une plus récente reste non lue.
        Assert.Equal([ids[1]],
            restante.GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()));
        Assert.Equal(3, (await CentreAsync(jetons.Delegue)).GetProperty("nonLues").GetInt32()); // ses notifications intactes
        using (var tout = await EnvoyerAsync(HttpMethod.Post, $"{Centre}/read", jetons.Delegue))
        {
            Assert.Equal(HttpStatusCode.NoContent, tout.StatusCode);
        }

        Assert.Equal(0, (await CentreAsync(jetons.Delegue)).GetProperty("nonLues").GetInt32());

        // Autre organisation : un membre de B qui lit le centre de A reçoit 404, comme sur ses identifiants.
        var autre = await IdentiteModule.OuvrirOrganisationAsync(ApiServices, "Saint-Pierre", "admin@autre.test", "Admin", CancellationToken.None, premierSite: "Site nord");
        using (await EnvoyerSansJetonAsync("/auth/activate", new { jeton = autre.JetonActivation, motDePasse = MotDePasse }))
        {
        }

        var etranger = await JetonAsync("admin@autre.test");
        using var horsOrganisation = await EnvoyerAsync(HttpMethod.Get, Centre, etranger);
        using var marquageEtranger = await EnvoyerAsync(HttpMethod.Post, $"{Centre}/{ids[1]}/read", etranger);
        using var sonCentre = await EnvoyerAsync(HttpMethod.Get, $"/organizations/{autre.OrganisationId}/me/notifications", etranger);
        Assert.Equal(HttpStatusCode.NotFound, horsOrganisation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, marquageEtranger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sonCentre.StatusCode); // administrateur sans rôle de site : centre vide, pas 404
    }

    [Fact]
    public async Task ARevokedMemberNoLongerSeesItsNotifications()
    {
        var jetons = await EquipeAsync();
        var zone = await ZoneAsync(jetons.Technicien);
        await DeclarerAsync(jetons.Responsable, zone, danger: true, activiteBloquee: false);
        Assert.Single(Types(await CentreAsync(jetons.Pasteur)));
        var admin = await JetonAsync("admin@inventaire.test");
        var pasteur = (await LireAsync($"/organizations/{Organisation}/members", admin)).EnumerateArray()
            .Single(m => m.GetProperty("email").GetString() == "pasteur@inventaire.test").GetProperty("membreId").GetGuid();

        using (var revocation = await EnvoyerAsync(HttpMethod.Delete, $"/organizations/{Organisation}/members/{pasteur}", admin, new { motif = "Départ" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, revocation.StatusCode);
        }

        using var apres = await EnvoyerAsync(HttpMethod.Get, Centre, jetons.Pasteur);
        Assert.Equal(HttpStatusCode.NotFound, apres.StatusCode);
    }

    [Fact]
    public async Task TheDatabaseShowsANotificationToItsRecipientOnlyAndOnlyItsReadDateCanChange()
    {
        var jetons = await EquipeAsync();
        var zone = await ZoneAsync(jetons.Technicien);
        await DeclarerAsync(jetons.Technicien, zone, danger: true, activiteBloquee: false);
        var responsable = await CompteAsync(jetons.Responsable);
        var delegue = await CompteAsync(jetons.Delegue);

        await using var connexion = new NpgsqlConnection(Database.ApplicationConnectionString);
        await connexion.OpenAsync();
        Assert.Equal(0L, await CompterAsync(connexion, compte: null)); // RLS : organisation seule, sans destinataire
        Assert.Equal(1L, await CompterAsync(connexion, responsable));
        Assert.Equal(1L, await CompterAsync(connexion, delegue));
        Assert.Equal(1L, await ExecuterAsync(connexion, responsable, "UPDATE maintenance.notifications SET lue_le = now()"));
        var refus = await Assert.ThrowsAsync<PostgresException>(() => ExecuterAsync(connexion, responsable, "UPDATE maintenance.notifications SET compte_id = gen_random_uuid()"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState);
        var suppression = await Assert.ThrowsAsync<PostgresException>(() => ExecuterAsync(connexion, responsable, "DELETE FROM maintenance.notifications"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, suppression.SqlState);
        Assert.Equal(3L, await Database.CountAsync("SELECT count(*) FROM maintenance.notifications WHERE lue_le IS NULL")); // D, P, C intactes
    }

    private async Task<long> CompterAsync(NpgsqlConnection connexion, Guid? compte)
    {
        await using var transaction = await connexion.BeginTransactionAsync();
        await using var commande = new NpgsqlCommand(
            $"SELECT set_config('{OrganisationCourante.VariableSession}', @org, true), set_config('{CompteCourant.VariableSession}', @compte, true); " +
            "SELECT count(*) FROM maintenance.notifications", connexion, transaction);
        commande.Parameters.AddWithValue("org", Organisation.ToString());
        commande.Parameters.AddWithValue("compte", compte?.ToString() ?? string.Empty);
        await using var lecteur = await commande.ExecuteReaderAsync();
        await lecteur.NextResultAsync();
        await lecteur.ReadAsync();
        return lecteur.GetInt64(0);
    }

    private async Task<int> ExecuterAsync(NpgsqlConnection connexion, Guid compte, string sql)
    {
        await using var transaction = await connexion.BeginTransactionAsync();
        await using (var contexte = new NpgsqlCommand(
            $"SELECT set_config('{OrganisationCourante.VariableSession}', @org, true), set_config('{CompteCourant.VariableSession}', @compte, true)", connexion, transaction))
        {
            contexte.Parameters.AddWithValue("org", Organisation.ToString());
            contexte.Parameters.AddWithValue("compte", compte.ToString());
            await contexte.ExecuteNonQueryAsync();
        }

        await using var commande = new NpgsqlCommand(sql, connexion, transaction);
        var lignes = await commande.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return lignes;
    }

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

    private Task<Guid> DeclarerAsync(string jeton, Guid zone, bool danger, bool activiteBloquee) =>
        CreerAsync(Dossiers, new { commandeId = Guid.NewGuid(), zoneId = zone, description = "Disjoncteur qui chauffe", danger, activiteBloquee }, jeton);

    private async Task<HttpResponseMessage> DeposerAsync(object depot)
    {
        var code = await CodePublicAsync();
        return await EnvoyerSansJetonAsync($"/public/sites/{code}/reports", depot);
    }

    private async Task<string> CodePublicAsync()
    {
        await using var connexion = new NpgsqlConnection(Database.ConnectionString);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand("SELECT code FROM identite.codes_sites_publics WHERE site_id = @site", connexion);
        commande.Parameters.AddWithValue("site", Site);
        return (string)(await commande.ExecuteScalarAsync())!;
    }

    private async Task QualifierAsync(Guid dossier, Guid zone, string jeton, Guid? composant = null)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Dossiers}/{dossier}/qualify", jeton,
            new { priorite = "Haute", zoneId = zone, composantId = composant, version = await VersionAsync(dossier, jeton) });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<uint> VersionAsync(Guid dossier, string jeton) => (await LireAsync($"{Dossiers}/{dossier}", jeton)).GetProperty("version").GetUInt32();

    private async Task<Guid> CompteAsync(string jeton) => (await LireAsync("/me", jeton)).GetProperty("compteId").GetGuid();

    private async Task<Guid> PlanAsync(string jeton, Guid composant)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans", jeton, new
        {
            commandeId = Guid.NewGuid(),
            cible = new { type = "Composant", composantId = composant },
            nature = "InspectionInterne",
            libelle = "Contrôle",
            @base = "BonnePratique",
            periode = new { valeur = 1, unite = "Ans" },
            premiereEcheance = JourCivil.Du(DateTimeOffset.UtcNow),
        });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task InspecterAsync(Guid plan, string jeton, bool danger)
    {
        var echeance = DateOnly.Parse(
            (await LireAsync($"{Preventif}/plans/{plan}", jeton)).GetProperty("plan").GetProperty("prochaine").GetProperty("echeance").GetString()!, Invariante);
        var demande = new
        {
            commandeId = Guid.NewGuid(),
            echeance,
            realiseLe = JourCivil.Du(DateTimeOffset.UtcNow),
            resultat = "NonConforme",
            grilleVersion = 1,
            etat = "Degrade",
            points = new object[] { new { conforme = true, danger = false }, new { conforme = true, danger = false }, new { conforme = false, danger } },
        };
        using var formulaire = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(demande)), "demande" } };
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{Preventif}/plans/{plan}/perform", jeton, formulaire);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    private Task<JsonElement> CentreAsync(string jeton, string requete = "") => LireAsync(Centre + requete, jeton);

    private async Task<string> BrutAsync(string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, Centre, jeton);
        return await reponse.Content.ReadAsStringAsync();
    }

    private static List<string?> Types(JsonElement page) =>
        [.. page.GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("type").GetString())];

    private async Task<JsonElement> LireAsync(string chemin, string jeton)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, jeton);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadFromJsonAsync<JsonElement>();
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestionSite.Api.Tests;

/// <summary>
/// Administration autonome (US-31, décisions du porteur du 03/11) sur l'API et PostgreSQL réels : sites en double, protection
/// de son propre accès, lien de réinitialisation, détail des invitations, journal d'un membre, isolation entre organisations.
/// </summary>
[Collection(SharedInfrastructure.Name)]
public sealed class AdministrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string AdminEmail = "admin@us31.test";
    private const string MotDePasse = "un-mot-de-passe-solide";
    private const string NouveauMotDePasse = "une-autre-phrase-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private Guid _organisation;
    private string _admin = null!;
    private Guid _nord;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        _client = _api.CreateClient();
        (_organisation, _admin) = await OuvrirAsync("Saint-Paul", AdminEmail);
        _nord = await CreerSiteAsync(_organisation, _admin, "Chapelle Saint-Roch");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ASiteNameAlreadyUsedIsRefusedIgnoringCaseAndAccents()
    {
        using var minuscules = await Post($"/organizations/{_organisation}/sites", _admin, new { nom = "chapelle saint roch" });
        using var accents = await Post($"/organizations/{_organisation}/sites", _admin, new { nom = "  Châpelle   SAINT-Roch " });
        using var vide = await Post($"/organizations/{_organisation}/sites", _admin, new { nom = "  " });
        using var autre = await Post($"/organizations/{_organisation}/sites", _admin, new { nom = "Chapelle Saint-Paul" });
        var (ailleurs, adminAilleurs) = await OuvrirAsync("Autre paroisse", "admin@ailleurs.test");
        using var memeNomAilleurs = await Post($"/organizations/{ailleurs}/sites", adminAilleurs, new { nom = "Chapelle Saint-Roch" });

        Assert.Equal(HttpStatusCode.Conflict, minuscules.StatusCode);
        Assert.Equal("site_existant", await CodeAsync(minuscules));
        Assert.Equal("site_existant", await CodeAsync(accents));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, vide.StatusCode);
        Assert.Equal(HttpStatusCode.Created, autre.StatusCode);
        Assert.Equal(HttpStatusCode.Created, memeNomAilleurs.StatusCode); // une autre organisation n'est pas concernée
        Assert.Equal(2L, await _database.CountAsync("SELECT count(*) FROM identite.sites WHERE organisation_id = @org", ("org", _organisation)));
    }

    [Fact]
    public async Task AnAdministratorCannotRevokeThemselvesNorDropTheirOwnAdministration()
    {
        var second = await MembreActifAsync("claire@us31.test", new { role = "Administrateur", siteId = (Guid?)null });
        var jetonSecond = await ConnecterAsync("claire@us31.test");
        var moi = await MembreAsync(AdminEmail);

        using var revocation = await Delete($"/organizations/{_organisation}/members/{moi.Id}", _admin, "Je pars");
        using var retrait = await Put($"/organizations/{_organisation}/members/{moi.Id}", _admin,
            new { roles = new object[] { new { role = "Pasteur", siteId = _nord } }, version = moi.Version, motif = "Je passe pasteur" });
        using var ajout = await Put($"/organizations/{_organisation}/members/{moi.Id}", _admin,
            new { roles = new object[] { new { role = "Administrateur", siteId = (Guid?)null }, new { role = "Pasteur", siteId = _nord } }, version = moi.Version, motif = "Je suis aussi pasteur" });
        using var parUnAutre = await Delete($"/organizations/{_organisation}/members/{moi.Id}", jetonSecond, "Départ de l'administrateur");

        Assert.Equal("propre_acces", await CodeAsync(revocation));
        Assert.Equal("propre_administration", await CodeAsync(retrait));
        Assert.Equal(HttpStatusCode.OK, ajout.StatusCode); // ses rôles de site restent modifiables
        Assert.Equal(HttpStatusCode.NoContent, parUnAutre.StatusCode);
        Assert.NotEqual(Guid.Empty, second);
    }

    [Fact]
    public async Task ANewActivationLinkIsJournaledAndAMemberHistoryNamesPeople()
    {
        using var cree = await Post($"/organizations/{_organisation}/members", _admin,
            new { nom = "Hugo B.", email = "hugo@us31.test", roles = new[] { new { role = "Technicien", siteId = (Guid?)_nord } }, motif = "Arrivée" });
        var hugo = (await Json(cree)).GetProperty("membreId").GetGuid();

        using var lien = await Post($"/organizations/{_organisation}/members/{hugo}/activation", _admin, new { });
        using var historique = await Get($"/organizations/{_organisation}/members/journal?membreId={hugo}", _admin);
        var entrees = (await Json(historique)).EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, lien.StatusCode);
        Assert.Equal(["NouveauLienActivation", "Creation"], entrees.Select(e => e.GetProperty("nature").GetString()));
        Assert.All(entrees, e => Assert.Equal("Hugo B.", e.GetProperty("nom").GetString()));
        Assert.All(entrees, e => Assert.Equal("Admin Saint-Paul", e.GetProperty("auteur").GetString()));
    }

    [Fact]
    public async Task AResetLinkLetsTheHolderChooseANewPasswordAndDisconnectsAllDevices()
    {
        var marc = await MembreActifAsync("marc@us31.test", new { role = "Technicien", siteId = (Guid?)_nord });
        using var connexion = await Post("/auth/login", null, new { email = "marc@us31.test", motDePasse = MotDePasse });
        var renouvellement = (await Json(connexion)).GetProperty("jetonRenouvellement").GetString();
        // Compte verrouillé par des essais : le lien le libère.
        for (var i = 0; i < 5; i++)
        {
            using var essai = await Post("/auth/login", null, new { email = "marc@us31.test", motDePasse = "mauvais-mot-de-passe" });
        }

        using var cree = await Post($"/organizations/{_organisation}/members/{marc}/password-reset", _admin, new { });
        var lien = await Json(cree);
        var jeton = lien.GetProperty("jeton").GetString();
        using var court = await Post("/auth/reset-password", null, new { jeton, motDePasse = "court" });
        using var reinitialise = await Post("/auth/reset-password", null, new { jeton, motDePasse = NouveauMotDePasse });
        using var encore = await Post("/auth/reset-password", null, new { jeton, motDePasse = NouveauMotDePasse });
        using var ancien = await Post("/auth/login", null, new { email = "marc@us31.test", motDePasse = MotDePasse });
        using var nouveau = await Post("/auth/login", null, new { email = "marc@us31.test", motDePasse = NouveauMotDePasse });
        using var autreAppareil = await Post("/auth/refresh", null, new { jetonRenouvellement = renouvellement });
        using var journal = await Get($"/organizations/{_organisation}/members/journal?membreId={marc}&limite=2", _admin);

        Assert.Equal(HttpStatusCode.OK, cree.StatusCode);
        var validite = lien.GetProperty("expireLe").GetDateTimeOffset() - DateTimeOffset.UtcNow;
        Assert.InRange(validite, TimeSpan.FromMinutes(58), TimeSpan.FromMinutes(61)); // 1 heure
        Assert.Equal(HttpStatusCode.UnprocessableEntity, court.StatusCode); // le lien reste utilisable
        Assert.Equal("marc@us31.test", (await Json(reinitialise)).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.Gone, encore.StatusCode); // usage unique
        Assert.Equal(HttpStatusCode.Unauthorized, ancien.StatusCode);
        Assert.Equal(HttpStatusCode.OK, nouveau.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, autreAppareil.StatusCode); // tous les appareils déconnectés
        Assert.Equal(["MotDePasseReinitialise", "LienReinitialisation"], (await Json(journal)).EnumerateArray().Select(e => e.GetProperty("nature").GetString()));
        Assert.Equal(0L, await _database.CountAsync(
            "SELECT count(*) FROM identite.reinitialisations WHERE encode(empreinte, 'escape') LIKE '%' || @jeton || '%'", ("jeton", jeton!)));
    }

    [Fact]
    public async Task AnExpiredResetLinkIsRefused()
    {
        var marc = await MembreActifAsync("marc@us31.test", new { role = "Technicien", siteId = (Guid?)_nord });
        using var cree = await Post($"/organizations/{_organisation}/members/{marc}/password-reset", _admin, new { });
        var jeton = (await Json(cree)).GetProperty("jeton").GetString();
        await _database.CountAsync("WITH maj AS (UPDATE identite.reinitialisations SET expire_le = now() - interval '1 minute' RETURNING 1) SELECT count(*) FROM maj");

        using var reponse = await Post("/auth/reset-password", null, new { jeton, motDePasse = NouveauMotDePasse });
        using var inconnu = await Post("/auth/reset-password", null, new { jeton = "AAAA", motDePasse = NouveauMotDePasse });

        Assert.Equal(HttpStatusCode.Gone, reponse.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, inconnu.StatusCode);
    }

    [Fact]
    public async Task AResetLinkIsOnlyForAnAccountCreatedHereAndLinkedToNoOtherOrganisation()
    {
        var (ailleurs, adminAilleurs) = await OuvrirAsync("Autre paroisse", "admin@ailleurs.test");
        var siteAilleurs = await CreerSiteAsync(ailleurs, adminAilleurs, "Temple");
        // Compte créé par l'autre organisation, puis invité ici et accepté : ni l'une ni l'autre ne peut créer de lien.
        await MembreActifAsync("luc@us31.test", new { role = "Technicien", siteId = (Guid?)siteAilleurs }, ailleurs, adminAilleurs);
        var luc = await InviterAsync(_organisation, _admin, "luc@us31.test", _nord);
        var jetonLuc = await ConnecterAsync("luc@us31.test");
        using (await Post($"/me/invitations/{luc}/accept", jetonLuc, new { }))
        {
        }

        var lucAilleurs = (await MembreAsync("luc@us31.test", ailleurs)).Id;
        using var parInvitant = await Post($"/organizations/{_organisation}/members/{luc}/password-reset", _admin, new { });
        using var parCreateur = await Post($"/organizations/{ailleurs}/members/{lucAilleurs}/password-reset", adminAilleurs, new { });
        // Invitation pas encore activée : il faut un nouveau lien d'activation, pas un lien de réinitialisation.
        using var cree = await Post($"/organizations/{_organisation}/members", _admin,
            new { nom = "Hugo B.", email = "hugo@us31.test", roles = new[] { new { role = "Technicien", siteId = (Guid?)_nord } } });
        using var enAttente = await Post($"/organizations/{_organisation}/members/{(await Json(cree)).GetProperty("membreId").GetGuid()}/password-reset", _admin, new { });

        Assert.Equal("reinitialisation_impossible", await CodeAsync(parInvitant));
        Assert.Equal("reinitialisation_impossible", await CodeAsync(parCreateur));
        Assert.Equal("reinitialisation_impossible", await CodeAsync(enAttente));
        Assert.Equal(0L, await _database.CountAsync("SELECT count(*) FROM identite.reinitialisations"));
    }

    [Fact]
    public async Task AResetLinkStopsWorkingOnceTheAccountJoinsAnotherOrganisation()
    {
        var marc = await MembreActifAsync("marc@us31.test", new { role = "Technicien", siteId = (Guid?)_nord });
        using var cree = await Post($"/organizations/{_organisation}/members/{marc}/password-reset", _admin, new { });
        var jeton = (await Json(cree)).GetProperty("jeton").GetString();
        var (ailleurs, adminAilleurs) = await OuvrirAsync("Autre paroisse", "admin@ailleurs.test");
        await InviterAsync(ailleurs, adminAilleurs, "marc@us31.test", await CreerSiteAsync(ailleurs, adminAilleurs, "Temple")); // invitation en attente

        using var reponse = await Post("/auth/reset-password", null, new { jeton, motDePasse = NouveauMotDePasse });
        using var ancien = await Post("/auth/login", null, new { email = "marc@us31.test", motDePasse = MotDePasse });

        Assert.Equal(HttpStatusCode.Gone, reponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ancien.StatusCode); // rien n'a changé
    }

    [Fact]
    public async Task ResetLinksAndOpenAssignmentsAreReservedToTheAdministratorOfTheOrganisation()
    {
        var marc = await MembreActifAsync("marc@us31.test", new { role = "Technicien", siteId = (Guid?)_nord });
        var technicien = await ConnecterAsync("marc@us31.test");
        var compteMarc = (await MembreAsync("marc@us31.test")).CompteId;
        var (ailleurs, adminAilleurs) = await OuvrirAsync("Autre paroisse", "admin@ailleurs.test");

        using var parTechnicien = await Post($"/organizations/{_organisation}/members/{marc}/password-reset", technicien, new { });
        using var parAutreOrganisation = await Post($"/organizations/{_organisation}/members/{marc}/password-reset", adminAilleurs, new { });
        using var membreEtranger = await Post($"/organizations/{ailleurs}/members/{marc}/password-reset", adminAilleurs, new { });
        using var affectationsTechnicien = await Get($"/organizations/{_organisation}/open-assignments/{compteMarc}", technicien);
        using var affectationsAilleurs = await Get($"/organizations/{_organisation}/open-assignments/{compteMarc}", adminAilleurs);
        using var affectationsAdmin = await Get($"/organizations/{_organisation}/open-assignments/{compteMarc}", _admin);
        using var journalAilleurs = await Get($"/organizations/{ailleurs}/members/journal?membreId={marc}", adminAilleurs);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, parAutreOrganisation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, membreEtranger.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, affectationsTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, affectationsAilleurs.StatusCode);
        Assert.Equal(0, (await Json(affectationsAdmin)).GetArrayLength());
        Assert.Equal(0, (await Json(journalAilleurs)).GetArrayLength()); // le journal d'une autre organisation reste invisible
    }

    [Fact]
    public async Task TheInviteeSeesTheOrganisationTheRolesBySiteWhoInvitesAndWhen()
    {
        var (ailleurs, adminAilleurs) = await OuvrirAsync("Autre paroisse", "admin@ailleurs.test");
        var temple = await CreerSiteAsync(ailleurs, adminAilleurs, "Temple");
        var avant = DateTimeOffset.UtcNow.AddMinutes(-1);
        using (await Post($"/organizations/{ailleurs}/members", adminAilleurs, new
        {
            nom = "Admin",
            email = AdminEmail,
            roles = new[] { new { role = "Technicien", siteId = (Guid?)temple }, new { role = "Pasteur", siteId = (Guid?)temple } },
        }))
        {
        }

        using var me = await Get("/me", _admin);
        var invitation = (await Json(me)).GetProperty("invitations").EnumerateArray().Single();

        Assert.Equal("Autre paroisse", invitation.GetProperty("organisation").GetString());
        Assert.Equal("Admin Autre paroisse", invitation.GetProperty("invitePar").GetString());
        Assert.True(invitation.GetProperty("inviteLe").GetDateTimeOffset() > avant);
        Assert.Equal(["Pasteur · Temple", "Technicien · Temple"], invitation.GetProperty("roles").EnumerateArray()
            .Select(r => $"{r.GetProperty("role").GetString()} · {r.GetProperty("site").GetString()}"));
    }

    private async Task<(Guid Organisation, string JetonAdmin)> OuvrirAsync(string nom, string email)
    {
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, nom, email, $"Admin {nom}", CancellationToken.None);
        using var activation = await Post("/auth/activate", null, new { jeton = ouverte.JetonActivation, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        return (ouverte.OrganisationId, await ConnecterAsync(email));
    }

    private async Task<string> ConnecterAsync(string email)
    {
        using var reponse = await Post("/auth/login", null, new { email, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await Json(reponse)).GetProperty("jetonAcces").GetString()!;
    }

    private async Task<Guid> CreerSiteAsync(Guid organisation, string jeton, string nom)
    {
        using var reponse = await Post($"/organizations/{organisation}/sites", jeton, new { nom });
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await Json(reponse)).GetProperty("id").GetGuid();
    }

    // Membre créé et activé dans l'organisation (par défaut : Saint-Paul) ; rend l'identifiant de l'adhésion.
    private async Task<Guid> MembreActifAsync(string email, object role, Guid? organisation = null, string? jeton = null)
    {
        using var cree = await Post($"/organizations/{organisation ?? _organisation}/members", jeton ?? _admin, new { nom = "Marc D.", email, roles = new[] { role } });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        var corps = await Json(cree);
        using var activation = await Post("/auth/activate", null, new { jeton = corps.GetProperty("activation").GetProperty("jeton").GetString(), motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        return corps.GetProperty("membreId").GetGuid();
    }

    private async Task<Guid> InviterAsync(Guid organisation, string jeton, string email, Guid site)
    {
        using var cree = await Post($"/organizations/{organisation}/members", jeton, new { nom = "Invité", email, roles = new[] { new { role = "Technicien", siteId = (Guid?)site } } });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        return (await Json(cree)).GetProperty("membreId").GetGuid();
    }

    private async Task<(Guid Id, Guid CompteId, int Version)> MembreAsync(string email, Guid? organisation = null)
    {
        using var liste = await Get($"/organizations/{organisation ?? _organisation}/members", organisation is null || organisation == _organisation ? _admin : await ConnecterAsync("admin@ailleurs.test"));
        var membre = (await Json(liste)).EnumerateArray().Single(m => m.GetProperty("email").GetString() == email);
        return (membre.GetProperty("membreId").GetGuid(), membre.GetProperty("compteId").GetGuid(), membre.GetProperty("version").GetInt32());
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage reponse) =>
        (await Json(reponse)).GetProperty("code").GetString();

    private static async Task<JsonElement> Json(HttpResponseMessage reponse) =>
        JsonDocument.Parse(await reponse.Content.ReadAsStringAsync()).RootElement.Clone();

    private Task<HttpResponseMessage> Get(string chemin, string? jeton) => Envoyer(HttpMethod.Get, chemin, jeton, null);

    private Task<HttpResponseMessage> Post(string chemin, string? jeton, object corps) => Envoyer(HttpMethod.Post, chemin, jeton, corps);

    private Task<HttpResponseMessage> Put(string chemin, string? jeton, object corps) => Envoyer(HttpMethod.Put, chemin, jeton, corps);

    private Task<HttpResponseMessage> Delete(string chemin, string? jeton, string motif) => Envoyer(HttpMethod.Delete, chemin, jeton, new { motif });

    private async Task<HttpResponseMessage> Envoyer(HttpMethod methode, string chemin, string? jeton, object? corps)
    {
        using var demande = new HttpRequestMessage(methode, new Uri(chemin, UriKind.Relative));
        if (jeton is not null)
        {
            demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        }

        if (corps is not null)
        {
            demande.Content = JsonContent.Create(corps);
        }

        return await _client.SendAsync(demande);
    }
}

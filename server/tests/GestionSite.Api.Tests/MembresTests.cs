using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GestionSite.Modules.Identite;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GestionSite.Api.Tests;

/// <summary>Ouverture d'organisation, activation et gestion des membres (S06-04) sur l'API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed class MembresTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string AdminEmail = "admin@saint-paul.test";
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private Guid _organisation;
    private string _admin = null!;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        _client = _api.CreateClient();
        (_organisation, _admin) = await OuvrirAsync("Saint-Paul", AdminEmail);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ActivationLinkIsSingleUse()
    {
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, "Autre", "autre@exemple.test", "Autre Admin", CancellationToken.None);

        using var court = await Post("/auth/activate", null, new { jeton = ouverte.JetonActivation, motDePasse = "court" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, court.StatusCode); // le lien reste utilisable
        using var premier = await Post("/auth/activate", null, new { jeton = ouverte.JetonActivation, motDePasse = MotDePasse });
        using var second = await Post("/auth/activate", null, new { jeton = ouverte.JetonActivation, motDePasse = MotDePasse });
        using var inconnu = await Post("/auth/activate", null, new { jeton = "AAAA", motDePasse = MotDePasse });

        Assert.Equal(HttpStatusCode.NoContent, premier.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, inconnu.StatusCode);
    }

    [Fact]
    public async Task ExpiredActivationLinkIsRefused()
    {
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, "Expirée", "expire@exemple.test", "Admin", CancellationToken.None);
        await _database.CountAsync("WITH maj AS (UPDATE identite.activations SET expire_le = now() - interval '1 minute' RETURNING 1) SELECT count(*) FROM maj");

        using var reponse = await Post("/auth/activate", null, new { jeton = ouverte.JetonActivation, motDePasse = MotDePasse });

        Assert.Equal(HttpStatusCode.Gone, reponse.StatusCode);
    }

    [Fact]
    public async Task AdministratorCreatesAMemberWhoSeesOnlyTheirSite()
    {
        var nord = await CreerSiteAsync("Nord");
        await CreerSiteAsync("Sud");

        var cree = await CreerMembreAsync("marc@exemple.test", new { role = "Technicien", siteId = nord });
        await ActiverAsync(cree.GetProperty("activation").GetProperty("jeton").GetString()!);
        var technicien = await ConnecterAsync("marc@exemple.test");

        using var sites = await Get($"/organizations/{_organisation}/sites", technicien);
        Assert.Equal([nord], (await Json(sites)).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task OnlyTheAdministratorManagesMembersAndOtherOrganisationsAreInvisible()
    {
        var nord = await CreerSiteAsync("Nord");
        var cree = await CreerMembreAsync("marc@exemple.test", new { role = "Technicien", siteId = nord });
        await ActiverAsync(cree.GetProperty("activation").GetProperty("jeton").GetString()!);
        var technicien = await ConnecterAsync("marc@exemple.test");
        var (autre, _) = await OuvrirAsync("Autre organisation", "admin@autre.test");

        using var parTechnicien = await Post($"/organizations/{_organisation}/members", technicien, NouveauMembre("x@exemple.test", new { role = "Pasteur", siteId = nord }));
        using var chezAutre = await Get($"/organizations/{autre}/sites", _admin);
        using var membreAilleurs = await Post($"/organizations/{autre}/members", _admin, NouveauMembre("y@exemple.test", new { role = "Administrateur" }));
        using var sansJeton = await Get($"/organizations/{_organisation}/sites", jeton: null);

        Assert.Equal(HttpStatusCode.Forbidden, parTechnicien.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, chezAutre.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, membreAilleurs.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sansJeton.StatusCode);
    }

    [Fact]
    public async Task RevisionRequiresTheCurrentVersion()
    {
        var nord = await CreerSiteAsync("Nord");
        var cree = await CreerMembreAsync("marc@exemple.test", new { role = "Technicien", siteId = nord });
        var membre = cree.GetProperty("membreId").GetGuid();
        var version = cree.GetProperty("version").GetInt32();

        using var revise = await Put($"/organizations/{_organisation}/members/{membre}", _admin,
            new { roles = new object[] { new { role = "Responsable", siteId = nord } }, version, motif = "Changement de rôle" });
        using var perime = await Put($"/organizations/{_organisation}/members/{membre}", _admin,
            new { roles = new object[] { new { role = "Pasteur", siteId = nord } }, version, motif = "Changement de rôle" });

        Assert.Equal(HttpStatusCode.OK, revise.StatusCode);
        Assert.Equal(version + 1, (await Json(revise)).GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, perime.StatusCode);
    }

    [Fact]
    public async Task RevocationEndsAccessToTheOrganisationImmediately()
    {
        var nord = await CreerSiteAsync("Nord");
        var cree = await CreerMembreAsync("marc@exemple.test", new { role = "Technicien", siteId = nord });
        await ActiverAsync(cree.GetProperty("activation").GetProperty("jeton").GetString()!);
        using var connexion = await Post("/auth/login", null, new { email = "marc@exemple.test", motDePasse = MotDePasse });
        var jetons = await Json(connexion);

        using var revocation = await Delete($"/organizations/{_organisation}/members/{cree.GetProperty("membreId").GetGuid()}", _admin);
        using var sites = await Get($"/organizations/{_organisation}/sites", jetons.GetProperty("jetonAcces").GetString());
        using var me = await Get("/me", jetons.GetProperty("jetonAcces").GetString());

        Assert.Equal(HttpStatusCode.NoContent, revocation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sites.StatusCode); // jeton d'accès encore valide, adhésion révoquée
        Assert.Equal(0, (await Json(me)).GetProperty("organisations").GetArrayLength());
    }

    [Fact]
    public async Task TheLastAdministratorCannotBeRemoved()
    {
        using var me = await Get("/me", _admin);
        var moi = (await Json(me)).GetProperty("organisations")[0];
        var membres = await _database.CountAsync("SELECT count(*) FROM identite.membres WHERE organisation_id = @org", ("org", _organisation));
        Assert.Equal(1L, membres);
        var membreAdmin = await MembreDuCompteAsync(AdminEmail);

        using var revocation = await Delete($"/organizations/{_organisation}/members/{membreAdmin}", _admin);
        using var retrait = await Put($"/organizations/{_organisation}/members/{membreAdmin}", _admin,
            new { roles = new object[] { new { role = "Pasteur", siteId = await CreerSiteAsync("Nord") } }, version = 1, motif = "Changement de rôle" });

        Assert.Equal("Saint-Paul", moi.GetProperty("nom").GetString());
        Assert.Equal(HttpStatusCode.Conflict, revocation.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retrait.StatusCode);
    }

    [Theory]
    [InlineData("Inconnu", false, "role_inconnu")]
    [InlineData("0", false, "role_inconnu")]
    [InlineData("Responsable, Delegue", true, "role_inconnu")]
    [InlineData("Technicien", false, "site_selon_role")]
    [InlineData("Administrateur", true, "site_selon_role")]
    [InlineData("Technicien", true, "site_inconnu")]
    public async Task InvalidRolesAreRefused(string role, bool avecSiteEtranger, string code)
    {
        Guid? site = avecSiteEtranger ? Guid.NewGuid() : null;

        using var reponse = await Post($"/organizations/{_organisation}/members", _admin, NouveauMembre("z@exemple.test", new { role, siteId = site }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        Assert.Equal(code, (await Json(reponse)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnExistingAccountGetsRightsOnlyAfterAcceptingTheInvitation()
    {
        var (autre, adminAutre) = await OuvrirAsync("Autre organisation", "admin@autre.test");

        using var reponse = await Post($"/organizations/{autre}/members", adminAutre, NouveauMembre(AdminEmail, new { role = "Administrateur" }));
        var invitation = (await Json(reponse)).GetProperty("membreId").GetGuid();
        using var avant = await Get($"/organizations/{autre}/sites", _admin);
        using var meAvant = await Get("/me", _admin);
        using var acceptation = await Post($"/me/invitations/{invitation}/accept", _admin, new { });
        using var apres = await Get($"/organizations/{autre}/sites", _admin);
        using var meApres = await Get("/me", _admin);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(reponse)).GetProperty("activation").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, avant.StatusCode); // aucun droit avant acceptation
        Assert.Equal([invitation], (await Json(meAvant)).GetProperty("invitations").EnumerateArray().Select(i => i.GetProperty("membreId").GetGuid()));
        Assert.Equal(1, (await Json(meAvant)).GetProperty("organisations").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, acceptation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apres.StatusCode);
        Assert.Equal(2, (await Json(meApres)).GetProperty("organisations").GetArrayLength());
        Assert.Equal(0, (await Json(meApres)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task APendingAdministratorInvitationDoesNotCountAsAnAdministrator()
    {
        await OuvrirAsync("Autre organisation", "admin@autre.test"); // compte existant, donc simple invitation
        using var invitation = await Post($"/organizations/{_organisation}/members", _admin, NouveauMembre("admin@autre.test", new { role = "Administrateur" }));
        var moi = await MembreDuCompteAsync(AdminEmail);

        using var depart = await Delete($"/organizations/{_organisation}/members/{moi}", _admin);

        Assert.Equal(HttpStatusCode.Conflict, depart.StatusCode);
        Assert.Equal(HttpStatusCode.Created, invitation.StatusCode);
        Assert.Equal("dernier_administrateur", (await Json(depart)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnInvitationCanBeDeclinedAndOnlyByItsHolder()
    {
        var (autre, adminAutre) = await OuvrirAsync("Autre organisation", "admin@autre.test");
        using var reponse = await Post($"/organizations/{autre}/members", adminAutre, NouveauMembre(AdminEmail, new { role = "Administrateur" }));
        var invitation = (await Json(reponse)).GetProperty("membreId").GetGuid();

        using var parAutrui = await Post($"/me/invitations/{invitation}/accept", adminAutre, new { });
        using var refus = await Post($"/me/invitations/{invitation}/decline", _admin, new { });
        using var acceptationApres = await Post($"/me/invitations/{invitation}/accept", _admin, new { });

        Assert.Equal(HttpStatusCode.NotFound, parAutrui.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, refus.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, acceptationApres.StatusCode);
    }

    [Fact]
    public async Task AnotherOrganisationCannotTakeOverAPendingAccount()
    {
        var nord = await CreerSiteAsync("Nord");
        var cree = await CreerMembreAsync("bob@exemple.test", new { role = "Responsable", siteId = nord });
        var (autre, adminAutre) = await OuvrirAsync("Organisation tierce", "admin@tierce.test");

        using var prise = await Post($"/organizations/{autre}/members", adminAutre, NouveauMembre("bob@exemple.test", new { role = "Administrateur" }));
        using var relance = await Post($"/organizations/{autre}/members/{cree.GetProperty("membreId").GetGuid()}/activation", adminAutre, new { });

        Assert.Equal(HttpStatusCode.Conflict, prise.StatusCode);
        Assert.Equal("compte_en_attente", (await Json(prise)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, relance.StatusCode);
        await ActiverAsync(cree.GetProperty("activation").GetProperty("jeton").GetString()!); // le lien d'origine reste valable
    }

    [Fact]
    public async Task TheCreatingOrganisationCanIssueANewLink()
    {
        var nord = await CreerSiteAsync("Nord");
        var cree = await CreerMembreAsync("marc@exemple.test", new { role = "Technicien", siteId = nord });
        var ancien = cree.GetProperty("activation").GetProperty("jeton").GetString()!;

        using var relance = await Post($"/organizations/{_organisation}/members/{cree.GetProperty("membreId").GetGuid()}/activation", _admin, new { });
        var nouveau = (await Json(relance)).GetProperty("jeton").GetString()!;
        using var parAncien = await Post("/auth/activate", null, new { jeton = ancien, motDePasse = MotDePasse });
        await ActiverAsync(nouveau);
        using var apresActivation = await Post($"/organizations/{_organisation}/members/{cree.GetProperty("membreId").GetGuid()}/activation", _admin, new { });

        Assert.Equal(HttpStatusCode.OK, relance.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, parAncien.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, apresActivation.StatusCode);
    }

    [Fact]
    public async Task RevocationKeepsSessionsUsedByOtherOrganisations()
    {
        var (autre, adminAutre) = await OuvrirAsync("Autre organisation", "admin@autre.test");
        var nord = await CreerSiteAsync("Nord");
        using var ajout = await Post($"/organizations/{_organisation}/members", _admin, NouveauMembre("admin@autre.test", new { role = "Technicien", siteId = nord }));
        var membre = (await Json(ajout)).GetProperty("membreId").GetGuid();
        using var acceptation = await Post($"/me/invitations/{membre}/accept", adminAutre, new { });
        Assert.Equal(HttpStatusCode.NoContent, acceptation.StatusCode); // adhésion effective avant sa révocation
        using var connexion = await Post("/auth/login", null, new { email = "admin@autre.test", motDePasse = MotDePasse });
        var renouvellement = (await Json(connexion)).GetProperty("jetonRenouvellement").GetString();

        using var revocation = await Delete($"/organizations/{_organisation}/members/{membre}", _admin);
        using var apres = await Post("/auth/refresh", null, new { jetonRenouvellement = renouvellement });
        using var sitesAutre = await Get($"/organizations/{autre}/sites", adminAutre);

        Assert.Equal(HttpStatusCode.NoContent, revocation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apres.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sitesAutre.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ajout.StatusCode);
    }

    [Fact]
    public async Task EmailIsTrimmedBeforeLookingForTheAccount()
    {
        var (autre, adminAutre) = await OuvrirAsync("Autre organisation", "admin@autre.test");

        using var reponse = await Post($"/organizations/{autre}/members", adminAutre, NouveauMembre($"  {AdminEmail} ", new { role = "Administrateur" }));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }

    [Fact]
    public async Task AMemberCannotBeAddedTwice()
    {
        var nord = await CreerSiteAsync("Nord");
        await CreerMembreAsync("marc@exemple.test", new { role = "Technicien", siteId = nord });

        using var double_ = await Post($"/organizations/{_organisation}/members", _admin, NouveauMembre("marc@exemple.test", new { role = "Pasteur", siteId = nord }));

        Assert.Equal(HttpStatusCode.Conflict, double_.StatusCode);
    }

    private async Task<(Guid Organisation, string JetonAdmin)> OuvrirAsync(string nom, string email)
    {
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, nom, email, "Admin", CancellationToken.None);
        await ActiverAsync(ouverte.JetonActivation!);
        return (ouverte.OrganisationId, await ConnecterAsync(email));
    }

    private async Task ActiverAsync(string jeton)
    {
        using var reponse = await Post("/auth/activate", null, new { jeton, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<string> ConnecterAsync(string email)
    {
        using var reponse = await Post("/auth/login", null, new { email, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await Json(reponse)).GetProperty("jetonAcces").GetString()!;
    }

    private async Task<Guid> CreerSiteAsync(string nom)
    {
        using var reponse = await Post($"/organizations/{_organisation}/sites", _admin, new { nom });
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await Json(reponse)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreerMembreAsync(string email, object role)
    {
        using var reponse = await Post($"/organizations/{_organisation}/members", _admin, NouveauMembre(email, role));
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return await Json(reponse);
    }

    private async Task<Guid> MembreDuCompteAsync(string email)
    {
        await using var connection = new Npgsql.NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT m.id FROM identite.membres m JOIN identite.comptes c ON c.id = m.compte_id WHERE c.email = @email AND m.organisation_id = @org", connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("org", _organisation);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static object NouveauMembre(string email, object role) => new { nom = "Marc D.", email, roles = new[] { role } };

    private static async Task<JsonElement> Json(HttpResponseMessage reponse) =>
        JsonDocument.Parse(await reponse.Content.ReadAsStringAsync()).RootElement.Clone();

    private Task<HttpResponseMessage> Get(string chemin, string? jeton) => Envoyer(HttpMethod.Get, chemin, jeton, null);

    private Task<HttpResponseMessage> Post(string chemin, string? jeton, object corps) => Envoyer(HttpMethod.Post, chemin, jeton, corps);

    private Task<HttpResponseMessage> Put(string chemin, string? jeton, object corps) => Envoyer(HttpMethod.Put, chemin, jeton, corps);

    private Task<HttpResponseMessage> Delete(string chemin, string? jeton) => Envoyer(HttpMethod.Delete, chemin, jeton, new { motif = "Départ" });

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

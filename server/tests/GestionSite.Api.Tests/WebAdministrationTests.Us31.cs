using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite;

namespace GestionSite.Api.Tests;

/// <summary>
/// Écrans d'administration autonome (US-31, maquettes validées le 03/11) de bout en bout : site en deux étapes, panneau du
/// membre (rôles, droit « plans », accès), journal, lien de réinitialisation, carte d'invitation et sélecteur d'organisation.
/// </summary>
public sealed partial class WebAdministrationTests
{
    private string PageMembres => $"/organisations/{_ouverte.OrganisationId}/membres";

    [Fact]
    public async Task ASiteIsCreatedInTwoStepsAndADuplicateNameIsExplained()
    {
        using var admin = await AdministrateurConnecteAsync();
        var sites = $"/organisations/{_ouverte.OrganisationId}/sites";

        var liste = await LirePageAsync(admin, sites);
        var double_ = await LireAsync(await PosterAsync(admin, sites + "?ajouter=1", "site", ("SaisieSite.Nom", "site CENTRE")));
        var vide = await LireAsync(await PosterAsync(admin, sites + "?ajouter=1", "site", ("SaisieSite.Nom", " ")));
        using var cree = await PosterAsync(admin, sites + "?ajouter=1", "site", ("SaisieSite.Nom", "Chapelle Saint-Roch"));
        var etape2 = await LirePageAsync(admin, Chemin(cree));

        Assert.Contains("Ajouter un site", liste, StringComparison.Ordinal);
        Assert.Contains("Un site porte déjà ce nom dans l'organisation.", double_, StringComparison.Ordinal);
        Assert.Contains("Saisissez le nom du site.", vide, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, cree.StatusCode);
        Assert.Contains("Chapelle Saint-Roch est créé", etape2, StringComparison.Ordinal);
        Assert.Contains("À faire ensuite", etape2, StringComparison.Ordinal);
        Assert.Contains("Ouvrir Membres", etape2, StringComparison.Ordinal);
        Assert.Matches("/s/[A-Z0-9]{10}", etape2); // lien public créé avec le site
    }

    [Fact]
    public async Task TheMemberPanelGrantsThePlansRightThenRevisesRolesWithAReason()
    {
        using var admin = await AdministrateurConnecteAsync();
        var (luc, site) = await MembreActifParLeWebAsync(admin, "luc@web.test", "Luc B.");
        var panneau = $"{PageMembres}?membre={luc}";

        var roles = await LirePageAsync(admin, panneau);
        var accorder = await LirePageAsync(admin, $"{panneau}&onglet=plans&site={site}");
        using var accorde = await PosterAsync(admin, $"{panneau}&onglet=plans&site={site}", "plans",
            ("Plans.SiteId", site), ("Plans.Retirer", "false"), ("Plans.Motif", "Relevé des plans du sous-sol"));
        var plans = await LirePageAsync(admin, Chemin(accorde).Split('#')[0]);
        var version = Version().Match(await LirePageAsync(admin, panneau)).Groups[1].Value;
        var sansMotif = await LireAsync(await PosterAsync(admin, panneau, "roles",
            ("Roles.Version", version), ("Roles.Sites[0].SiteId", site), ("Roles.Sites[0].Valeur", "Responsable"), ("Roles.Motif", "")));
        using var revise = await PosterAsync(admin, panneau, "roles",
            ("Roles.Version", version), ("Roles.Sites[0].SiteId", site), ("Roles.Sites[0].Valeur", "Responsable"), ("Roles.Motif", "Luc prend\nla responsabilité"));
        var apres = await LirePageAsync(admin, Chemin(revise).Split('#')[0]);
        using var perime = await PosterAsync(admin, panneau, "roles",
            ("Roles.Version", version), ("Roles.Sites[0].SiteId", site), ("Roles.Sites[0].Valeur", "Pasteur"), ("Roles.Motif", "Autre changement"));
        var conflit = await LireAsync(perime);

        Assert.Contains("Enregistrer les rôles", roles, StringComparison.Ordinal);
        Assert.Contains("Accorder sur Site centre", accorder, StringComparison.Ordinal);
        Assert.Contains("Droit « modifier les plans » accordé à Luc B.", plans, StringComparison.Ordinal);
        Assert.Contains("accordé le", plans, StringComparison.Ordinal);
        Assert.Contains("+ plans", plans, StringComparison.Ordinal);
        Assert.Contains("Indiquez le motif : il est inscrit au journal des habilitations.", sansMotif, StringComparison.Ordinal);
        Assert.Contains("Accès de Luc B. enregistrés.", apres, StringComparison.Ordinal);
        Assert.Contains(">Responsable", apres, StringComparison.Ordinal);
        Assert.Contains("ont changé depuis l'ouverture de cette page", conflit, StringComparison.Ordinal);
        Assert.Contains("Autre changement", conflit, StringComparison.Ordinal); // motif conservé
        var journal = await LirePageAsync(admin, $"{PageMembres}?vue=journal");
        Assert.Contains("Rôles modifiés · Luc B.", journal, StringComparison.Ordinal);
        Assert.Contains("« Luc prend la responsabilité »", journal, StringComparison.Ordinal); // une ligne au journal
        Assert.Contains("Droit « plans » accordé · Luc B.", journal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdministratorRevokesAnotherMemberButNotThemselves()
    {
        using var admin = await AdministrateurConnecteAsync();
        var (luc, _) = await MembreActifParLeWebAsync(admin, "luc@web.test", "Luc B.");
        var moi = MembreLien("Claire M.").Match(await LirePageAsync(admin, PageMembres)).Groups[1].Value;

        var propreAcces = await LirePageAsync(admin, $"{PageMembres}?membre={moi}&onglet=acces&confirmer=1");
        var confirmation = await LirePageAsync(admin, $"{PageMembres}?membre={luc}&onglet=acces&confirmer=1");
        using var revoque = await PosterAsync(admin, $"{PageMembres}?membre={luc}&onglet=acces&confirmer=1", "revocation", ("Revocation.Motif", "Départ de la paroisse"));
        var liste = await LirePageAsync(admin, Chemin(revoque));

        Assert.Contains("Vous ne pouvez pas révoquer votre propre accès", propreAcces, StringComparison.Ordinal);
        Assert.DoesNotContain("Révoquer l'accès de Claire M.", propreAcces, StringComparison.Ordinal);
        Assert.Contains("Révoquer l'accès de Luc B. ?", confirmation, StringComparison.Ordinal);
        Assert.Contains("Accès révoqué.", liste, StringComparison.Ordinal);
        Assert.DoesNotContain("luc@web.test", liste, StringComparison.Ordinal);
        Assert.Contains("« Départ de la paroisse »", await LirePageAsync(admin, $"{PageMembres}?vue=journal"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResetLinkFromThePanelLetsTheMemberChooseANewPassword()
    {
        using var admin = await AdministrateurConnecteAsync();
        var (luc, _) = await MembreActifParLeWebAsync(admin, "luc@web.test", "Luc B.");

        var acces = await LireAsync(await PosterAsync(admin, $"{PageMembres}?membre={luc}&onglet=acces", "reinitialisation"));
        var lien = Lien().Match(acces).Groups[1].Value;
        using var luc2 = Navigateur();
        var jeton = Uri.UnescapeDataString(lien[(lien.IndexOf("jeton=", StringComparison.Ordinal) + 6)..]);
        var court = await LireAsync(await PosterAsync(luc2, new Uri(lien).PathAndQuery, "reinitialisation",
            ("Saisie.Jeton", jeton), ("Saisie.MotDePasse", "court"), ("Saisie.Confirmation", "court")));
        using var reinitialise = await PosterAsync(luc2, new Uri(lien).PathAndQuery, "reinitialisation",
            ("Saisie.Jeton", jeton), ("Saisie.MotDePasse", "une-autre-phrase-solide"), ("Saisie.Confirmation", "une-autre-phrase-solide"));
        var reutilise = await LireAsync(await PosterAsync(Navigateur(), new Uri(lien).PathAndQuery, "reinitialisation",
            ("Saisie.Jeton", jeton), ("Saisie.MotDePasse", "une-autre-phrase-solide"), ("Saisie.Confirmation", "une-autre-phrase-solide")));
        var ancien = await LireAsync(await PosterAsync(Navigateur(), "/connexion", "connexion", ("Saisie.Email", "luc@web.test"), ("Saisie.MotDePasse", MotDePasse)));

        Assert.Contains("/reinitialisation?jeton=", lien, StringComparison.Ordinal);
        Assert.Contains("vaut 1 heure", acces, StringComparison.Ordinal);
        Assert.Contains("Le mot de passe doit contenir au moins 12 caractères.", court, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, reinitialise.StatusCode);
        Assert.Equal("/", Chemin(reinitialise));
        Assert.Contains("Lien expiré ou déjà utilisé.", reutilise, StringComparison.Ordinal);
        Assert.Contains("E-mail ou mot de passe incorrect.", ancien, StringComparison.Ordinal);
        Assert.Contains("Lien de réinitialisation créé", await LirePageAsync(admin, $"{PageMembres}?membre={luc}&onglet=acces"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APendingInvitationGetsANewActivationLinkAndTheForgottenPasswordPageSaysEmailIsNotLive()
    {
        using var admin = await AdministrateurConnecteAsync();
        var etape2 = await LireAsync(await PosterAsync(admin, PageMembres + "?inviter=1", "invitation",
            ("Saisie.Etape", "1"), ("Saisie.Nom", "Hugo B."), ("Saisie.Email", "hugo@web.test")));
        await PosterAsync(admin, PageMembres, "invitation", ("Saisie.Etape", "2"), ("Saisie.Nom", "Hugo B."), ("Saisie.Email", "hugo@web.test"),
            ("Saisie.Roles[0].SiteId", SiteId().Match(etape2).Groups[1].Value), ("Saisie.Roles[0].Role", "Technicien"));
        var hugo = MembreLien("Hugo B.").Match(await LirePageAsync(admin, PageMembres)).Groups[1].Value;

        var acces = await LireAsync(await PosterAsync(admin, $"{PageMembres}?membre={hugo}&onglet=acces", "activation"));
        using var anonyme = Navigateur();
        var oublie = await LirePageAsync(anonyme, "/mot-de-passe-oublie");
        var connexion = await LirePageAsync(anonyme, "/connexion");

        Assert.Contains("Nouveau lien d'activation pour Hugo B.", acces, StringComparison.Ordinal);
        Assert.Contains("/activation?jeton=", Lien().Match(acces).Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("Annuler l'invitation…", acces, StringComparison.Ordinal);
        Assert.Contains("La réinitialisation par e-mail n'est pas encore en service.", oublie, StringComparison.Ordinal);
        Assert.Contains("href=\"/mot-de-passe-oublie\"", connexion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvitationCardAppearsOnTopAndTheOrganisationSelectorFollowsItsAcceptance()
    {
        using var admin = await AdministrateurConnecteAsync();
        var (autre, invitation) = await InviterDansUneAutreOrganisationAsync(AdminEmail);

        var membres = await LirePageAsync(admin, PageMembres);
        using var accepte = await PosterAsync(admin, PageMembres, formName: null, action: $"/invitations/{invitation}/accepter");
        var accueil = await LirePageAsync(admin, Chemin(accepte));
        var apres = await LirePageAsync(admin, PageMembres);
        using var bascule = await admin.GetAsync(new Uri($"/?organisation={autre}", UriKind.Relative));

        Assert.Contains("Autre paroisse vous invite", membres, StringComparison.Ordinal);
        Assert.Contains("Admin Deux vous propose : ", membres, StringComparison.Ordinal);
        Assert.Contains("Technicien sur Temple", membres, StringComparison.Ordinal);
        Assert.Contains("vous gardez votre accès à Église Saint-Paul", membres, StringComparison.Ordinal);
        Assert.Contains("Vous avez rejoint Autre paroisse.", accueil, StringComparison.Ordinal);
        Assert.Contains("Ouvrir Autre paroisse", accueil, StringComparison.Ordinal);
        Assert.DoesNotContain("vous invite", apres, StringComparison.Ordinal);
        Assert.Contains($"href=\"/?organisation={autre}\"", apres, StringComparison.Ordinal); // sélecteur d'organisation
        Assert.Equal($"/organisations/{autre}/dossiers", Chemin(bascule));
    }

    [Fact]
    public async Task ARefusedInvitationIsGoneAndAnAccountWithoutOrganisationSeesItsInvitations()
    {
        using var admin = await AdministrateurConnecteAsync();
        var (_, invitation) = await InviterDansUneAutreOrganisationAsync(AdminEmail);
        using var refuse = await PosterAsync(admin, PageMembres, formName: null, action: $"/invitations/{invitation}/refuser");
        var accueil = await LirePageAsync(admin, Chemin(refuse));
        using var encore = await PosterAsync(admin, PageMembres, formName: null, action: $"/invitations/{invitation}/accepter");
        var plusValable = await LirePageAsync(admin, Chemin(encore));

        // Compte sans organisation (révoqué ici), invité ailleurs : l'accueil montre ses invitations.
        var (luc, _) = await MembreActifParLeWebAsync(admin, "luc@web.test", "Luc B.");
        await PosterAsync(admin, $"{PageMembres}?membre={luc}&onglet=acces&confirmer=1", "revocation", ("Revocation.Motif", "Départ"));
        await InviterDansUneAutreOrganisationAsync("luc@web.test", nomOrganisation: "Troisième paroisse", adminEmail: "admin3@web.test");
        using var navigateurLuc = Navigateur();
        await PosterAsync(navigateurLuc, "/connexion", "connexion", ("Saisie.Email", "luc@web.test"), ("Saisie.MotDePasse", MotDePasse));
        var sansOrganisation = await LirePageAsync(navigateurLuc, "/");

        Assert.Contains("Invitation refusée.", accueil, StringComparison.Ordinal);
        Assert.Contains("Cette invitation n'est plus valable.", plusValable, StringComparison.Ordinal);
        Assert.Contains("Vous n'avez encore accès à aucune organisation.", sansOrganisation, StringComparison.Ordinal);
        Assert.Contains("Troisième paroisse vous invite", sansOrganisation, StringComparison.Ordinal);
    }

    // Membre invité par l'écran (technicien sur « Site centre ») puis activé par son lien ; rend son adhésion et le site.
    private async Task<(string Membre, string Site)> MembreActifParLeWebAsync(HttpClient admin, string email, string nom)
    {
        var etape2 = await LireAsync(await PosterAsync(admin, PageMembres + "?inviter=1", "invitation",
            ("Saisie.Etape", "1"), ("Saisie.Nom", nom), ("Saisie.Email", email)));
        var site = SiteId().Match(etape2).Groups[1].Value;
        var etape3 = await LireAsync(await PosterAsync(admin, PageMembres, "invitation", ("Saisie.Etape", "2"), ("Saisie.Nom", nom),
            ("Saisie.Email", email), ("Saisie.Roles[0].SiteId", site), ("Saisie.Roles[0].Role", "Technicien")));
        var lien = Lien().Match(etape3).Groups[1].Value;
        var jeton = Uri.UnescapeDataString(lien[(lien.IndexOf("jeton=", StringComparison.Ordinal) + 6)..]);
        using var api = _api.CreateClient();
        using var activation = await api.PostAsJsonAsync("/auth/activate", new { jeton, motDePasse = MotDePasse });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        return (MembreLien(nom).Match(await LirePageAsync(admin, PageMembres)).Groups[1].Value, site);
    }

    // Autre organisation (site « Temple ») dont l'administrateur invite un compte existant comme technicien ; rend l'organisation
    // et l'identifiant de l'invitation.
    private async Task<(Guid Organisation, Guid Invitation)> InviterDansUneAutreOrganisationAsync(
        string email, string nomOrganisation = "Autre paroisse", string adminEmail = "admin2@web.test")
    {
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(_api.Services, nomOrganisation, adminEmail, "Admin Deux", CancellationToken.None, premierSite: "Temple");
        using var api = _api.CreateClient();
        using (await api.PostAsJsonAsync("/auth/activate", new { jeton = ouverte.JetonActivation, motDePasse = MotDePasse }))
        {
        }

        using var connexion = await api.PostAsJsonAsync("/auth/login", new { email = adminEmail, motDePasse = MotDePasse });
        var jeton = (await connexion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jetonAcces").GetString();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        var temple = (await api.GetFromJsonAsync<JsonElement>($"/organizations/{ouverte.OrganisationId}/sites"))[0].GetProperty("id").GetGuid();
        using var cree = await api.PostAsJsonAsync($"/organizations/{ouverte.OrganisationId}/members",
            new { nom = "Invité", email, roles = new[] { new { role = "Technicien", siteId = (Guid?)temple } } });
        Assert.Equal(HttpStatusCode.Created, cree.StatusCode);
        return (ouverte.OrganisationId, (await cree.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("membreId").GetGuid());
    }

    private static Regex MembreLien(string nom) =>
        new($"membre=([0-9a-f-]{{36}})#panneau\">{Regex.Escape(nom)}<", RegexOptions.None, TimeSpan.FromSeconds(1));

    [GeneratedRegex("name=\"Roles.Version\" value=\"(\\d+)\"")]
    private static partial Regex Version();
}

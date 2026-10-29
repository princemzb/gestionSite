using System.Net;
using System.Text.RegularExpressions;
using GestionSite.Modules.Identite;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using GestionSite.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Api.Tests;

/// <summary>Écrans des dossiers (US-09, S09-05) de bout en bout : application web, API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed partial class WebDossiersTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private WebApplicationFactory<WebOptions> _web = null!;
    private Guid _organisation;
    private Guid _site;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync(infrastructure.ConnectionString);
        _api = infrastructure.CreateApi(_database);
        using (_api.CreateClient())
        {
        }

        _web = new WebApplicationFactory<WebOptions>().WithWebHostBuilder(builder => builder
            .UseSetting("Web:ApiUrl", "http://localhost")
            .ConfigureServices(services => services.AddHttpClient("api").ConfigurePrimaryHttpMessageHandler(() => _api.Server.CreateHandler())));
        var ouverte = await IdentiteModule.OuvrirOrganisationAsync(
            _api.Services, "Église Saint-Paul", "admin@dossiers-web.test", "Claire M.", CancellationToken.None, premierSite: "Site centre");
        _organisation = ouverte.OrganisationId;
        await MembreAsync("technicien@dossiers-web.test", "Marc D.", Role.Technicien);
        await MembreAsync("responsable@dossiers-web.test", "Rose L.", Role.Responsable);
        await MembreAsync("pasteur@dossiers-web.test", "Paul R.", Role.Pasteur);
    }

    public async Task DisposeAsync()
    {
        await _web.DisposeAsync();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ACaseGoesFromDeclarationToClosureThroughTheScreens()
    {
        using var responsable = await ConnecterAsync("responsable@dossiers-web.test");
        var batiment = await PosterAsync(responsable, $"/organisations/{_organisation}/inventaire?action=batiment", "structure",
            ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var zone = ZoneCommunes().Match(await LirePageAsync(responsable, Chemin(batiment))).Groups[1].Value;

        // 1. Le pasteur déclare depuis l'application.
        using var pasteur = await ConnecterAsync("pasteur@dossiers-web.test");
        var declare = await PosterAsync(pasteur, $"/organisations/{_organisation}/dossiers/declarer", "declaration",
            ("Saisie.CommandeId", Guid.NewGuid().ToString()), ("Saisie.ZoneId", zone), ("Saisie.Nature", "Incident"),
            ("Saisie.Description", "Fuite au plafond de la sacristie"), ("Saisie.Danger", "false"), ("Saisie.ActiviteBloquee", "true"));
        var fiche = Chemin(declare);
        var suivi = await LirePageAsync(pasteur, $"/organisations/{_organisation}/dossiers");

        Assert.Contains("Fuite au plafond de la sacristie", await LirePageAsync(pasteur, fiche), StringComparison.Ordinal);
        Assert.Matches("aria-label=\"À qualifier\">\\s*<h2>À qualifier <span>1</span>", suivi);
        Assert.DoesNotContain("Enregistrer la qualification", await LirePageAsync(pasteur, fiche), StringComparison.Ordinal);

        // 2. La responsable qualifie puis affecte au technicien.
        var qualifie = await PosterAsync(responsable, fiche, "qualification",
            ("Qualification.Version", Version("Qualification").Match(await LirePageAsync(responsable, fiche)).Groups[1].Value),
            ("Qualification.Priorite", "Haute"), ("Qualification.ZoneId", zone), ("Qualification.ComposantId", ""));
        var pageInterventions = await LirePageAsync(responsable, Chemin(qualifie).Replace("onglet=constat", "onglet=interventions", StringComparison.Ordinal));
        var technicien = Technicien().Match(pageInterventions).Groups[1].Value;
        var affecte = await PosterAsync(responsable, Chemin(qualifie).Replace("onglet=constat", "onglet=interventions", StringComparison.Ordinal), "affectation",
            ("Affectation.Version", Version("Affectation").Match(pageInterventions).Groups[1].Value), ("Affectation.Titre", "Reprendre le solin"),
            ("Affectation.IntervenantCompteId", technicien), ("Affectation.Prestataire", ""), ("Affectation.ReferentCompteId", ""));
        var apresAffectation = await LirePageAsync(responsable, Chemin(affecte));

        Assert.Contains("Reprendre le solin", apresAffectation, StringComparison.Ordinal);
        Assert.Contains("Marc D.", apresAffectation, StringComparison.Ordinal);
        Assert.Contains("Prise en charge", apresAffectation, StringComparison.Ordinal);

        // 3. Le technicien le voit dans « Mes interventions », le démarre puis le clôture.
        using var tech = await ConnecterAsync("technicien@dossiers-web.test");
        var miennes = await LirePageAsync(tech, $"/organisations/{_organisation}/interventions");
        var pageTech = await LirePageAsync(tech, $"{fiche}&onglet=interventions");
        var demarre = await PosterAsync(tech, $"{fiche}&onglet=interventions", formName: null,
            action: $"/organisations/{_organisation}/sites/{_site}/dossiers/{Identifiant().Match(fiche).Groups[1].Value}/interventions/1/demarrer",
            ("version", VersionAction().Match(pageTech).Groups[1].Value));
        var cloturePage = await LirePageAsync(tech, $"{fiche}&onglet=interventions&action=cloture&intervention=1");
        var cloture = await PosterAsync(tech, $"{fiche}&onglet=interventions&action=cloture&intervention=1", "cloture",
            ("Cloture.Version", Version("Cloture").Match(cloturePage).Groups[1].Value), ("Cloture.Numero", "1"),
            ("Cloture.Actions", "Solin refait au mortier, tuiles reposées"), ("Cloture.EtatFinal", "Resolu"));
        var cloturee = await LirePageAsync(tech, Chemin(cloture));
        var archives = await LirePageAsync(responsable, $"/organisations/{_organisation}/dossiers?archives=true");

        Assert.Contains("Reprendre le solin", miennes, StringComparison.Ordinal);
        Assert.Contains("issue=ok", Chemin(demarre), StringComparison.Ordinal);
        Assert.Contains("Solin refait au mortier", cloturee, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"step\">Clôturé", cloturee, StringComparison.Ordinal);
        Assert.Contains("Fuite au plafond de la sacristie", archives, StringComparison.Ordinal);
        Assert.Contains("Rouvrir le dossier", await LirePageAsync(responsable, fiche), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusalsAreExplainedAndHistoryNamesPeople()
    {
        using var responsable = await ConnecterAsync("responsable@dossiers-web.test");
        var batiment = await PosterAsync(responsable, $"/organisations/{_organisation}/inventaire?action=batiment", "structure",
            ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var zone = ZoneCommunes().Match(await LirePageAsync(responsable, Chemin(batiment))).Groups[1].Value;
        var fiche = Chemin(await PosterAsync(responsable, $"/organisations/{_organisation}/dossiers/declarer", "declaration",
            ("Saisie.CommandeId", Guid.NewGuid().ToString()), ("Saisie.ZoneId", zone), ("Saisie.Nature", "Incident"), ("Saisie.Description", "Bruit étrange")));
        var page = await LirePageAsync(responsable, fiche);

        var classementSansCommentaire = await LireAsync(await PosterAsync(responsable, fiche, "classement",
            ("Classement.Version", Version("Classement").Match(page).Groups[1].Value), ("Classement.Motif", "Autre"), ("Classement.Commentaire", "")));
        var sansPriorite = await LireAsync(await PosterAsync(responsable, fiche, "qualification",
            ("Qualification.Version", Version("Qualification").Match(page).Groups[1].Value), ("Qualification.ZoneId", zone)));
        var historique = await LirePageAsync(responsable, $"{fiche}&onglet=historique");
        var declarant = await LirePageAsync(responsable, $"{fiche}&onglet=declarant");

        Assert.Contains("Commentaire obligatoire pour le motif « Autre »", classementSansCommentaire, StringComparison.Ordinal);
        Assert.Contains("Choisissez une priorité.", sansPriorite, StringComparison.Ordinal);
        Assert.Contains("Rose L.", historique, StringComparison.Ordinal);
        Assert.Contains("Déclaré", historique, StringComparison.Ordinal);
        Assert.Contains("(compte interne)", declarant, StringComparison.Ordinal);
        Assert.Contains("Joignable dans l'application", declarant, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoldResumeDuplicatesContractorsAndReopeningWorkFromTheScreens()
    {
        using var responsable = await ConnecterAsync("responsable@dossiers-web.test");
        var batiment = await PosterAsync(responsable, $"/organisations/{_organisation}/inventaire?action=batiment", "structure",
            ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var zone = ZoneCommunes().Match(await LirePageAsync(responsable, Chemin(batiment))).Groups[1].Value;
        var principal = await DeclarerAsync(responsable, zone, "Infiltration au plafond");
        var doublon = await DeclarerAsync(responsable, zone, "Eau qui coule du plafond");
        var sansZone = await LireAsync(await PosterAsync(responsable, $"/organisations/{_organisation}/dossiers/declarer", "declaration",
            ("Saisie.CommandeId", Guid.NewGuid().ToString()), ("Saisie.Nature", "Incident"), ("Saisie.Description", "Sans zone")));

        // Doublon : suggéré sur la fiche, rattaché d'un clic.
        var pageDoublon = await LirePageAsync(responsable, doublon);
        var rattache = await PosterAsync(responsable, doublon, formName: null,
            action: $"/organisations/{_organisation}/sites/{_site}/dossiers/{Identifiant().Match(doublon).Groups[1].Value}/rattacher",
            ("version", VersionAction().Match(pageDoublon).Groups[1].Value), ("principalId", Identifiant().Match(principal).Groups[1].Value));

        // Principal : qualifié, affecté à un prestataire suivi par le technicien, démarré, mis en attente, repris.
        await PosterAsync(responsable, principal, "qualification",
            ("Qualification.Version", Version("Qualification").Match(await LirePageAsync(responsable, principal)).Groups[1].Value),
            ("Qualification.Priorite", "Urgente"), ("Qualification.ZoneId", zone));
        var interventions = $"{principal}&onglet=interventions";
        var page = await LirePageAsync(responsable, interventions);
        await PosterAsync(responsable, interventions, "affectation", ("Affectation.Version", Version("Affectation").Match(page).Groups[1].Value),
            ("Affectation.Titre", "Reprise de la toiture"), ("Affectation.IntervenantCompteId", ""), ("Affectation.Prestataire", "Couverture Martin"),
            ("Affectation.ReferentCompteId", Technicien().Match(page).Groups[1].Value));
        using var tech = await ConnecterAsync("technicien@dossiers-web.test");
        var aFaire = await LirePageAsync(tech, $"/organisations/{_organisation}/interventions");
        var identifiant = Identifiant().Match(principal).Groups[1].Value;
        var perime = await PosterAsync(tech, interventions, formName: null,
            action: $"/organisations/{_organisation}/sites/{_site}/dossiers/{identifiant}/interventions/1/demarrer", ("version", "1"));
        await PosterAsync(tech, interventions, formName: null,
            action: $"/organisations/{_organisation}/sites/{_site}/dossiers/{identifiant}/interventions/1/demarrer",
            ("version", VersionAction().Match(await LirePageAsync(tech, interventions)).Groups[1].Value));
        var enCours = await LirePageAsync(tech, $"/organisations/{_organisation}/interventions?filtre=EnCours");
        var formulaireAttente = await LirePageAsync(tech, $"{interventions}&action=attente&intervention=1");
        var attente = await PosterAsync(tech, $"{interventions}&action=attente&intervention=1", "attente",
            ("Attente.Version", Version("Attente").Match(formulaireAttente).Groups[1].Value), ("Attente.Numero", "1"), ("Attente.Motif", "Prestataire"),
            ("Attente.ReexamenLe", DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        var enAttente = await LirePageAsync(tech, Chemin(attente));
        var suivi = await LirePageAsync(responsable, $"/organisations/{_organisation}/dossiers");
        var repris = await PosterAsync(tech, interventions, formName: null,
            action: $"/organisations/{_organisation}/sites/{_site}/dossiers/{identifiant}/interventions/1/reprendre",
            ("version", VersionAction().Match(await LirePageAsync(tech, interventions)).Groups[1].Value));

        Assert.Contains("Choisissez une zone du site.", sansZone, StringComparison.Ordinal);
        Assert.Contains("issue=ok", Chemin(rattache), StringComparison.Ordinal);
        Assert.Contains("Rattaché à un doublon", await LirePageAsync(responsable, doublon), StringComparison.Ordinal);
        Assert.Contains("Reprise de la toiture", aFaire, StringComparison.Ordinal);
        Assert.Contains("prestataire", aFaire, StringComparison.Ordinal);
        Assert.Contains("issue=modifie_entre_temps", Chemin(perime), StringComparison.Ordinal);
        Assert.Contains("Quelqu'un d'autre vient de modifier ce dossier", await LirePageAsync(tech, Chemin(perime)), StringComparison.Ordinal);
        Assert.Contains("Reprise de la toiture", enCours, StringComparison.Ordinal);
        Assert.Contains("Prestataire extérieur · réexamen le", enAttente, StringComparison.Ordinal);
        Assert.Contains("Délai suspendu", suivi, StringComparison.Ordinal);
        Assert.Contains("issue=ok", Chemin(repris), StringComparison.Ordinal);

        // Un autre dossier : classé sans suite, puis rouvert.
        var bruit = await DeclarerAsync(responsable, zone, "Bruit étrange");
        await PosterAsync(responsable, bruit, "classement",
            ("Classement.Version", Version("Classement").Match(await LirePageAsync(responsable, bruit)).Groups[1].Value), ("Classement.Motif", "AucunProbleme"));
        var classe = await LirePageAsync(responsable, bruit);
        var rouvert = await PosterAsync(responsable, bruit, "reouverture",
            ("Reouverture.Version", Version("Reouverture").Match(classe).Groups[1].Value), ("Reouverture.Motif", "Le bruit est revenu"));

        Assert.Contains("Classé sans suite : Aucun problème constaté", classe, StringComparison.Ordinal);
        Assert.Contains("Enregistrer la qualification", await LirePageAsync(responsable, Chemin(rouvert)), StringComparison.Ordinal);
        Assert.Contains("Rouvert", await LirePageAsync(responsable, $"{bruit}&onglet=historique"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadersSeeEmptyArchivesAndCannotActOnACase()
    {
        using var responsable = await ConnecterAsync("responsable@dossiers-web.test");
        var batiment = await PosterAsync(responsable, $"/organisations/{_organisation}/inventaire?action=batiment", "structure",
            ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var zone = ZoneCommunes().Match(await LirePageAsync(responsable, Chemin(batiment))).Groups[1].Value;
        var fiche = await DeclarerAsync(responsable, zone, "Porte qui grince");
        var identifiant = Identifiant().Match(fiche).Groups[1].Value;
        using var pasteur = await ConnecterAsync("pasteur@dossiers-web.test");

        var archives = await LirePageAsync(pasteur, $"/organisations/{_organisation}/dossiers?archives=true");
        var refus = await PosterAsync(pasteur, fiche, formName: null, action: $"/organisations/{_organisation}/sites/{_site}/dossiers/{identifiant}/rattacher",
            ("version", "1"), ("principalId", Guid.NewGuid().ToString()));
        using var photo = await pasteur.GetAsync(new Uri($"/organisations/{_organisation}/sites/{_site}/dossiers/{identifiant}/interventions/1/photos/1", UriKind.Relative));
        var aucune = await LirePageAsync(pasteur, $"/organisations/{_organisation}/interventions");
        var inconnu = await LirePageAsync(pasteur, $"/organisations/{_organisation}/dossiers/{Guid.NewGuid()}");

        Assert.Contains("Aucun dossier archivé.", archives, StringComparison.Ordinal);
        Assert.Contains("issue=interdit", Chemin(refus), StringComparison.Ordinal);
        Assert.Contains("Vous n'avez pas le droit de faire cette action.", await LirePageAsync(pasteur, Chemin(refus)), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, photo.StatusCode);
        Assert.Contains("Aucun site ne vous confie d'intervention.", aucune, StringComparison.Ordinal);
        Assert.Contains("Ce dossier n'existe pas sur vos sites.", inconnu, StringComparison.Ordinal);
    }

    private async Task<string> DeclarerAsync(HttpClient navigateur, string zone, string description) =>
        Chemin(await PosterAsync(navigateur, $"/organisations/{_organisation}/dossiers/declarer", "declaration",
            ("Saisie.CommandeId", Guid.NewGuid().ToString()), ("Saisie.ZoneId", zone), ("Saisie.Nature", "Incident"), ("Saisie.Description", description)));

    private async Task MembreAsync(string email, string nom, Role role)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_organisation);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var compte = Compte.Nouveau(email, nom);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse)).Succeeded);
        var site = await context.Sites.SingleAsync();
        _site = site.Id;
        var membre = new Membre(Guid.NewGuid(), _organisation, compte.Id, DateTimeOffset.UtcNow);
        membre.Reviser([(role, site)]);
        membre.Accepter(DateTimeOffset.UtcNow);
        context.Add(membre);
        await context.SaveChangesAsync();
    }

    private async Task<HttpClient> ConnecterAsync(string email)
    {
        var navigateur = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var connexion = await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", email), ("Saisie.MotDePasse", MotDePasse));
        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
        return navigateur;
    }

    private static Task<HttpResponseMessage> PosterAsync(HttpClient navigateur, string page, string formName, params (string Nom, string Valeur)[] champs) =>
        PosterAsync(navigateur, page, formName, action: null, champs);

    private static async Task<HttpResponseMessage> PosterAsync(
        HttpClient navigateur, string page, string? formName, string? action, params (string Nom, string Valeur)[] champs)
    {
        var html = await LirePageAsync(navigateur, page);
        var valeurs = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", Antifalsification().Match(html).Groups[1].Value) };
        if (formName is not null)
        {
            valeurs.Add(new("_handler", formName));
        }

        valeurs.AddRange(champs.Select(c => new KeyValuePair<string, string>(c.Nom, c.Valeur)));
        using var contenu = new FormUrlEncodedContent(valeurs);
        return await navigateur.PostAsync(new Uri(action ?? page, UriKind.Relative), contenu);
    }

    private static string Chemin(HttpResponseMessage reponse)
    {
        Assert.True(reponse.StatusCode == HttpStatusCode.Redirect, $"{reponse.StatusCode} : {Alerte().Match(WebUtility.HtmlDecode(reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult())).Value}");
        var cible = reponse.Headers.Location!;
        return cible.IsAbsoluteUri ? cible.PathAndQuery : cible.OriginalString;
    }

    private static async Task<string> LirePageAsync(HttpClient navigateur, string page)
    {
        using var reponse = await navigateur.GetAsync(new Uri(page, UriKind.Relative));
        return await LireAsync(reponse);
    }

    private static async Task<string> LireAsync(HttpResponseMessage reponse)
    {
        var corps = WebUtility.HtmlDecode(await reponse.Content.ReadAsStringAsync());
        Assert.True(reponse.StatusCode == HttpStatusCode.OK, $"{reponse.StatusCode} : {corps[..Math.Min(corps.Length, 2000)]}");
        return corps;
    }

    private static Regex Version(string formulaire) => new($"name=\"{formulaire}\\.Version\" value=\"(\\d+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));

    [GeneratedRegex("name=\"__RequestVerificationToken\" (?:type=\"hidden\" )?value=\"([^\"]+)\"")]
    private static partial Regex Antifalsification();

    [GeneratedRegex("role=\"alert\"[^>]*>([^<]+)<")]
    private static partial Regex Alerte();

    [GeneratedRegex("class=\"arbre-zone retrait-1 communes\" href=\"[^\"]*zone=([0-9a-f-]{36})")]
    private static partial Regex ZoneCommunes();

    [GeneratedRegex("<option value=\"([0-9a-f-]{36})\">Marc D\\.</option>")]
    private static partial Regex Technicien();

    [GeneratedRegex("name=\"version\" value=\"(\\d+)\"")]
    private static partial Regex VersionAction();

    [GeneratedRegex("dossiers/([0-9a-f-]{36})")]
    private static partial Regex Identifiant();
}

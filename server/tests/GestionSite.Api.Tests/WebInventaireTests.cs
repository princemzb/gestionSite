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

/// <summary>Écrans d'inventaire (US-07, S07-03) de bout en bout : application web, API et PostgreSQL réels.</summary>
[Collection(SharedInfrastructure.Name)]
public sealed partial class WebInventaireTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string MotDePasse = "un-mot-de-passe-solide";
    private TestDatabase _database = null!;
    private WebApplicationFactory<Program> _api = null!;
    private WebApplicationFactory<WebOptions> _web = null!;
    private Guid _organisation;
    private string _page = null!;

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
            _api.Services, "Église Saint-Paul", "admin@inventaire-web.test", "Claire M.", CancellationToken.None, premierSite: "Site centre");
        _organisation = ouverte.OrganisationId;
        _page = $"/organisations/{_organisation}/inventaire";
        await MembreAsync("technicien@inventaire-web.test", "Marc D.", Role.Technicien);
        await MembreAsync("pasteur@inventaire-web.test", "Paul R.", Role.Pasteur);
    }

    public async Task DisposeAsync()
    {
        await _web.DisposeAsync();
        await _api.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ATechnicianDescribesTheSiteThenFollowsALotThroughItsHistory()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        Assert.Contains("Décrivez le site", await LirePageAsync(technicien, _page), StringComparison.Ordinal);

        var batiment = await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var structure = await LirePageAsync(technicien, Chemin(batiment));
        var communes = ZoneCommunes().Match(structure).Groups[1].Value;
        var cree = await PosterAsync(technicien, $"{_page}?action=composant", "composant", Chaises(communes, quantite: "40", bon: "30", degrade: "10"));
        var fiche = await LirePageAsync(technicien, Chemin(cree));

        Assert.Equal(HttpStatusCode.Redirect, batiment.StatusCode);
        Assert.Contains("Parties communes", structure, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, cree.StatusCode);
        Assert.Contains("aria-label=\"30 bon, 10 dégradé\"", fiche, StringComparison.Ordinal);
        Assert.Contains("Création : lot de 40 unités", fiche, StringComparison.Ordinal);
        Assert.Contains("Marc D.", fiche, StringComparison.Ordinal);
        Assert.Contains("1 composant actif dans 1 zone", fiche, StringComparison.Ordinal);

        var version = Version().Match(await LirePageAsync(technicien, $"{Chemin(cree)}&action=modifier")).Groups[1].Value;
        var modifie = await PosterAsync(technicien, $"{Chemin(cree)}&action=modifier", "composant",
            [.. Chaises(communes, quantite: "40", bon: "35", degrade: "5", verification: "non"), ("ComposantSaisi.Id", Identifiant().Match(Chemin(cree)).Groups[1].Value), ("ComposantSaisi.Version", version)]);
        var historique = await LirePageAsync(technicien, Chemin(modifie));
        var versionRetrait = VersionRetrait().Match(await LirePageAsync(technicien, $"{Chemin(modifie)}&action=retirer")).Groups[1].Value;
        var retire = await PosterAsync(technicien, $"{Chemin(modifie)}&action=retirer", "retrait",
            ("RetraitSaisi.Motif", "Cédées à la paroisse voisine"), ("RetraitSaisi.Version", versionRetrait));
        var apresRetrait = await LirePageAsync(technicien, Chemin(retire) + "&retires=true");

        Assert.Contains("Modification : bon 30 → 35, dégradé 10 → 5", historique, StringComparison.Ordinal);
        Assert.Contains("Retiré le", apresRetrait, StringComparison.Ordinal);
        Assert.Contains("Retrait : Cédées à la paroisse voisine", apresRetrait, StringComparison.Ordinal);
        Assert.Contains("0 composant actif", apresRetrait, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedEntryKeepsWhatWasTypedAndExplainsTheRule()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var batiment = await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var communes = ZoneCommunes().Match(await LirePageAsync(technicien, Chemin(batiment))).Groups[1].Value;

        var refus = await LireAsync(await PosterAsync(technicien, $"{_page}?action=composant", "composant", Chaises(communes, quantite: "6", bon: "5", degrade: "2")));
        var doublon = await LireAsync(await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "église")));

        Assert.Contains("Répartition par état : valeur refusée. La somme des quantités par état doit égaler la quantité totale.", refus, StringComparison.Ordinal);
        Assert.Contains("value=\"Chaises\"", refus, StringComparison.Ordinal);
        Assert.Contains("Ce nom existe déjà à cet endroit", doublon, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LevelsAndRoomsAreAddedFromTheirBuilding()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var batiment = await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var id = Batiment().Match(await LirePageAsync(technicien, Chemin(batiment))).Groups[1].Value;

        var niveau = await PosterAsync(technicien, $"{_page}?action=niveau&pour={id}", "structure",
            ("StructureSaisie.Type", "niveau"), ("StructureSaisie.BatimentId", id), ("StructureSaisie.Nom", "1er étage"), ("StructureSaisie.Ordre", "1"));
        var formulaireZone = await LirePageAsync(technicien, $"{Chemin(niveau)}&action=zone&pour={id}");
        var niveauId = Niveau().Match(formulaireZone).Groups[1].Value;
        var zone = await PosterAsync(technicien, $"{_page}?action=zone&pour={id}", "structure",
            ("StructureSaisie.Type", "zone"), ("StructureSaisie.BatimentId", id), ("StructureSaisie.Nom", "Salle des jeunes"), ("StructureSaisie.NiveauId", niveauId));
        var arbre = await LirePageAsync(technicien, Chemin(zone));

        Assert.Contains("Nouvelle zone · Église", formulaireZone, StringComparison.Ordinal);
        Assert.Matches("arbre-niveau\">1er étage</span>\\s*<a class=\"arbre-zone retrait-2\"[^>]*>Salle des jeunes", arbre);
        Assert.Contains("aria-current=\"page\">Salle des jeunes", arbre, StringComparison.Ordinal); // la nouvelle zone est sélectionnée
    }

    [Fact]
    public async Task EveryFieldChangeIsReadableAndConflictsAreExplained()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var batiment = await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var communes = ZoneCommunes().Match(await LirePageAsync(technicien, Chemin(batiment))).Groups[1].Value;
        var cree = await PosterAsync(technicien, $"{_page}?action=composant", "composant",
            ("ComposantSaisi.Nom", "Extincteur"), ("ComposantSaisi.CorpsEtat", "Mobilier"), ("ComposantSaisi.ZoneId", communes),
            ("ComposantSaisi.Verification", "non"), ("ComposantSaisi.Mode", "individuel"), ("ComposantSaisi.Unite", "Unite"));
        var fiche = Chemin(cree);
        var version = Version().Match(await LirePageAsync(technicien, $"{fiche}&action=modifier")).Groups[1].Value;
        (string, string)[] modification =
        [
            ("ComposantSaisi.Id", Identifiant().Match(fiche).Groups[1].Value), ("ComposantSaisi.Version", version),
            ("ComposantSaisi.Nom", "Extincteur CO2 2 kg"), ("ComposantSaisi.CorpsEtat", "SecuriteIncendie"), ("ComposantSaisi.ZoneId", communes),
            ("ComposantSaisi.Marque", "Sicli"), ("ComposantSaisi.Annee", "2021"), ("ComposantSaisi.Mois", "3"), ("ComposantSaisi.Verification", "oui"),
            ("ComposantSaisi.Mode", "lot"), ("ComposantSaisi.Quantite", "2"), ("ComposantSaisi.Unite", "Ensemble"),
        ];

        var modifie = await PosterAsync(technicien, $"{fiche}&action=modifier", "composant", modification);
        var historique = await LirePageAsync(technicien, Chemin(modifie));
        var perime = await LireAsync(await PosterAsync(technicien, $"{fiche}&action=modifier", "composant",
            [.. modification.Where(c => c.Item1 != "ComposantSaisi.Marque"), ("ComposantSaisi.Marque", "Desautel")])); // version déjà dépassée
        var inconnu = await LirePageAsync(technicien, $"{_page}?composant={Guid.NewGuid()}&action=modifier");
        var filtres = await LirePageAsync(technicien, $"{_page}?corpsEtat=SecuriteIncendie&batiment={Batiment().Match(historique).Groups[1].Value}&retires=true");
        var filtreInvalide = await LirePageAsync(technicien, $"{_page}?corpsEtat=Inconnu");

        Assert.Contains(
            "Modification : nom « Extincteur » → « Extincteur CO2 2 kg », corps d'état Mobilier → Sécurité incendie, marque — → « Sicli », " +
            "année de mise en service — → 2021, mois de mise en service — → mars, soumis à vérification non → oui, unité — → ensemble, quantité — → 2",
            historique, StringComparison.Ordinal);
        Assert.Contains("mars 2021", historique, StringComparison.Ordinal);
        Assert.Contains("Quelqu'un d'autre vient de modifier ce composant", perime, StringComparison.Ordinal);
        Assert.Contains("Ce composant n'existe pas sur ce site.", inconnu, StringComparison.Ordinal);
        Assert.DoesNotContain("Nouveau composant", inconnu, StringComparison.Ordinal); // pas de création à la place d'une modification
        Assert.Contains("Extincteur CO2 2 kg", filtres, StringComparison.Ordinal);
        Assert.Contains("Effacer", filtres, StringComparison.Ordinal);
        Assert.Contains("Filtre refusé : Corps d'état.", filtreInvalide, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadersGetNoFormAndOtherOrganisationsShowNothing()
    {
        await MembreAsync("coordination@inventaire-web.test", "Anne C.", Role.Coordination);
        using var coordination = await ConnecterAsync("coordination@inventaire-web.test");

        var lecture = await LirePageAsync(coordination, $"{_page}?action=batiment");
        using var ecriture = await PosterAsync(coordination, $"{_page}?action=batiment", "structure",
            ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        using var autre = await coordination.GetAsync(new Uri($"/organisations/{Guid.NewGuid()}/inventaire", UriKind.Relative));

        Assert.Contains("Décrivez le site", lecture, StringComparison.Ordinal);
        Assert.DoesNotContain("Nouveau bâtiment", lecture, StringComparison.Ordinal);
        Assert.Contains("Aucun site ne vous est attribué", await LireAsync(autre), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, ecriture.StatusCode); // formulaire absent de la page d'un lecteur
        Assert.Equal(0, await _database.CountAsync("SELECT count(*) FROM patrimoine.batiments"));
    }

    [Fact]
    public async Task FiltersNarrowTheListAndReadersCannotChangeAnything()
    {
        using var technicien = await ConnecterAsync("technicien@inventaire-web.test");
        var batiment = await PosterAsync(technicien, $"{_page}?action=batiment", "structure", ("StructureSaisie.Type", "batiment"), ("StructureSaisie.Nom", "Église"));
        var communes = ZoneCommunes().Match(await LirePageAsync(technicien, Chemin(batiment))).Groups[1].Value;
        await PosterAsync(technicien, $"{_page}?action=composant", "composant", Chaises(communes, quantite: "40", bon: "", degrade: ""));
        await PosterAsync(technicien, $"{_page}?action=composant", "composant",
            ("ComposantSaisi.Nom", "Tableau divisionnaire"), ("ComposantSaisi.CorpsEtat", "CourantsForts"), ("ComposantSaisi.ZoneId", communes),
            ("ComposantSaisi.Verification", ""), ("ComposantSaisi.Mode", "individuel"), ("ComposantSaisi.Unite", "Unite"));

        using var pasteur = await ConnecterAsync("pasteur@inventaire-web.test");
        var tout = await LirePageAsync(pasteur, _page);
        var verification = await LirePageAsync(pasteur, $"{_page}?verification=true");
        var recherche = await LirePageAsync(pasteur, $"{_page}?recherche=zzz");

        Assert.Contains("40 unités · non évalué", tout, StringComparison.Ordinal);
        Assert.DoesNotContain("Ajouter un composant", tout, StringComparison.Ordinal);
        Assert.Contains("Tableau divisionnaire", verification, StringComparison.Ordinal); // proposé pour les courants forts
        Assert.DoesNotContain(">Chaises<", verification, StringComparison.Ordinal);
        Assert.Contains("Aucun composant ne correspond à ces filtres.", recherche, StringComparison.Ordinal);
    }

    private static (string, string)[] Chaises(string zone, string quantite, string bon, string degrade, string verification = "") =>
    [
        ("ComposantSaisi.Nom", "Chaises"), ("ComposantSaisi.CorpsEtat", "Mobilier"), ("ComposantSaisi.ZoneId", zone), ("ComposantSaisi.Verification", verification),
        ("ComposantSaisi.Mode", "lot"), ("ComposantSaisi.Quantite", quantite), ("ComposantSaisi.Unite", "Unite"),
        ("ComposantSaisi.Bon", bon), ("ComposantSaisi.Degrade", degrade),
    ];

    private static string Chemin(HttpResponseMessage reponse)
    {
        Assert.True(reponse.StatusCode == HttpStatusCode.Redirect, AlerteDe(reponse));
        var cible = reponse.Headers.Location!;
        return cible.IsAbsoluteUri ? cible.PathAndQuery : cible.OriginalString;
    }

    // Message affiché par la page quand l'envoi n'aboutit pas (diagnostic d'un test en échec).
    private static string AlerteDe(HttpResponseMessage reponse)
    {
        var corps = WebUtility.HtmlDecode(reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var alerte = Alerte().Match(corps);
        return $"{reponse.StatusCode} : {(alerte.Success ? alerte.Groups[1].Value : corps[..Math.Min(corps.Length, 500)])}";
    }

    private async Task<HttpClient> ConnecterAsync(string email)
    {
        var navigateur = _web.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var connexion = await PosterAsync(navigateur, "/connexion", "connexion", ("Saisie.Email", email), ("Saisie.MotDePasse", MotDePasse));
        Assert.Equal(HttpStatusCode.Redirect, connexion.StatusCode);
        return navigateur;
    }

    private static async Task<HttpResponseMessage> PosterAsync(HttpClient navigateur, string page, string formName, params (string Nom, string Valeur)[] champs)
    {
        var html = await LirePageAsync(navigateur, page);
        var valeurs = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", Antifalsification().Match(html).Groups[1].Value), new("_handler", formName),
        };
        valeurs.AddRange(champs.Select(c => new KeyValuePair<string, string>(c.Nom, c.Valeur)));
        using var contenu = new FormUrlEncodedContent(valeurs);
        return await navigateur.PostAsync(new Uri(page, UriKind.Relative), contenu);
    }

    private static async Task<string> LirePageAsync(HttpClient navigateur, string page)
    {
        using var reponse = await navigateur.GetAsync(new Uri(page, UriKind.Relative));
        return await LireAsync(reponse);
    }

    private static async Task<string> LireAsync(HttpResponseMessage reponse)
    {
        var corps = WebUtility.HtmlDecode(await reponse.Content.ReadAsStringAsync());
        Assert.True(reponse.StatusCode == HttpStatusCode.OK, $"{reponse.StatusCode} : {corps[..Math.Min(corps.Length, 3000)]}");
        return corps;
    }

    private async Task MembreAsync(string email, string nom, Role role)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(_organisation);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var compte = Compte.Nouveau(email, nom);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Compte>>().CreateAsync(compte, MotDePasse)).Succeeded);
        var membre = new Membre(Guid.NewGuid(), _organisation, compte.Id, DateTimeOffset.UtcNow);
        membre.Reviser([(role, await context.Sites.SingleAsync())]);
        membre.Accepter(DateTimeOffset.UtcNow);
        context.Add(membre);
        await context.SaveChangesAsync();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" (?:type=\"hidden\" )?value=\"([^\"]+)\"")]
    private static partial Regex Antifalsification();

    [GeneratedRegex("class=\"arbre-zone retrait-1 communes\" href=\"[^\"]*zone=([0-9a-f-]{36})")]
    private static partial Regex ZoneCommunes();

    [GeneratedRegex("role=\"alert\"[^>]*>([^<]+)<")]
    private static partial Regex Alerte();

    [GeneratedRegex("class=\"arbre-batiment\" href=\"[^\"]*batiment=([0-9a-f-]{36})")]
    private static partial Regex Batiment();

    [GeneratedRegex("<option value=\"([0-9a-f-]{36})\">1er étage</option>")]
    private static partial Regex Niveau();

    [GeneratedRegex("name=\"ComposantSaisi.Version\" value=\"(\\d+)\"")]
    private static partial Regex Version();

    [GeneratedRegex("name=\"RetraitSaisi.Version\" value=\"(\\d+)\"")]
    private static partial Regex VersionRetrait();

    [GeneratedRegex("composant=([0-9a-f-]{36})")]
    private static partial Regex Identifiant();
}

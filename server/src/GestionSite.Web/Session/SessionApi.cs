using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GestionSite.Web.Session;

internal enum IssueConnexion
{
    Reussie,
    IdentifiantsInvalides,
    CompteVerrouille,
    LienInvalide,
    MotDePasseInvalide,
    Indisponible,

    /// <summary>Compte activé, mais connexion automatique impossible : se connecter depuis la page de connexion.</summary>
    ActiveSansConnexion,
}

/// <summary>Session expirée ou révoquée : l'utilisateur doit se reconnecter.</summary>
internal sealed class SessionExpireeException() : Exception("Session expirée.");

/// <summary>Échec métier renvoyé par l'API (code stable des ProblemDetails).</summary>
internal sealed class RefusApiException(
    HttpStatusCode statut, string? code, string? message, string? champ = null, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? details = null)
    : Exception(message ?? code)
{
    internal HttpStatusCode Statut { get; } = statut;

    internal string? Code { get; } = code;

    /// <summary>Champ refusé d'une saisie invalide (422 « donnee_invalide »).</summary>
    internal string? Champ { get; } = champ;

    /// <summary>Autres membres du refus (conflit de version : version gagnante, auteur, date).</summary>
    internal IReadOnlyDictionary<string, System.Text.Json.JsonElement> Details { get; } = details ?? new Dictionary<string, System.Text.Json.JsonElement>();
}

/// <summary>
/// Appels à l'API pour l'utilisateur connecté. Jetons rangés dans les propriétés chiffrées du cookie ;
/// le jeton d'accès (15 min) est renouvelé à l'approche de son expiration ou sur refus 401. Une page peut lancer
/// plusieurs lectures en parallèle : le renouvellement et l'écriture du cookie restent un à la fois.
/// </summary>
internal sealed class SessionApi(
    IHttpClientFactory clients, IHttpContextAccessor accesseur, TimeProvider horloge, RenouvellementsPartages renouvellements) : IDisposable
{
    private const string Acces = "acces";
    private const string Renouvellement = "renouvellement";
    private const string AccesExpire = "acces_expire";

    // Jetons de la requête en cours : après un renouvellement, les appels suivants de la même page doivent
    // utiliser les nouveaux, pas ceux du cookie reçu (déjà remplacés, donc pris pour un vol par l'API).
    private JetonsApi? _jetonsCourants;

    // Lectures parallèles d'une même page : le contexte HTTP (cookie de session) n'est jamais modifié en même temps.
    private readonly SemaphoreSlim _verrouJetons = new(1, 1);

    // Renouvellement refusé pendant cette requête : session fermée, les lectures en attente ne le retentent pas.
    private bool _sessionFermee;

    // Profil lu pendant cette requête (MoiAsync).
    private Task<MoiApi>? _moi;

    private HttpContext Contexte => accesseur.HttpContext ?? throw new InvalidOperationException("Hors requête HTTP.");

    private HttpClient Client => clients.CreateClient(Programme.ClientApi);

    internal async Task<IssueConnexion> ConnecterAsync(string email, string motDePasse, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAnonymeAsync("/auth/login", new { email, motDePasse }, annulation).ConfigureAwait(false);
        if (reponse is null)
        {
            return IssueConnexion.Indisponible;
        }

        if (!reponse.IsSuccessStatusCode)
        {
            // 401 : identifiants ; 423 : verrouillage ; le reste est une panne, pas un mauvais mot de passe.
            return reponse.StatusCode switch
            {
                HttpStatusCode.Unauthorized => IssueConnexion.IdentifiantsInvalides,
                HttpStatusCode.Locked => IssueConnexion.CompteVerrouille,
                _ => IssueConnexion.Indisponible,
            };
        }

        var jetons = (await reponse.Content.ReadFromJsonAsync<JetonsApi>(annulation).ConfigureAwait(false))!;
        try
        {
            using var demande = new HttpRequestMessage(HttpMethod.Get, new Uri("/me", UriKind.Relative));
            demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jetons.JetonAcces);
            using var moi = await Client.SendAsync(demande, annulation).ConfigureAwait(false);
            if (!moi.IsSuccessStatusCode)
            {
                return IssueConnexion.Indisponible;
            }

            var profil = (await moi.Content.ReadFromJsonAsync<MoiApi>(annulation).ConfigureAwait(false))!;
            await OuvrirSessionAsync(profil.CompteId, profil.Email ?? email, jetons).ConfigureAwait(false);
            return IssueConnexion.Reussie;
        }
        catch (HttpRequestException)
        {
            return IssueConnexion.Indisponible;
        }
    }

    /// <summary>Activation par le lien, puis connexion immédiate avec le mot de passe choisi.</summary>
    internal async Task<IssueConnexion> ActiverAsync(string jeton, string motDePasse, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAnonymeAsync("/auth/activate", new { jeton, motDePasse }, annulation).ConfigureAwait(false);
        if (reponse is null)
        {
            return IssueConnexion.Indisponible;
        }

        if (!reponse.IsSuccessStatusCode)
        {
            return reponse.StatusCode == HttpStatusCode.UnprocessableEntity ? IssueConnexion.MotDePasseInvalide : IssueConnexion.LienInvalide;
        }

        // Le lien est consommé : si la connexion échoue ensuite, un nouvel essai d'activation dirait « lien
        // expiré ». On renvoie donc vers la page de connexion, où le compte, désormais actif, fonctionne.
        var activation = (await reponse.Content.ReadFromJsonAsync<ActivationReussieApi>(annulation).ConfigureAwait(false))!;
        var connexion = await ConnecterAsync(activation.Email, motDePasse, annulation).ConfigureAwait(false);
        return connexion == IssueConnexion.Reussie ? connexion : IssueConnexion.ActiveSansConnexion;
    }

    /// <summary>
    /// Nouveau mot de passe choisi avec un lien de réinitialisation (US-31), puis connexion immédiate. L'API a déconnecté tous
    /// les appareils du compte ; seule cette session-ci est ouverte.
    /// </summary>
    internal async Task<IssueConnexion> ReinitialiserAsync(string jeton, string motDePasse, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAnonymeAsync("/auth/reset-password", new { jeton, motDePasse }, annulation).ConfigureAwait(false);
        if (reponse is null)
        {
            return IssueConnexion.Indisponible;
        }

        if (!reponse.IsSuccessStatusCode)
        {
            return reponse.StatusCode switch
            {
                HttpStatusCode.UnprocessableEntity => IssueConnexion.MotDePasseInvalide,
                HttpStatusCode.Gone => IssueConnexion.LienInvalide,
                _ => IssueConnexion.Indisponible, // dont 409 « compte_modifie » : réessayer, le lien reste valable
            };
        }

        var compte = (await reponse.Content.ReadFromJsonAsync<ActivationReussieApi>(annulation).ConfigureAwait(false))!;
        var connexion = await ConnecterAsync(compte.Email, motDePasse, annulation).ConfigureAwait(false);
        return connexion == IssueConnexion.Reussie ? connexion : IssueConnexion.ActiveSansConnexion;
    }

    // GET /me passe par le profil de la requête (MoiAsync) : les pages existantes le lisent ainsi sans appel en double.
    internal Task<T> LireAsync<T>(string chemin, CancellationToken annulation) =>
        typeof(T) == typeof(MoiApi) && chemin == "/me"
            ? (Task<T>)(object)MoiAsync(annulation)
            : EnvoyerEtLireAsync<T>(HttpMethod.Get, chemin, corps: null, annulation);

    /// <summary>
    /// Profil de l'appelant (GET /me), lu une fois par requête : la page et la navigation commune (sélecteur d'organisation,
    /// carte d'invitation) le partagent ; toute écriture de la requête l'oublie.
    /// </summary>
    internal Task<MoiApi> MoiAsync(CancellationToken annulation) => _moi ??= EnvoyerEtLireAsync<MoiApi>(HttpMethod.Get, "/me", corps: null, annulation);

    internal Task<T> EcrireAsync<T>(string chemin, object corps, CancellationToken annulation) => EnvoyerEtLireAsync<T>(HttpMethod.Post, chemin, corps, annulation);

    /// <summary>Contenu binaire (photo) d'une ressource de l'API, ou null si elle n'existe pas.</summary>
    internal async Task<byte[]?> LireOctetsAsync(string chemin, CancellationToken annulation) =>
        (await LireFichierAsync(chemin, annulation).ConfigureAwait(false))?.Contenu;

    /// <summary>Contenu binaire et type annoncé par l'API (fond de plan : PNG ou JPEG), ou null si la ressource n'existe pas.</summary>
    internal async Task<(byte[] Contenu, string? TypeContenu)?> LireFichierAsync(string chemin, CancellationToken annulation)
    {
        try
        {
            using var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, corps: null, annulation).ConfigureAwait(false);
            return (await reponse.Content.ReadAsByteArrayAsync(annulation).ConfigureAwait(false), reponse.Content.Headers.ContentType?.MediaType);
        }
        catch (RefusApiException refus) when (refus.Statut is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }
    }

    internal async Task ModifierAsync(string chemin, object corps, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Put, chemin, corps, annulation).ConfigureAwait(false);
    }

    /// <summary>Remplacement (PUT) dont la réponse est lue, par exemple la version créée d'une grille.</summary>
    internal Task<T> ModifierAvecReponseAsync<T>(string chemin, object corps, CancellationToken annulation) =>
        EnvoyerEtLireAsync<T>(HttpMethod.Put, chemin, corps, annulation);

    private async Task<T> EnvoyerEtLireAsync<T>(HttpMethod methode, string chemin, object? corps, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAsync(methode, chemin, corps, annulation).ConfigureAwait(false);
        return (await reponse.Content.ReadFromJsonAsync<T>(annulation).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Formulaire multipart (partie « demande » en JSON et photos), réponse lue en <typeparamref name="T"/> ou ignorée (204).
    /// Le contenu est reconstruit à chaque essai : un renouvellement de jeton renvoie un corps complet.
    /// </summary>
    internal Task<T?> EnvoyerFormulaireAsync<T>(string chemin, object demande, IReadOnlyList<IFormFile> photos, CancellationToken annulation) =>
        EnvoyerFormulaireAsync<T>(chemin, new ContenuFormulaire(demande, photos), annulation);

    /// <summary>Même formulaire, fichiers envoyés dans la partie <paramref name="partie"/> (fond de plan : « fichier »).</summary>
    internal Task<T?> EnvoyerFormulaireAsync<T>(string chemin, object demande, IReadOnlyList<IFormFile> fichiers, string partie, CancellationToken annulation) =>
        EnvoyerFormulaireAsync<T>(chemin, new ContenuFormulaire(demande, fichiers, partie), annulation);

    /// <summary>Formulaire multipart avec, en plus des photos, un rapport PDF (partie « rapport ») vérifié par l'API.</summary>
    internal Task<T?> EnvoyerFormulaireAsync<T>(string chemin, object demande, IReadOnlyList<IFormFile> photos, IFormFile? rapport, CancellationToken annulation) =>
        EnvoyerFormulaireAsync<T>(chemin, new ContenuFormulaire(demande, photos, rapport: rapport), annulation);

    private async Task<T?> EnvoyerFormulaireAsync<T>(string chemin, ContenuFormulaire formulaire, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, chemin, formulaire, annulation).ConfigureAwait(false);
        return reponse.StatusCode == HttpStatusCode.NoContent
            ? default
            : await reponse.Content.ReadFromJsonAsync<T>(annulation).ConfigureAwait(false);
    }

    /// <summary>Action sans contenu en retour (204), par exemple un retrait.</summary>
    internal async Task ExecuterAsync(string chemin, object corps, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Post, chemin, corps, annulation).ConfigureAwait(false);
    }

    internal static async Task<IResult> DeconnecterAsync(HttpContext contexte, IHttpClientFactory clients, IAntiforgery antifalsification)
    {
        // Point d'accès sans formulaire lié : le jeton anti-falsification n'y serait pas vérifié d'office.
        if (!await antifalsification.IsRequestValidAsync(contexte).ConfigureAwait(false))
        {
            return Results.BadRequest();
        }

        var renouvellement = await contexte.GetTokenAsync(Renouvellement).ConfigureAwait(false);
        if (renouvellement is not null)
        {
            try
            {
                // Sans annulation : la révocation doit aboutir même si le navigateur abandonne la requête.
                using var reponse = await clients.CreateClient(Programme.ClientApi)
                    .PostAsJsonAsync("/auth/logout", new { jetonRenouvellement = renouvellement }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // API injoignable : la session locale est fermée quand même ; le jeton expirera.
            }
        }

        await contexte.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        // Brouillons locaux de l'éditeur de plans (tablette partagée, RP-21.8) : stockage du site effacé par le navigateur.
        contexte.Response.Headers["Clear-Site-Data"] = "\"storage\"";
        return Results.LocalRedirect("/connexion");
    }

    /// <summary>Suppression (DELETE) avec un corps, par exemple le motif d'une révocation (jamais dans l'adresse).</summary>
    internal async Task SupprimerAsync(string chemin, object corps, CancellationToken annulation)
    {
        using var reponse = await EnvoyerAsync(HttpMethod.Delete, chemin, corps, annulation).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> EnvoyerAsync(HttpMethod methode, string chemin, object? corps, CancellationToken annulation)
    {
        if (methode != HttpMethod.Get)
        {
            _moi = null; // rôles, invitations ou organisations peut-être changés
        }

        try
        {
            return await EnvoyerOuEchouerAsync(methode, chemin, corps, annulation).ConfigureAwait(false);
        }
        catch (Exception erreur) when (erreur is HttpRequestException or TaskCanceledException)
        {
            // API en réveil ou injoignable : message à l'utilisateur plutôt qu'une page d'erreur.
            throw new RefusApiException(HttpStatusCode.ServiceUnavailable, "indisponible", "Service momentanément indisponible. Réessayez dans un instant.");
        }
    }

    private async Task<HttpResponseMessage> EnvoyerOuEchouerAsync(HttpMethod methode, string chemin, object? corps, CancellationToken annulation)
    {
        var acces = await JetonValideAsync(refuse: null, annulation).ConfigureAwait(false);
        var reponse = await EnvoyerAvecAsync(methode, chemin, corps, acces, annulation).ConfigureAwait(false);
        if (reponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            reponse.Dispose();
            acces = await JetonValideAsync(refuse: acces, annulation).ConfigureAwait(false);
            reponse = await EnvoyerAvecAsync(methode, chemin, corps, acces, annulation).ConfigureAwait(false);
        }

        if (reponse.IsSuccessStatusCode)
        {
            return reponse;
        }

        using (reponse)
        {
            var probleme = await LireProblemeAsync(reponse, annulation).ConfigureAwait(false);
            throw reponse.StatusCode == HttpStatusCode.Unauthorized
                ? new SessionExpireeException()
                : new RefusApiException(reponse.StatusCode, probleme?.Code, probleme?.Title, probleme?.Champ, probleme?.Autres);
        }
    }

    // Corps d'erreur souvent sans longueur annoncée (réponse découpée) ; page HTML d'un proxy possible.
    private static async Task<ProblemeApi?> LireProblemeAsync(HttpResponseMessage reponse, CancellationToken annulation)
    {
        try
        {
            return await reponse.Content.ReadFromJsonAsync<ProblemeApi>(annulation).ConfigureAwait(false);
        }
        catch (Exception erreur) when (erreur is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> EnvoyerAvecAsync(HttpMethod methode, string chemin, object? corps, string acces, CancellationToken annulation)
    {
        return await AvecReessaisAsync(async () =>
        {
            using var demande = new HttpRequestMessage(methode, new Uri(chemin, UriKind.Relative));
            demande.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acces);
            demande.Content = corps switch
            {
                null => null,
                ContenuFormulaire formulaire => formulaire.Creer(),
                _ => JsonContent.Create(corps),
            };

            // Import d'un fond de plan (rendu PDF compris) : client au délai long.
            var client = corps is ContenuFormulaire { Partie: "fichier" } ? clients.CreateClient(Programme.ClientApiLong) : Client;
            return await client.SendAsync(demande, annulation).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>Jeton d'accès valide ; <paramref name="refuse"/> : jeton refusé (401) par l'API, à remplacer.</summary>
    private async Task<string> JetonValideAsync(string? refuse, CancellationToken annulation)
    {
        await _verrouJetons.WaitAsync(annulation).ConfigureAwait(false);
        try
        {
            return await JetonValideSousVerrouAsync(refuse, annulation).ConfigureAwait(false);
        }
        finally
        {
            _verrouJetons.Release();
        }
    }

    private async Task<string> JetonValideSousVerrouAsync(string? refuse, CancellationToken annulation)
    {
        if (_sessionFermee)
        {
            throw new SessionExpireeException();
        }

        // Jeton refusé déjà remplacé par une lecture parallèle : pas de second renouvellement.
        if (refuse is not null && _jetonsCourants is { } courants && courants.JetonAcces != refuse)
        {
            return courants.JetonAcces;
        }

        var forcer = refuse is not null;
        var resultat = await Contexte.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        var proprietes = resultat.Properties ?? throw new SessionExpireeException();
        var acces = _jetonsCourants?.JetonAcces ?? proprietes.GetTokenValue(Acces);
        var expire = _jetonsCourants?.AccesExpireLe
            ?? (DateTimeOffset.TryParse(proprietes.GetTokenValue(AccesExpire), out var date) ? date : DateTimeOffset.MinValue);
        if (!forcer && acces is not null && expire > horloge.GetUtcNow().AddMinutes(1))
        {
            return acces;
        }

        if (!forcer && _jetonsCourants is not null)
        {
            return _jetonsCourants.JetonAcces; // déjà renouvelé pendant cette requête : pas de second renouvellement
        }

        var renouvellement = _jetonsCourants?.JetonRenouvellement ?? proprietes.GetTokenValue(Renouvellement) ?? string.Empty;
        var jetons = await renouvellements.RenouvelerAsync(renouvellement, async () =>
        {
            using var reponse = await EnvoyerAnonymeAsync("/auth/refresh", new { jetonRenouvellement = renouvellement }, annulation).ConfigureAwait(false);
            return reponse is { IsSuccessStatusCode: true }
                ? await reponse.Content.ReadFromJsonAsync<JetonsApi>(annulation).ConfigureAwait(false)
                : null;
        }).ConfigureAwait(false);
        if (jetons is null)
        {
            _sessionFermee = true;
            await Contexte.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            throw new SessionExpireeException();
        }

        _jetonsCourants = jetons;
        await Contexte.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, resultat.Principal!, Proprietes(jetons)).ConfigureAwait(false);
        return jetons.JetonAcces;
    }

    // Exigé par l'analyse (CA1001) ; toutes les lectures d'une page sont attendues avant la fin de la requête.
    public void Dispose() => _verrouJetons.Dispose();

    private async Task OuvrirSessionAsync(Guid compteId, string email, JetonsApi jetons)
    {
        var identite = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, compteId.ToString()), new Claim(ClaimTypes.Name, email)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        _jetonsCourants = jetons;
        await Contexte.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identite), Proprietes(jetons))
            .ConfigureAwait(false);
    }

    private static AuthenticationProperties Proprietes(JetonsApi jetons)
    {
        var proprietes = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = jetons.RenouvellementExpireLe };
        proprietes.StoreTokens([
            new AuthenticationToken { Name = Acces, Value = jetons.JetonAcces },
            new AuthenticationToken { Name = Renouvellement, Value = jetons.JetonRenouvellement },
            new AuthenticationToken { Name = AccesExpire, Value = jetons.AccesExpireLe.ToString("O") },
        ]);
        return proprietes;
    }

    /// <summary>
    /// Au réveil du conteneur, l'application web écoute avant l'API : une connexion refusée (requête jamais
    /// partie, donc sans effet) est réessayée quelques secondes. Toute autre erreur remonte aussitôt.
    /// </summary>
    internal static async Task<HttpResponseMessage> AvecReessaisAsync(Func<Task<HttpResponseMessage>> envoi)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                return await envoi().ConfigureAwait(false);
            }
            catch (HttpRequestException erreur) when (essai < 10 && erreur.InnerException is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused,
            })
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * essai)).ConfigureAwait(false);
            }
        }
    }

    private async Task<HttpResponseMessage?> EnvoyerAnonymeAsync(string chemin, object corps, CancellationToken annulation)
    {
        try
        {
            return await AvecReessaisAsync(() => Client.PostAsJsonAsync(chemin, corps, annulation)).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null; // API injoignable : message « service indisponible »
        }
    }
}

/// <summary>
/// Formulaire avec photos (ou un fichier dans la partie <paramref name="partie"/>) et, au besoin, un rapport PDF ; recréé à
/// chaque envoi (un flux déjà lu ne se renvoie pas).
/// </summary>
internal sealed class ContenuFormulaire(object demande, IReadOnlyList<IFormFile> fichiers, string partie = "photos", IFormFile? rapport = null)
{
    /// <summary>Partie des fichiers : « photos », ou « fichier » pour un fond de plan.</summary>
    internal string Partie => partie;

    internal MultipartFormDataContent Creer()
    {
        var formulaire = new MultipartFormDataContent { { JsonContent.Create(demande), "demande" } };
        foreach (var fichier in fichiers)
        {
            var contenu = new StreamContent(fichier.OpenReadStream());
            contenu.Headers.ContentType = MediaTypeHeaderValue.TryParse(fichier.ContentType, out var type) ? type : null;
            formulaire.Add(contenu, partie, partie);
        }

        // Type réel reconnu par l'API à sa signature, jamais au nom ni au type annoncé.
        if (rapport is not null)
        {
            var contenu = new StreamContent(rapport.OpenReadStream());
            contenu.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            formulaire.Add(contenu, "rapport", "rapport.pdf");
        }

        return formulaire;
    }
}

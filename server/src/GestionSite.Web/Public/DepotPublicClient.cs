using System.Net;
using System.Net.Http.Headers;
using GestionSite.Web.Session;

namespace GestionSite.Web.Public;

/// <summary>API injoignable, trop lente ou réponse illisible.</summary>
internal sealed class DepotIndisponibleException(Exception cause) : Exception("Dépôt public momentanément indisponible.", cause);

/// <summary>Issue d'un envoi du formulaire public (CONTRATS : dépôt public).</summary>
internal sealed record IssueDepot(AccuseApi? Accuse, HttpStatusCode Statut, string? Champ);

/// <summary>Données du formulaire transmises à l'API, photos à part.</summary>
internal sealed record DemandeDepotWeb(
    Guid CommandeId, string Nature, string? Description, string? Emplacement, bool Danger, bool ActiviteBloquee, string? Nom, string? Contact,
    string? Verification);

/// <summary>
/// Appels anonymes du formulaire public vers l'API interne (US-08). Une API injoignable, trop lente ou une réponse
/// illisible lèvent <see cref="DepotIndisponibleException"/> : la page garde la saisie et l'explique.
/// </summary>
internal sealed class DepotPublicClient(IHttpClientFactory clients)
{
    /// <summary>Client du dépôt : délai long, le traitement de 3 photos par l'API pouvant dépasser 30 s.</summary>
    internal const string ClientDepot = "api-depot";

    private HttpClient Client => clients.CreateClient(Programme.ClientApi);

    private HttpClient ClientEnvoi => clients.CreateClient(ClientDepot);

    /// <summary>Site et organisation du code, ou null s'il est inconnu ; exception si l'API est injoignable.</summary>
    internal async Task<SitePublicApi?> SiteAsync(string code, CancellationToken annulation)
    {
        try
        {
            return await LireSiteAsync(code, annulation).ConfigureAwait(false);
        }
        catch (Exception erreur) when (erreur is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new DepotIndisponibleException(erreur);
        }
    }

    internal async Task<IssueDepot> DeposerAsync(string code, DemandeDepotWeb demande, IReadOnlyList<IFormFile> photos, CancellationToken annulation)
    {
        try
        {
            return await EnvoyerAsync(code, demande, photos, annulation).ConfigureAwait(false);
        }
        catch (Exception erreur) when (erreur is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new DepotIndisponibleException(erreur);
        }
    }

    private async Task<SitePublicApi?> LireSiteAsync(string code, CancellationToken annulation)
    {
        using var reponse = await SessionApi.AvecReessaisAsync(() =>
            Client.GetAsync(new Uri($"/public/sites/{Uri.EscapeDataString(code)}", UriKind.Relative), annulation)).ConfigureAwait(false);
        return reponse.StatusCode == HttpStatusCode.NotFound
            ? null
            : await reponse.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SitePublicApi>(annulation).ConfigureAwait(false);
    }

    /// <summary>Envoi idempotent (commande) : un renvoi après coupure rend le même accusé.</summary>
    private async Task<IssueDepot> EnvoyerAsync(string code, DemandeDepotWeb demande, IReadOnlyList<IFormFile> photos, CancellationToken annulation)
    {
        using var reponse = await SessionApi.AvecReessaisAsync(async () =>
        {
            using var formulaire = new MultipartFormDataContent { { JsonContent.Create(demande), "demande" } };
            foreach (var photo in photos)
            {
                var contenu = new StreamContent(photo.OpenReadStream());
                contenu.Headers.ContentType = MediaTypeHeaderValue.TryParse(photo.ContentType, out var type) ? type : null;
                formulaire.Add(contenu, "photos", "photo");
            }

            return await ClientEnvoi.PostAsync(new Uri($"/public/sites/{Uri.EscapeDataString(code)}/reports", UriKind.Relative), formulaire, annulation)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
        if (reponse.IsSuccessStatusCode)
        {
            return new IssueDepot(await reponse.Content.ReadFromJsonAsync<AccuseApi>(annulation).ConfigureAwait(false), reponse.StatusCode, null);
        }

        var champ = reponse.StatusCode == HttpStatusCode.UnprocessableEntity
            ? (await reponse.Content.ReadFromJsonAsync<ProblemeChamp>(annulation).ConfigureAwait(false))?.Champ
            : null;
        return new IssueDepot(null, reponse.StatusCode, champ);
    }

    private sealed record ProblemeChamp(string? Champ);
}

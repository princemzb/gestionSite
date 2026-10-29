using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Maintenance.DepotPublic;

/// <summary>
/// Anti-abus du formulaire public (décision du 28/10, SECURITE.md) : prouve qu'une personne a rempli le
/// formulaire, sans vérifier son identité. Remplaçable : seule cette interface est connue du dépôt.
/// </summary>
internal interface IVerificationHumaine
{
    Task<bool> VerifierAsync(string? jeton, CancellationToken cancellationToken);
}

internal sealed class TurnstileOptions
{
    internal const string Section = "Turnstile";

    /// <summary>Clé secrète du widget (secret d'environnement, jamais dans le dépôt).</summary>
    public string? CleSecrete { get; init; }

    /// <summary>Développement et tests seulement (refusé ailleurs au démarrage) : aucun contrôle.</summary>
    public bool Desactive { get; init; }
}

/// <summary>Dépôt public momentanément impossible (503) : le reste de l'application continue de fonctionner.</summary>
internal sealed class DepotIndisponibleException() : Exception("Le dépôt public est momentanément indisponible.");

internal sealed class SansVerification : IVerificationHumaine
{
    public Task<bool> VerifierAsync(string? jeton, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>Clé secrète absente hors développement : aucun dépôt accepté, sans arrêter les autres fonctions.</summary>
internal sealed partial class VerificationIndisponible(ILogger<VerificationIndisponible> journal) : IVerificationHumaine
{
    public Task<bool> VerifierAsync(string? jeton, CancellationToken cancellationToken)
    {
        CleAbsente(journal);
        throw new DepotIndisponibleException();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Turnstile:CleSecrete absente : dépôt public refusé (503).")]
    private static partial void CleAbsente(ILogger journal);
}

/// <summary>
/// Cloudflare Turnstile : le jeton produit dans le navigateur est validé auprès de Cloudflare avec la clé secrète.
/// Refus par défaut : jeton absent, réponse invalide ou service injoignable.
/// </summary>
internal sealed partial class VerificationTurnstile(HttpClient client, IOptions<TurnstileOptions> options, ILogger<VerificationTurnstile> journal)
    : IVerificationHumaine
{
    internal static readonly Uri Adresse = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    // Jetons Turnstile : au plus 2048 caractères (documentation Cloudflare).
    private const int JetonMax = 2048;

    public async Task<bool> VerifierAsync(string? jeton, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jeton) || jeton.Length > JetonMax)
        {
            return false;
        }

        using var contenu = new FormUrlEncodedContent([new("secret", options.Value.CleSecrete!), new("response", jeton)]);
        try
        {
            using var reponse = await client.PostAsync(Adresse, contenu, cancellationToken).ConfigureAwait(false);
            if (!reponse.IsSuccessStatusCode)
            {
                Injoignable(journal, (int)reponse.StatusCode);
                return false;
            }

            var resultat = await reponse.Content.ReadFromJsonAsync<ResultatTurnstile>(cancellationToken).ConfigureAwait(false);
            return resultat?.Success == true;
        }
        catch (Exception erreur) when (
            (erreur is HttpRequestException or TaskCanceledException or JsonException) && !cancellationToken.IsCancellationRequested)
        {
            Injoignable(journal, null);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vérification Turnstile impossible (statut {Statut}) : dépôt refusé.")]
    private static partial void Injoignable(ILogger journal, int? statut);

    private sealed record ResultatTurnstile([property: JsonPropertyName("success")] bool Success);
}

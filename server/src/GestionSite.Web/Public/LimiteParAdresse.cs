using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace GestionSite.Web.Public;

/// <summary>
/// Signalements publics par adresse de visiteur (décision du 28/10), en complément de l'anti-robot et de la limite
/// par site de l'API. Adresse lue dans l'en-tête du relais s'il est configuré (WebOptions.EnteteAdresseClient).
/// </summary>
internal sealed class LimiteParAdresse(IOptions<WebOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiteur = PartitionedRateLimiter.Create<string, string>(adresse =>
        RateLimitPartition.GetFixedWindowLimiter(adresse, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.LimiteDepotsParAdresse,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
        }));

    internal bool Accepter(HttpContext contexte)
    {
        using var autorisation = _limiteur.AttemptAcquire(Adresse(contexte));
        return autorisation.IsAcquired;
    }

    public void Dispose() => _limiteur.Dispose();

    private string Adresse(HttpContext contexte) =>
        options.Value.EnteteAdresseClient is { Length: > 0 } entete && contexte.Request.Headers[entete].ToString() is { Length: > 0 } relayee
            ? relayee
            : contexte.Connection.RemoteIpAddress?.ToString() ?? "inconnue";
}

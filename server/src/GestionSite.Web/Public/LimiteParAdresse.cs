using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace GestionSite.Web.Public;

/// <summary>
/// Signalements publics par adresse de visiteur (décision du 28/10), en complément de l'anti-robot et de la limite
/// par site de l'API. Seuls les envois aboutis comptent : une erreur de saisie, un échec de l'anti-robot ou une
/// panne ne pénalisent pas le visiteur. Adresse lue dans l'en-tête du relais s'il est configuré.
/// </summary>
internal sealed class LimiteParAdresse(IOptions<WebOptions> options, TimeProvider horloge)
{
    private static readonly TimeSpan Fenetre = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _envois = new(StringComparer.Ordinal);

    /// <summary>Faux si l'adresse a déjà atteint la limite dans la fenêtre.</summary>
    internal bool Permis(HttpContext contexte)
    {
        var envois = Envois(Adresse(contexte));
        lock (envois)
        {
            Purger(envois);
            return envois.Count < options.Value.LimiteDepotsParAdresse;
        }
    }

    /// <summary>À appeler quand l'API a accepté le signalement.</summary>
    internal void Compter(HttpContext contexte)
    {
        var envois = Envois(Adresse(contexte));
        lock (envois)
        {
            Purger(envois);
            envois.Enqueue(horloge.GetUtcNow());
        }
    }

    private Queue<DateTimeOffset> Envois(string adresse)
    {
        // Adresses sans envoi récent retirées au passage : la table reste bornée par l'activité des 10 dernières minutes.
        if (_envois.Count > 10_000)
        {
            foreach (var (cle, file) in _envois)
            {
                lock (file)
                {
                    Purger(file);
                    if (file.Count == 0)
                    {
                        _envois.TryRemove(cle, out _);
                    }
                }
            }
        }

        return _envois.GetOrAdd(adresse, _ => new Queue<DateTimeOffset>());
    }

    private void Purger(Queue<DateTimeOffset> envois)
    {
        var limite = horloge.GetUtcNow() - Fenetre;
        while (envois.TryPeek(out var plusAncien) && plusAncien <= limite)
        {
            envois.Dequeue();
        }
    }

    private string Adresse(HttpContext contexte) =>
        options.Value.EnteteAdresseClient is { Length: > 0 } entete && contexte.Request.Headers[entete].ToString() is { Length: > 0 } relayee
            ? relayee
            : contexte.Connection.RemoteIpAddress?.ToString() ?? "inconnue";
}

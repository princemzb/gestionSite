using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace GestionSite.Web.Session;

/// <summary>
/// Un jeton de renouvellement ne sert qu'une fois : l'API prend un second usage pour un vol et révoque la
/// session. Deux requêtes simultanées du même navigateur (deux onglets, double clic) ou une réponse perdue
/// présenteraient pourtant le même jeton. Le premier renouvellement est donc partagé quelques minutes :
/// les suivants reçoivent les mêmes nouveaux jetons sans rappeler l'API. Clé : empreinte du jeton.
/// Limite : une seule instance web (préproduction) ; à revoir si l'application web est répartie.
/// </summary>
internal sealed class RenouvellementsPartages(IMemoryCache cache)
{
    private static readonly TimeSpan Fenetre = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _verrous = new(StringComparer.Ordinal);

    internal async Task<JetonsApi?> RenouvelerAsync(string jetonRenouvellement, Func<Task<JetonsApi?>> renouveler)
    {
        var cle = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jetonRenouvellement)));
        if (cache.TryGetValue(cle, out JetonsApi? deja))
        {
            return deja;
        }

        var verrou = _verrous.GetOrAdd(cle, _ => new SemaphoreSlim(1, 1));
        await verrou.WaitAsync().ConfigureAwait(false);
        try
        {
            if (cache.TryGetValue(cle, out deja))
            {
                return deja;
            }

            var jetons = await renouveler().ConfigureAwait(false);
            if (jetons is not null)
            {
                cache.Set(cle, jetons, Fenetre);
            }

            return jetons;
        }
        finally
        {
            verrou.Release();
            _verrous.TryRemove(cle, out _);
        }
    }
}

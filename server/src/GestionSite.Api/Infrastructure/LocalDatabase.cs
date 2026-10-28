using System.Net;
using Npgsql;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// Reconnaît une base PostgreSQL locale : localhost, boucle locale ou socket Unix, pour chaque hôte listé.
/// Garde-fou contre l'erreur courante, pas une barrière : un tunnel (SSH, port-forward) vers une base
/// distante sur localhost reste vu comme local. La vraie barrière est le rôle sans droit de schéma.
/// </summary>
public static class LocalDatabase
{
    /// <summary>Dans le doute (chaîne illisible, aucun hôte), répond « non locale » : le garde-fou bloque.</summary>
    public static bool IsLocal(string connectionString)
    {
        string? hosts;
        try
        {
            hosts = new NpgsqlConnectionStringBuilder(connectionString).Host;
        }
        catch (ArgumentException)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(hosts) && hosts.Split(',').All(IsLocalHost);
    }

    private static bool IsLocalHost(string host)
    {
        var name = WithoutPort(host.Trim());
        return name.StartsWith('/') // socket Unix
            || string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(name, out var address) && IPAddress.IsLoopback(address));
    }

    // Formes acceptées par Npgsql : « hôte », « hôte:port », « [IPv6] », « [IPv6]:port », IPv6 nue.
    private static string WithoutPort(string host)
    {
        if (host.StartsWith('['))
        {
            var end = host.IndexOf(']', StringComparison.Ordinal);
            return end > 0 ? host[1..end] : host;
        }

        var colon = host.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && colon == host.LastIndexOf(':') ? host[..colon] : host;
    }
}

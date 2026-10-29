using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Maintenance.DepotPublic;

internal sealed class DepotPublicOptions
{
    internal const string Section = "DepotPublic";

    /// <summary>Signalements acceptés par site et par fenêtre (filet derrière l'anti-robot ; limite par adresse côté web).</summary>
    public int LimiteParSite { get; init; } = 20;

    public TimeSpan Fenetre { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>Trop de signalements acceptés pour ce site dans la fenêtre (429).</summary>
internal sealed class TropDeDepotsException() : Exception("Trop de signalements pour ce site : réessayez plus tard.");

/// <summary>
/// Limite de signalements par site (décision du 28/10). Ne compte que les signalements acceptés : après la
/// vérification anti-robot, sur le site trouvé en base. Une requête invalide, sans jeton ou rejouée ne consomme
/// rien : impossible de bloquer un site sans passer l'anti-robot, ni de contourner la limite par variantes du code.
/// </summary>
internal sealed class LimiteDepots(IOptions<DepotPublicOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _limiteur = PartitionedRateLimiter.Create<Guid, Guid>(site =>
        RateLimitPartition.GetFixedWindowLimiter(site, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.LimiteParSite,
            Window = options.Value.Fenetre,
            QueueLimit = 0,
        }));

    internal void Exiger(Guid siteId)
    {
        using var autorisation = _limiteur.AttemptAcquire(siteId);
        if (!autorisation.IsAcquired)
        {
            throw new TropDeDepotsException();
        }
    }

    public void Dispose() => _limiteur.Dispose();
}

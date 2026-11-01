using ImageMagick;

namespace GestionSite.SharedKernel.Photos;

/// <summary>
/// Bornes d'ImageMagick pour tout le processus (photos des formulaires, fonds de plan de bâtiment) : une image annonçant
/// des dimensions énormes est refusée avant allocation, sans repli sur le disque. Posées par l'API à son démarrage, avant
/// toute requête, puis de nouveau (sans effet) par chaque traitement : aucun décodage ne part sans elles.
/// </summary>
public static class LimitesImages
{
    /// <summary>Plus grand côté accepté, lu dans l'en-tête avant tout décodage.</summary>
    public const uint CoteMax = 12_000;

    /// <summary>50 Mpx : au-delà des photos de téléphone courantes et d'un plan numérisé en A1 à 200 ppp.</summary>
    public const ulong SurfaceMax = 50_000_000;

    public const ulong MemoireMax = 512UL * 1024 * 1024;

    private static readonly Lazy<bool> Posees = new(() =>
    {
        ResourceLimits.Width = CoteMax;
        ResourceLimits.Height = CoteMax;
        ResourceLimits.Area = SurfaceMax;
        ResourceLimits.Memory = MemoireMax;
        ResourceLimits.Disk = 0;
        return true;
    });

    /// <summary>Pose les bornes une fois pour tout le processus ; les appels suivants sont sans effet.</summary>
    public static void Poser() => _ = Posees.Value;
}

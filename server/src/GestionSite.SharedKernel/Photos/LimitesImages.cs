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

    /// <summary>
    /// Mémoire des pixels d'ImageMagick, commune à tous les décodages du processus (1 Gio, SOC-04). Mesurée le 01/11/2026
    /// (Magick.NET Q8) sur des images de 50 Mpx : jusqu'à 681 Mio pour un fond (PNG en palette, RVBA carré : 668 Mio,
    /// le réencodage PNG d'une image non réduite coûtant le plus) et 382 Mio pour une photo (JPEG CMJN avec profil),
    /// soit 764 Mio pour les deux places de décodage occupées par des photos. Le conteneur (4 Gio) garde la place du
    /// processus de rendu PDF (1 Gio) et de l'application web.
    /// </summary>
    public const ulong MemoireMax = 1024UL * 1024 * 1024;

    private static readonly Lazy<bool> Posees = new(() =>
    {
        ResourceLimits.Width = CoteMax;
        ResourceLimits.Height = CoteMax;
        // ImageMagick refuse une surface égale à sa borne : une image de 50 000 000 px exactement reste acceptée.
        ResourceLimits.Area = SurfaceMax + 1;
        ResourceLimits.Memory = MemoireMax;
        ResourceLimits.Disk = 0;
        return true;
    });

    /// <summary>Pose les bornes une fois pour tout le processus ; les appels suivants sont sans effet.</summary>
    public static void Poser() => _ = Posees.Value;
}

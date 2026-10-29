namespace GestionSite.Modules.Patrimoine.Contracts;

/// <summary>Lieu d'un dossier dans l'inventaire du site : zone obligatoire, composant actif facultatif.</summary>
public sealed record LieuInventaire(Guid ZoneId, string Zone, string Batiment, Guid? ComposantId, string? Composant, string? CorpsEtat)
{
    /// <summary>« Bâtiment · Zone ».</summary>
    public string Libelle => $"{Batiment} · {Zone}";
}

/// <summary>
/// Lecture de l'inventaire d'un site par les autres modules (US-09 : rattachement d'un dossier). À appeler après une
/// autorisation favorable sur le site : l'organisation courante est alors fixée (filtres et RLS).
/// </summary>
public interface IInventaireSite
{
    /// <summary>
    /// La zone du site et, s'il est donné, le composant de cette zone ; null si l'un est inconnu sur ce site. Pour un nouveau
    /// rattachement (<paramref name="composantActif"/>), seul un composant actif convient ; pour relire un rattachement
    /// existant, un composant retiré depuis reste nommé.
    /// </summary>
    Task<LieuInventaire?> LieuAsync(Guid siteId, Guid zoneId, Guid? composantId, bool composantActif, CancellationToken cancellationToken);

    /// <summary>Libellés « Bâtiment · Zone » des zones demandées qui existent sur le site.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ZonesAsync(Guid siteId, IReadOnlyCollection<Guid> zoneIds, CancellationToken cancellationToken);
}

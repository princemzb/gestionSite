namespace GestionSite.Modules.Patrimoine.Contracts;

/// <summary>Lieu d'un dossier dans l'inventaire du site : zone obligatoire, composant actif facultatif.</summary>
/// <param name="ZoneId"></param>
/// <param name="Zone"></param>
/// <param name="Batiment"></param>
/// <param name="ComposantId"></param>
/// <param name="Composant"></param>
/// <param name="CorpsEtat"></param>
/// <param name="PhotoApresIntervention">Le corps d'état du composant exige une photo à la clôture d'une intervention.</param>
public sealed record LieuInventaire(
    Guid ZoneId, string Zone, string Batiment, Guid? ComposantId, string? Composant, string? CorpsEtat, bool PhotoApresIntervention = false)
{
    /// <summary>« Bâtiment · Zone ».</summary>
    public string Libelle => $"{Batiment} · {Zone}";
}

/// <summary>Classement ERP déclaré d'un bâtiment (US-12) : types et réponses en noms exacts (« V », « Oui »…).</summary>
/// <param name="Erp"></param>
/// <param name="Type">Type principal (ERP seulement).</param>
/// <param name="TypesSecondaires"></param>
/// <param name="Categorie">1 à 5 (ERP seulement).</param>
/// <param name="Effectif">Effectif déclaré, facultatif (ERP seulement).</param>
/// <param name="LocauxSommeil"></param>
/// <param name="MonumentHistorique">Classé ou inscrit.</param>
/// <param name="PermisAvant1997">Oui, Non ou Inconnu.</param>
/// <param name="DerniereVisiteCommission"></param>
public sealed record ClassementBatiment(
    bool Erp, string? Type, IReadOnlyList<string> TypesSecondaires, int? Categorie, int? Effectif, bool LocauxSommeil, bool MonumentHistorique,
    string PermisAvant1997, DateOnly? DerniereVisiteCommission);

/// <summary>Bâtiment du site et son classement, null tant qu'il n'est pas déclaré.</summary>
/// <param name="Id"></param>
/// <param name="Nom"></param>
/// <param name="Classement"></param>
/// <param name="PartiesCommunesId">Zone « Parties communes » du bâtiment (créée avec lui).</param>
public sealed record BatimentInventaire(Guid Id, string Nom, ClassementBatiment? Classement, Guid? PartiesCommunesId);

/// <summary>Données du site pour le préventif : salariés présents (null : non déclaré) et bâtiments classés.</summary>
/// <param name="SalariesPresents"></param>
/// <param name="Batiments"></param>
public sealed record EtablissementInventaire(bool? SalariesPresents, IReadOnlyList<BatimentInventaire> Batiments);

/// <summary>Composant de l'inventaire suivi par le préventif (US-12).</summary>
/// <param name="Id"></param>
/// <param name="Nom"></param>
/// <param name="ZoneId"></param>
/// <param name="Zone">« Bâtiment · Zone ».</param>
/// <param name="BatimentId"></param>
/// <param name="CorpsEtat">Nom exact du corps d'état (« CourantsForts »…).</param>
/// <param name="SoumisAVerification"></param>
/// <param name="EnLot"></param>
/// <param name="Retire"></param>
public sealed record ComposantInventaire(
    Guid Id, string Nom, Guid ZoneId, string Zone, Guid BatimentId, string CorpsEtat, bool SoumisAVerification, bool EnLot, bool Retire);

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

    /// <summary>Salariés présents et bâtiments du site avec leur classement (US-12), bâtiments par nom.</summary>
    Task<EtablissementInventaire> EtablissementAsync(Guid siteId, CancellationToken cancellationToken);

    /// <summary>
    /// Composants du site : sans <paramref name="ids"/>, les composants actifs soumis à vérification ; sinon les composants
    /// demandés qui existent sur le site, retirés compris (relecture d'un plan).
    /// </summary>
    Task<IReadOnlyList<ComposantInventaire>> ComposantsAsync(Guid siteId, IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken);
}

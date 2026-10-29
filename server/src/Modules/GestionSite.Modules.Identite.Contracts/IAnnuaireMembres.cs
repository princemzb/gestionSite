namespace GestionSite.Modules.Identite.Contracts;

/// <summary>Membre actif d'un site et ses rôles sur ce site (« Technicien », « Responsable », « Delegue »…).</summary>
public sealed record MembreSite(Guid CompteId, string Nom, IReadOnlyCollection<string> Roles);

/// <summary>
/// Noms affichés des membres de l'organisation courante, pour nommer les auteurs d'un historique. À appeler après une
/// autorisation favorable (organisation fixée) : un compte étranger à l'organisation n'a pas de nom ici.
/// </summary>
public interface IAnnuaireMembres
{
    Task<IReadOnlyDictionary<Guid, string>> NomsAsync(IReadOnlyCollection<Guid> compteIds, CancellationToken cancellationToken);

    /// <summary>Membres actifs (invitation acceptée, non révoqués) ayant au moins un rôle sur le site, par nom.</summary>
    Task<IReadOnlyList<MembreSite>> MembresDuSiteAsync(Guid siteId, CancellationToken cancellationToken);
}

namespace GestionSite.Modules.Identite.Contracts;

/// <summary>Site atteint par son code public (dépôt public sans compte, RG-02 et RG-03).</summary>
public sealed record SitePublic(Guid OrganisationId, Guid SiteId, string NomSite, string NomOrganisation, string? NumeroUrgence);

/// <summary>
/// Accès public à un site par son code (lien ou QR). Un code trouvé devient l'organisation courante de la
/// requête (filtres et RLS) : à appeler avant d'ouvrir une transaction, comme IAutorisations.
/// </summary>
public interface ISitesPublics
{
    /// <summary>Le site du code, ou null si le code est mal formé ou inconnu (répondre 404).</summary>
    Task<SitePublic?> OuvrirAsync(string code, CancellationToken cancellationToken);
}

namespace GestionSite.Modules.Identite.Contracts;

/// <summary>Issue d'une vérification, à traduire en 2xx, 404 ou 403 par l'appelant.</summary>
public enum DecisionAutorisation
{
    Autorise,

    /// <summary>
    /// Organisation dont l'appelant n'est pas membre actif, ou site où il n'a aucun rôle : répondre 404,
    /// sans révéler l'existence de la ressource (CONTRATS, T-01.11).
    /// </summary>
    Introuvable,

    /// <summary>Ressource visible pour l'appelant mais action non permise par ses rôles : répondre 403.</summary>
    Refuse,
}

/// <summary>
/// Autorisation de l'appelant authentifié (compte courant) dans une organisation. Quand l'appelant est
/// membre actif, l'organisation devient l'organisation courante de la requête (filtres et RLS).
/// </summary>
public interface IAutorisations
{
    Task<DecisionAutorisation> VerifierAsync(Guid organisationId, Droit droit, Guid? siteId, CancellationToken cancellationToken);

    /// <summary>Sites de l'organisation sur lesquels l'appelant détient le droit (vide si non membre).</summary>
    Task<IReadOnlySet<Guid>> SitesAutorisesAsync(Guid organisationId, Droit droit, CancellationToken cancellationToken);
}

namespace GestionSite.Modules.Identite.Contracts;

/// <summary>
/// Issue d'une vérification, à traduire en 2xx, 404 ou 403 par l'appelant. La valeur par défaut (0) refuse :
/// une décision non initialisée ou un double de test non configuré n'autorise jamais.
/// </summary>
public enum DecisionAutorisation
{
    /// <summary>
    /// Organisation dont l'appelant n'est pas membre actif, ou site où il n'a aucun rôle : répondre 404,
    /// sans révéler l'existence de la ressource (CONTRATS, T-01.11).
    /// </summary>
    Introuvable,

    /// <summary>Ressource visible pour l'appelant mais action non permise par ses rôles : répondre 403.</summary>
    Refuse,

    Autorise,
}

/// <summary>
/// Autorisation de l'appelant authentifié (compte courant) dans une organisation. Quand l'appelant est
/// membre actif, l'organisation devient l'organisation courante de la requête (filtres et RLS).
/// À appeler avant d'ouvrir une transaction (l'organisation y est fixée à l'ouverture) ; une opération
/// sensible à une révocation concurrente verrouille ensuite ce qu'elle modifie dans sa transaction.
/// </summary>
public interface IAutorisations
{
    Task<DecisionAutorisation> VerifierAsync(Guid organisationId, Droit droit, Guid? siteId, CancellationToken cancellationToken);

    /// <summary>Sites de l'organisation sur lesquels l'appelant détient le droit (vide si non membre).</summary>
    Task<IReadOnlySet<Guid>> SitesAutorisesAsync(Guid organisationId, Droit droit, CancellationToken cancellationToken);
}

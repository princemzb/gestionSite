using GestionSite.SharedKernel;

namespace GestionSite.Modules.Identite.Contracts;

public static class ExigenceAutorisation
{
    /// <summary>
    /// Exige le droit sur le site, avant toute lecture : une décision favorable fixe l'organisation courante (filtres et
    /// RLS) ; sinon AccesRefuseException (404 hors périmètre, 403 site visible).
    /// </summary>
    public static async Task ExigerAsync(this IAutorisations autorisations, Guid organisationId, Guid siteId, Droit droit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(autorisations);
        var decision = await autorisations.VerifierAsync(organisationId, droit, siteId, cancellationToken).ConfigureAwait(false);
        if (decision != DecisionAutorisation.Autorise)
        {
            throw new AccesRefuseException(siteVisible: decision == DecisionAutorisation.Refuse);
        }
    }
}

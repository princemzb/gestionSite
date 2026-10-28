using GestionSite.Modules.Identite.Autorisation;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>Traduction des décisions d'autorisation en refus HTTP pour les opérations du module Identité.</summary>
internal sealed class AccesOrganisation(Autorisations autorisations)
{
    internal async Task<Membre> EntrerAsync(Guid organisationId, CancellationToken cancellationToken) =>
        await autorisations.AppelantAsync(organisationId, cancellationToken).ConfigureAwait(false) ?? throw RefusException.Introuvable();

    internal async Task<Membre> EntrerCommeAdministrateurAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        var appelant = await EntrerAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var decision = await autorisations.VerifierAsync(organisationId, Droit.GererComptes, siteId: null, cancellationToken).ConfigureAwait(false);
        return decision == DecisionAutorisation.Autorise ? appelant : throw RefusException.Interdit();
    }
}

using GestionSite.Modules.Identite.Contracts;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Maintenance.Dossiers;

/// <summary>
/// Personnes à qui confier un travail (interventions US-09, contrôles préventifs US-12, décision du 29/10) : un
/// intervenant interne est un technicien du site ; le référent d'un prestataire est un membre du site qui a le droit de
/// faire ce travail (technicien, responsable ou délégué).
/// </summary>
internal static class Intervenants
{
    private const string Technicien = "Technicien";

    internal static async Task ExigerAsync(
        IAnnuaireMembres annuaire, Guid siteId, Droit droit, Guid? intervenantCompteId, Guid? referentCompteId, CancellationToken cancellationToken)
    {
        var demandes = new[] { intervenantCompteId, referentCompteId }.Where(c => c is not null).Select(c => c!.Value).ToList();
        if (demandes.Count == 0)
        {
            return;
        }

        var terrain = (await annuaire.MembresAyantDroitAsync(siteId, droit, demandes, cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.CompteId);
        if (intervenantCompteId is { } intervenant && !(terrain.TryGetValue(intervenant, out var membre) && membre.Roles.Contains(Technicien)))
        {
            throw new SaisieInvalideException("intervenantCompteId", "Un technicien de ce site.");
        }

        if (referentCompteId is { } referent && !terrain.ContainsKey(referent))
        {
            throw new SaisieInvalideException("referentCompteId", "Technicien, responsable ou délégué de ce site.");
        }
    }
}

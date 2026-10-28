using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Modules.Identite.Autorisation;

/// <summary>
/// Traduction unique de docs/metier/PERMISSIONS.md (rôles V, décisions DEC-04). L'administrateur
/// n'obtient que l'administration : sans rôle métier, aucun accès aux opérations (« non implicite »).
/// </summary>
internal static class MatriceDroits
{
    private static readonly Role[] Internes = [Role.Technicien, Role.Responsable, Role.Delegue, Role.Pasteur, Role.Coordination];
    private static readonly Role[] Terrain = [Role.Technicien, Role.Responsable, Role.Delegue];
    private static readonly Role[] Encadrement = [Role.Responsable, Role.Delegue];

    private static readonly Dictionary<Droit, Role[]> Roles = new()
    {
        [Droit.ConsulterSite] = Internes,
        [Droit.DeposerSignalement] = Internes,
        [Droit.QualifierAffecter] = Encadrement,
        [Droit.DocumenterIntervention] = Terrain,
        [Droit.CloturerIntervention] = Terrain,
        [Droit.RouvrirArbitrer] = Encadrement,
        [Droit.Relancer] = [Role.Responsable, Role.Delegue, Role.Pasteur, Role.Coordination],
        [Droit.LancerIa] = Terrain,
        [Droit.EngagerBudget] = Encadrement,
        [Droit.EnregistrerAccordFinance] = Encadrement,
        [Droit.ConsommerStock] = Terrain,
        [Droit.ReceptionnerAjusterStock] = Encadrement,
        [Droit.VoirPosition] = Terrain,
        [Droit.LireContactsDeclarant] = Internes,
        [Droit.LireMontants] = [Role.Responsable, Role.Delegue, Role.Pasteur, Role.Coordination],
        [Droit.ModifierInventaire] = Terrain,
        [Droit.ModifierGrilles] = Encadrement,
        [Droit.ModifierPlans] = Encadrement,
        [Droit.GererComptes] = [Role.Administrateur],
        [Droit.AutoriserSupport] = [Role.Administrateur],
    };

    /// <summary>Permissions d'administration : toute l'organisation, jamais un site.</summary>
    internal static bool EstOrganisationnelle(Droit droit) =>
        droit is Droit.GererComptes or Droit.AutoriserSupport;

    internal static IReadOnlyCollection<Role> RolesPour(Droit droit) =>
        Roles.TryGetValue(droit, out var roles) ? roles : throw new ArgumentOutOfRangeException(nameof(droit), droit, "Droit inconnue.");
}

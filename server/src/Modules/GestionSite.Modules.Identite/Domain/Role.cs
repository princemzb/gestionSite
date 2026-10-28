namespace GestionSite.Modules.Identite.Domain;

/// <summary>Rôles cumulables (CONTRATS, US-06) ; stockés par leur nom.</summary>
internal enum Role
{
    /// <summary>A : toute l'organisation, jamais un site précis.</summary>
    Administrateur,
    Responsable,
    Delegue,
    Technicien,
    Pasteur,
    Coordination,
}

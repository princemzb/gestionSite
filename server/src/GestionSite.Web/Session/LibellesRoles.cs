namespace GestionSite.Web.Session;

/// <summary>Libellés des rôles (codes de l'API : « Technicien », « Delegue »…) et leur ordre d'affichage.</summary>
internal static class LibellesRoles
{
    /// <summary>Rôles accordés sur un site, dans l'ordre des listes de choix.</summary>
    internal static readonly (string Code, string Libelle)[] DeSite =
        [("Technicien", "Technicien"), ("Responsable", "Responsable"), ("Delegue", "Délégué"), ("Pasteur", "Pasteur"), ("Coordination", "Coordination")];

    internal static string Libelle(string role) => role == "Administrateur"
        ? "Administrateur"
        : DeSite.FirstOrDefault(r => r.Code == role).Libelle ?? role;

    /// <summary>« Technicien sur Église Notre-Dame ; Pasteur sur Chapelle » ; l'administrateur couvre toute l'organisation.</summary>
    internal static string Resume(IEnumerable<RoleInviteApi> roles) => string.Join(" ; ", roles.Select(r => r.SiteId is null
        ? "Administrateur de toute l'organisation"
        : $"{Libelle(r.Role)} sur {r.Site ?? "un site"}"));
}

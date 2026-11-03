using GestionSite.Web.Session;

namespace GestionSite.Web.Administration;

/// <summary>Journal des habilitations à l'écran (RG-31, US-31) : nature et détail d'une entrée en français.</summary>
internal static class LibellesJournal
{
    internal static string Nature(string nature) => nature switch
    {
        "Ouverture" => "Organisation ouverte",
        "Creation" => "Invitation créée",
        "Revision" => "Rôles modifiés",
        "Revocation" => "Accès révoqué",
        "Acceptation" => "Invitation acceptée",
        "Refus" => "Invitation refusée",
        "AttributionDroit" => "Droit « plans » accordé",
        "RetraitDroit" => "Droit « plans » retiré",
        "NouveauLienActivation" => "Nouveau lien d'activation",
        "LienReinitialisation" => "Lien de réinitialisation créé",
        "MotDePasseReinitialise" => "Mot de passe réinitialisé",
        _ => nature,
    };

    /// <summary>
    /// Ce qui a changé : rôles avant et après d'une révision, rôles d'une invitation, droit « plans » accordé ou retiré (avec le site).
    /// Vide quand la nature suffit (acceptation, liens).
    /// </summary>
    internal static IReadOnlyList<string> Details(EntreeJournalApi entree, IReadOnlyDictionary<Guid, string> sites)
    {
        ArgumentNullException.ThrowIfNull(entree);
        ArgumentNullException.ThrowIfNull(sites);
        var lignes = new List<string>();
        switch (entree.Nature)
        {
            case "Ouverture" or "Creation":
                lignes.Add(Roles(entree.RolesApres, sites));
                break;
            case "Revision":
                lignes.Add($"Avant : {Roles(entree.RolesAvant, sites)}");
                lignes.Add($"Après : {Roles(entree.RolesApres, sites)}");
                break;
        }

        var avant = entree.DroitsAvant ?? [];
        var apres = entree.DroitsApres ?? [];
        lignes.AddRange(apres.Except(avant).Select(d => $"« Modifier les plans » accordé sur {Site(d.SiteId, sites)}"));
        lignes.AddRange(avant.Except(apres).Select(d => $"« Modifier les plans » retiré sur {Site(d.SiteId, sites)}"));
        return [.. lignes.Where(l => l.Length > 0)];
    }

    /// <summary>« Technicien sur Église ; Pasteur sur Chapelle », ou « Administrateur de toute l'organisation ».</summary>
    internal static string Roles(IReadOnlyList<RoleApi>? roles, IReadOnlyDictionary<Guid, string> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        return roles is not { Count: > 0 }
            ? "aucun rôle"
            : string.Join(" ; ", roles.Select(r => r.SiteId is { } site
                ? $"{LibellesRoles.Libelle(r.Role)} sur {Site(site, sites)}"
                : "Administrateur de toute l'organisation"));
    }

    private static string Site(Guid siteId, IReadOnlyDictionary<Guid, string> sites) => sites.GetValueOrDefault(siteId) ?? "un site";
}

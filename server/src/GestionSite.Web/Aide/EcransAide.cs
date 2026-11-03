namespace GestionSite.Web.Aide;

/// <summary>Rubrique d'aide d'un écran : section principale du manuel, puis les autres tâches faites sur cet écran.</summary>
internal sealed record EcranAide(string Ancre, params string[] Autres);

/// <summary>Tâches courantes d'un groupe de rôles, en tête de la page Aide quand la personne a l'un de ces rôles.</summary>
internal sealed record TachesRole(string Surtitre, string Titre, string[] Roles, string[] Ancres);

/// <summary>
/// Correspondance entre les écrans livrés et les ancres stables du manuel (DOC-02). Un test vérifie que chaque ancre citée
/// existe et que chaque page de l'application déclare son écran (ou en est explicitement dispensée).
/// </summary>
internal static class EcransAide
{
    internal static readonly IReadOnlyDictionary<string, EcranAide> Ecrans = new Dictionary<string, EcranAide>(StringComparer.Ordinal)
    {
        ["connexion"] = new("connexion-se-connecter", "connexion-activer", "connexion-organisations", "faq-compte"),
        ["activation"] = new("connexion-activer", "connexion-se-connecter", "faq-compte"),
        ["etat"] = new("evaluations-etat-site", "evaluations-echelle", "evaluations-evaluer"),
        ["dossiers"] = new("dossiers-suivi", "dossiers-declarer", "dossiers-fiche", "dossiers-delais"),
        ["declarer"] = new("dossiers-declarer", "dossiers-suivi"),
        ["dossier"] = new("dossiers-fiche", "dossiers-qualifier", "dossiers-doublon", "dossiers-classer", "dossiers-affecter",
            "dossiers-demarrer", "dossiers-attente", "dossiers-cloturer", "dossiers-rouvrir"),
        ["interventions"] = new("dossiers-mes-interventions", "dossiers-demarrer", "dossiers-attente", "dossiers-cloturer"),
        ["depots"] = new("depot-recus", "dossiers-qualifier", "administration-sites"),
        ["inventaire"] = new("inventaire", "inventaire-structure", "inventaire-composant", "inventaire-lot", "inventaire-filtrer",
            "inventaire-modifier", "inventaire-historique"),
        ["evaluer"] = new("evaluations-evaluer", "evaluations-lot", "evaluations-echelle"),
        ["grilles"] = new("evaluations-grilles", "evaluations-echelle"),
        ["plans"] = new("plans", "plans-parcours", "plans-fond", "plans-calibrer", "plans-atelier", "plans-brouillon"),
        ["preventif"] = new("preventif-calendrier", "preventif-classement", "preventif-propositions", "preventif-affecter",
            "preventif-non-conformites"),
        ["propositions"] = new("preventif-propositions", "preventif-classement"),
        ["plan-preventif"] = new("preventif-affecter", "preventif-controler", "preventif-verification", "preventif-historique"),
        ["controler"] = new("preventif-controler", "preventif-verification"),
        ["controles"] = new("preventif-historique", "preventif-non-conformites"),
        ["controle"] = new("preventif-historique", "preventif-verification", "preventif-non-conformites"),
        ["sites"] = new("administration-sites", "administration-numero-urgence", "administration-affiche"),
        ["membres"] = new("administration-membres", "administration-inviter"),
    };

    /// <summary>Page Aide, proposition A (03/11) : tâches courantes par rôle ; libellés repris des titres du manuel.</summary>
    internal static readonly IReadOnlyList<TachesRole> Roles =
    [
        new("Encadrement du site", "Responsable, délégué", ["Responsable", "Delegue"],
            ["dossiers-qualifier", "dossiers-affecter", "preventif-propositions", "evaluations-grilles"]),
        new("Sur le terrain", "Technicien", ["Technicien"],
            ["dossiers-mes-interventions", "dossiers-cloturer", "evaluations-evaluer", "preventif-controler"]),
        new("Consultation", "Pasteur, coordination", ["Pasteur", "Coordination"],
            ["dossiers-declarer", "dossiers-suivi", "evaluations-etat-site"]),
        new("Organisation", "Administrateur", ["Administrateur"],
            ["administration-inviter", "administration-sites", "administration-numero-urgence"]),
    ];

    /// <summary>Groupes de tâches, ceux des rôles de la personne d'abord (ordre stable sinon).</summary>
    internal static IReadOnlyList<(TachesRole Taches, bool SesRoles)> RolesPour(IReadOnlyCollection<string> roles) =>
        [.. Roles.Select(t => (Taches: t, SesRoles: t.Roles.Any(roles.Contains))).OrderByDescending(t => t.SesRoles)];
}

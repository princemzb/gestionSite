using System.Globalization;

namespace GestionSite.Web.Dossiers;

/// <summary>Libellés du traitement des dossiers (US-09 : DEC-03, DEC-05, décisions du 29/10) : codes de l'API → texte affiché.</summary>
internal static class LibellesDossiers
{
    /// <summary>Colonnes du suivi (maquette B) : statuts ouverts, dans l'ordre du cycle.</summary>
    internal static readonly (string Code, string Libelle)[] ColonnesOuvertes =
        [("AQualifier", "À qualifier"), ("Affecte", "Affectés"), ("EnCours", "En cours"), ("EnAttente", "En attente")];

    internal static readonly (string Code, string Libelle, string Delais)[] Priorites =
        [("Urgente", "Urgente", "4 h · 2 j"), ("Haute", "Haute", "1 j · 7 j"), ("Normale", "Normale", "3 j · 30 j"), ("Basse", "Basse", "7 j · 90 j")];

    internal static readonly (string Code, string Libelle)[] MotifsClassement =
        [("AucunProbleme", "Aucun problème constaté"), ("HorsPerimetre", "Hors périmètre"), ("DejaTraite", "Déjà traité hors application"),
         ("SpamAbusif", "Spam ou abusif"), ("Autre", "Autre (commentaire)")];

    internal static readonly (string Code, string Libelle)[] MotifsAttente =
        [("Financement", "Financement ou accord finance"), ("Livraison", "Livraison de fournitures"), ("Prestataire", "Prestataire extérieur"),
         ("AccesImpossible", "Accès au lieu impossible"), ("DecisionInterne", "Décision interne"), ("Autre", "Autre (commentaire)")];

    internal static readonly (string Code, string Libelle)[] EtatsFinaux =
        [("Resolu", "Résolu"), ("MesureProvisoire", "Mesure provisoire (mis en sécurité, à reprendre)"), ("NonResolu", "Non résolu")];

    /// <summary>Étapes de la barre d'avancement (maquette B).</summary>
    internal static readonly (string Code, string Libelle)[] Etapes =
        [("AQualifier", "Déclaré"), ("Qualifie", "Qualifié"), ("Affecte", "Affecté"), ("EnCours", "En cours"), ("Cloture", "Clôturé")];

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    internal static string Statut(string code) => code switch
    {
        "AQualifier" => "À qualifier",
        "Affecte" => "Affecté",
        "EnCours" => "En cours",
        "EnAttente" => "En attente",
        "Cloture" => "Clôturé",
        "ClasseSansSuite" => "Classé sans suite",
        "RattacheDoublon" => "Rattaché à un doublon",
        _ => code,
    };

    internal static string StatutIntervention(string code) => code switch
    {
        "Affectee" => "Affectée",
        "EnCours" => "En cours",
        "EnAttente" => "En attente",
        "Cloturee" => "Clôturée",
        _ => code,
    };

    internal static string Libelle((string Code, string Libelle)[] liste, string? code) =>
        code is null ? "—" : liste.FirstOrDefault(e => e.Code == code).Libelle ?? code;

    internal static string Priorite(string? code) => code is null ? "Non qualifié" : Priorites.FirstOrDefault(p => p.Code == code).Libelle ?? code;

    internal static string ClassePriorite(string? code) => code switch
    {
        "Urgente" => "priorite-urgente",
        "Haute" => "priorite-haute",
        _ => "priorite-normale",
    };

    /// <summary>« dans 6 h », « dans 3 j », « dépassé de 1 j » (temps calendaire, DEC-05).</summary>
    internal static string Restant(DateTimeOffset echeance, DateTimeOffset maintenant)
    {
        var ecart = echeance - maintenant;
        var duree = ecart.Duration();
        var texte = duree.TotalHours < 48 ? $"{Math.Max(1, (int)Math.Ceiling(duree.TotalHours))} h" : $"{(int)Math.Ceiling(duree.TotalDays)} j";
        return ecart >= TimeSpan.Zero ? $"dans {texte}" : $"dépassé de {texte}";
    }

    internal static string Nature(string code) => code switch
    {
        "Declaration" => "Déclaré",
        "Qualification" => "Qualifié",
        "RattachementDoublon" => "Rattaché à un doublon",
        "ClassementSansSuite" => "Classé sans suite",
        "Affectation" => "Intervention affectée",
        "Demarrage" => "Intervention démarrée",
        "MiseEnAttente" => "Mise en attente",
        "Reprise" => "Reprise",
        "ClotureIntervention" => "Intervention clôturée",
        "Cloture" => "Dossier clôturé",
        "Reouverture" => "Rouvert",
        _ => code,
    };

    internal static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Francais);
}

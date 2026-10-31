using System.Globalization;
using GestionSite.Web.Session;

namespace GestionSite.Web.Preventif;

/// <summary>
/// Libellés du préventif (US-12) : codes de l'API → texte affiché. Le résultat déclaré d'un contrôle n'est jamais
/// présenté comme « conforme » (décision du 30/10) : « sans observation », « avec observations », « non satisfaisant ».
/// </summary>
internal static class LibellesPreventif
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    internal static readonly (string Code, string Libelle)[] Natures =
        [("InspectionInterne", "Inspection"), ("VerificationExterieure", "Vérification extérieure"), ("Entretien", "Entretien")];

    internal static readonly (string Code, string Libelle)[] Resultats =
        [("Conforme", "Sans observation"), ("AvecObservations", "Avec observations"), ("NonConforme", "Non satisfaisant")];

    internal static readonly (string Code, string Libelle)[] MotifsEcart =
        [("NonConcerne", "Non concerné"), ("SuiviAilleurs", "Suivi ailleurs (contrat, registre papier)"), ("Autre", "Autre (commentaire)")];

    internal static readonly (string Code, string Libelle)[] MotifsSaut =
        [("HorsService", "Installation hors service ou déposée"), ("Inaccessible", "Inaccessible"), ("Autre", "Autre (commentaire)")];

    internal static readonly (string Code, string Libelle)[] TypesErp =
    [
        ("V", "V · lieu de culte"), ("L", "L · salle de réunion, spectacles"), ("R", "R · enseignement, accueil"), ("N", "N · restauration"),
        ("S", "S · bibliothèque, documentation"), ("J", "J · structure d'accueil"), ("M", "M · magasin"), ("O", "O · hébergement"),
        ("P", "P · salle de danse, jeux"), ("T", "T · exposition"), ("U", "U · établissement de soins"), ("W", "W · bureaux"),
        ("X", "X · établissement sportif couvert"), ("Y", "Y · musée"),
    ];

    internal static readonly (string Code, string Libelle)[] Permis = [("Inconnu", "Inconnu"), ("Oui", "Oui"), ("Non", "Non")];

    internal static string Nature(string? code) => Libelle(Natures, code);

    internal static string Resultat(string? code) => Libelle(Resultats, code);

    internal static string MotifEcart(string? code) => Libelle(MotifsEcart, code);

    internal static string MotifSaut(string? code) => Libelle(MotifsSaut, code);

    internal static string Base(string? code) => code == "Reglementaire" ? "réglementaire" : "bonne pratique";

    internal static string Intervenant(string? code) => code switch
    {
        "TechnicienSite" => "technicien du site",
        "TechnicienCompetent" => "technicien compétent",
        "OrganismeAgree" => "organisme agréé",
        "Entreprise" => "entreprise",
        "CommissionSecurite" => "commission de sécurité",
        _ => code ?? "—",
    };

    /// <summary>Position d'une échéance : « À venir », « À faire », « En retard » ; statut enregistré sinon.</summary>
    internal static string Etat(string? code) => code switch
    {
        "AVenir" => "À venir",
        "AFaire" => "À faire",
        "EnRetard" => "En retard",
        "Affectee" => "Affectée",
        "Realisee" => "Réalisée",
        "RealiseeAvecReserves" => "Réalisée avec réserves",
        "ReservesLevees" => "Réserves levées",
        "Sautee" => "Sautée",
        _ => code ?? "—",
    };

    /// <summary>Classe de couleur d'une échéance : orange « à faire », rouge « en retard ».</summary>
    internal static string ClasseEtat(string? etat) => etat switch
    {
        "AFaire" => "echeance-a-faire",
        "EnRetard" => "echeance-en-retard",
        _ => "echeance-a-venir",
    };

    /// <summary>« tous les ans », « toutes les 6 semaines », « ponctuel ».</summary>
    internal static string Periode(PeriodeApi? periode) => periode switch
    {
        null => "ponctuel",
        { Valeur: 1, Unite: "Ans" } => "tous les ans",
        { Valeur: 1, Unite: "Mois" } => "tous les mois",
        { Valeur: 1, Unite: "Semaines" } => "toutes les semaines",
        { Unite: "Ans" } => $"tous les {periode.Valeur} ans",
        { Unite: "Mois" } => $"tous les {periode.Valeur} mois",
        _ => $"toutes les {periode.Valeur} semaines",
    };

    internal static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    internal static string DateCourte(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);

    internal static string Mois(DateOnly mois) => Francais.TextInfo.ToTitleCase(mois.ToString("MMMM yyyy", Francais));

    private static string Libelle((string Code, string Libelle)[] liste, string? code) =>
        liste.FirstOrDefault(l => l.Code == code).Libelle ?? code ?? "—";
}

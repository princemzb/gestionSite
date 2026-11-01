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

    /// <summary>Nature d'un événement du journal d'un plan.</summary>
    internal static string Journal(string nature) => nature switch
    {
        "Creation" => "Plan créé",
        "Modification" => "Règle modifiée",
        "Suspension" => "Plan suspendu",
        "Reprise" => "Plan repris",
        "Affectation" => "Échéance affectée",
        "Saut" => "Échéance sautée",
        "Realisation" => "Contrôle enregistré",
        _ => nature,
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

    /// <summary>
    /// Refus de l'enregistrement d'un contrôle (POST perform) : message et étape où reprendre (« points », « etat »,
    /// « debut » pour la première étape d'un entretien ou d'une vérification, « resultat », ou null : étape inchangée).
    /// </summary>
    internal static (string Message, string? Etape) RefusRealisation(string? code, string? champ, System.Net.HttpStatusCode statut) => (code, champ) switch
    {
        ("grille_modifiee", _) => ("La grille de ce corps d'état vient d'être modifiée : relisez les points à observer.", "points"),
        ("commande_reutilisee", _) => ("Cet envoi a déjà servi : recommencez l'enregistrement.", "resultat"),
        ("composant_retire", _) => ("Ce composant vient d'être retiré de l'inventaire : le contrôle ne s'enregistre plus.", "resultat"),
        ("modifie_entre_temps", _) => ("Quelqu'un d'autre vient de modifier cette échéance ou ce lot : vérifiez puis recommencez.", "resultat"),
        ("statut_incompatible", _) => ("Ce plan est suspendu, sa cible n'a pas de grille, ou cette échéance est déjà traitée : revenez à la fiche du plan.", "resultat"),
        (_, "echeance") => ("Cette échéance n'est plus l'échéance courante du plan : revenez à la fiche du plan.", "resultat"),
        (_, "photos") => ("Photos : 3 au plus, de 10 Mo chacune (JPEG, PNG ou HEIC) ; obligatoire pour un état mauvais ou critique.", "resultat"),
        (_, "rapport") => ("Rapport : un fichier PDF de 10 Mo au plus, obligatoire pour une vérification réglementaire par un prestataire.", "resultat"),
        (_, "realiseLe") => ("Date de réalisation : aujourd'hui ou avant, 5 ans au plus.", "resultat"),
        (_, "resultat") => ("Choisissez le résultat déclaré ; un défaut ou une observation exclut « Sans observation ».", "resultat"),
        (_, "compteRendu") => ("Compte rendu : obligatoire pour un entretien, 2000 caractères au plus.", "resultat"),
        (_, "prestataire") => ("Prestataire : une ligne visible de 120 caractères au plus.", "debut"),
        (_, "numeroAgrement") => ("N° d'agrément : celui d'un prestataire nommé, 60 caractères au plus.", "debut"),
        (_, "observations") => ("Observations : 50 au plus, de 300 caractères chacune, dans une zone du site.", "debut"),
        (_, "releves") => ("Relevés : 8 au plus ; libellé de 80 caractères, unité de 16, valeur numérique.", "debut"),
        (_, "etat") => ("Choisissez l'état constaté.", "etat"),
        (_, "repartition") => ("La somme des quantités par état doit égaler la quantité du lot.", "etat"),
        (_, "points" or "mesures" or "grilleVersion") => ("Vérifiez les points et les mesures.", "points"),
        _ when statut == System.Net.HttpStatusCode.Forbidden =>
            ("Ce contrôle ne vous est pas confié : seuls l'intervenant affecté et les responsables du site le saisissent.", null),
        _ when statut == System.Net.HttpStatusCode.NotFound => ("Ce plan n'existe plus sur ce site.", null),
        _ => ("Service momentanément indisponible : réessayez dans un instant.", null),
    };

    /// <summary>Constat d'un point de grille : jamais « conforme » (décision du 30/10).</summary>
    internal static string Point(bool? conforme, bool danger) => (conforme, danger) switch
    {
        (true, _) => "Sans défaut",
        (false, true) => "Défaut, danger signalé",
        (false, false) => "Défaut",
        _ => "Non vérifié",
    };

    /// <summary>Champ libre de la dernière étape : remarque d'une inspection, compte rendu exigé pour un entretien.</summary>
    internal static string ChampCompteRendu(string nature) => nature switch
    {
        "InspectionInterne" => "Remarque (facultative)",
        "Entretien" => "Compte rendu (obligatoire)",
        _ => "Compte rendu (facultatif)",
    };

    /// <summary>« 2 dossiers ouverts, 1 constat rattaché à un dossier existant » après l'enregistrement d'un contrôle.</summary>
    internal static string Ecarts(int nouveaux, int rattaches) => string.Join(", ", new[]
    {
        nouveaux > 0 ? $"{nouveaux} dossier{Pluriel(nouveaux)} ouvert{Pluriel(nouveaux)}" : null,
        rattaches > 0 ? $"{rattaches} constat{Pluriel(rattaches)} rattaché{Pluriel(rattaches)} à un dossier existant" : null,
    }.Where(t => t is not null));

    /// <summary>Taille d'un rapport en Ko ou Mo.</summary>
    internal static string Taille(int octets) => octets < 1024 * 1024
        ? $"{Math.Max(1, (int)Math.Round(octets / 1024d, MidpointRounding.AwayFromZero))} Ko"
        : $"{(octets / (1024d * 1024)).ToString("0.0", Francais)} Mo";

    /// <summary>Valeur d'une mesure ou d'un relevé, virgule française (« 48,5 °C ») ; « non relevée » sans valeur.</summary>
    internal static string Mesure(decimal? valeur, string unite) =>
        valeur is { } v ? $"{v.ToString("0.###", Francais)} {unite}".TrimEnd() : "non relevée";

    internal static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    internal static string DateCourte(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);

    internal static string Mois(DateOnly mois) => Francais.TextInfo.ToTitleCase(mois.ToString("MMMM yyyy", Francais));

    private static string Pluriel(int nombre) => nombre > 1 ? "s" : "";

    private static string Libelle((string Code, string Libelle)[] liste, string? code) =>
        liste.FirstOrDefault(l => l.Code == code).Libelle ?? code ?? "—";
}

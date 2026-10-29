namespace GestionSite.Web.Inventaire;

/// <summary>Libellés de l'inventaire (REGLES « Inventaire ») : codes de l'API → texte affiché.</summary>
internal static class Libelles
{
    internal static readonly (string Code, string Libelle)[] CorpsEtats =
    [
        ("StructureGrosOeuvre", "Structure et gros œuvre"),
        ("CouvertureEtancheite", "Couverture et étanchéité"),
        ("MenuiseriesExterieures", "Menuiseries extérieures et vitraux"),
        ("MenuiseriesInterieures", "Menuiseries intérieures et serrurerie"),
        ("RevetementsFinitions", "Revêtements et finitions"),
        ("CourantsForts", "Électricité courants forts"),
        ("EclairageSecurite", "Éclairage de sécurité"),
        ("SecuriteIncendie", "Sécurité incendie"),
        ("CourantsFaibles", "Courants faibles"),
        ("ChauffageVentilation", "Chauffage, ventilation, climatisation"),
        ("Plomberie", "Plomberie et sanitaires"),
        ("Gaz", "Gaz"),
        ("Ascenseurs", "Ascenseurs et élévateurs"),
        ("Paratonnerre", "Paratonnerre et protection foudre"),
        ("Campanaire", "Campanaire"),
        ("Accessibilite", "Accessibilité PMR"),
        ("Mobilier", "Mobilier"),
        ("EquipementsCulte", "Équipements de culte"),
        ("ExterieursVoirie", "Extérieurs et voirie"),
        ("Autre", "Autre"),
    ];

    internal static readonly (string Code, string Libelle, string Abrege)[] Unites =
    [
        ("Unite", "unité", "unités"), ("MetreCarre", "m²", "m²"), ("MetreLineaire", "mètre linéaire", "ml"),
        ("MetreCube", "m³", "m³"), ("Ensemble", "ensemble", "ensembles"),
    ];

    /// <summary>États de l'échelle commune, du meilleur au pire, avec leur classe de couleur.</summary>
    internal static readonly (string Cle, string Libelle, string Classe)[] Etats =
    [
        ("bon", "Bon", "etat-bon"), ("satisfaisant", "Satisfaisant", "etat-satisfaisant"), ("degrade", "Dégradé", "etat-degrade"),
        ("mauvais", "Mauvais", "etat-mauvais"), ("critique", "Critique", "etat-critique"),
    ];

    internal static readonly string[] Mois =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    private static readonly Dictionary<string, string> Champs = new(StringComparer.Ordinal)
    {
        ["nom"] = "Nom",
        ["corpsEtat"] = "Corps d'état",
        ["zoneId"] = "Zone",
        ["zone"] = "Zone",
        ["emplacement"] = "Emplacement précis",
        ["marque"] = "Marque",
        ["modele"] = "Modèle",
        ["numeroSerie"] = "N° de série",
        ["anneeMiseEnService"] = "Année de mise en service",
        ["moisMiseEnService"] = "Mois de mise en service",
        ["remarques"] = "Remarques",
        ["soumisAVerification"] = "Soumis à vérification",
        ["unite"] = "Unité",
        ["quantite"] = "Quantité",
        ["lot.unite"] = "Unité du lot",
        ["lot.quantite"] = "Quantité du lot",
        ["lot.repartition"] = "Répartition par état",
        ["bon"] = "Bon",
        ["satisfaisant"] = "Satisfaisant",
        ["degrade"] = "Dégradé",
        ["mauvais"] = "Mauvais",
        ["critique"] = "Critique",
        ["motif"] = "Motif",
        ["ordre"] = "Ordre",
        ["niveauId"] = "Niveau",
        ["batimentId"] = "Bâtiment",
        ["recherche"] = "Recherche",
        ["version"] = "Version",
    };

    // Ordre des champs de la fiche : l'historique les présente ainsi (jsonb ne garde pas l'ordre d'écriture).
    private static readonly string[] OrdreChamps =
    [
        "nom", "corpsEtat", "zone", "emplacement", "marque", "modele", "numeroSerie", "anneeMiseEnService", "moisMiseEnService",
        "remarques", "soumisAVerification", "unite", "quantite", "bon", "satisfaisant", "degrade", "mauvais", "critique",
    ];

    internal static int Rang(string champ) => Array.IndexOf(OrdreChamps, champ) is var rang and >= 0 ? rang : OrdreChamps.Length;

    internal static string CorpsEtat(string code) => CorpsEtats.FirstOrDefault(c => c.Code == code).Libelle ?? code;

    internal static string Unite(string code, int quantite) =>
        Unites.FirstOrDefault(u => u.Code == code) is { Code: not null } u ? (quantite > 1 ? u.Abrege : u.Libelle) : code;

    internal static string Champ(string? champ) => champ is not null && Champs.TryGetValue(champ, out var libelle) ? libelle : champ ?? "Saisie";

    /// <summary>Échelle commune (DEC-01) : code de l'API, libellé, note, classe de couleur ; du meilleur au pire.</summary>
    internal static readonly (string Code, string Libelle, int Note, string Classe)[] Echelle =
    [
        ("Bon", "Bon", 9, "etat-bon"), ("Satisfaisant", "Satisfaisant", 7, "etat-satisfaisant"), ("Degrade", "Dégradé", 5, "etat-degrade"),
        ("Mauvais", "Mauvais", 3, "etat-mauvais"), ("Critique", "Critique", 1, "etat-critique"),
    ];

    internal static string Etat(string? code) => Echelle.FirstOrDefault(e => e.Code == code).Libelle ?? code ?? "Non évalué";

    internal static string ClasseEtat(string? code) => Echelle.FirstOrDefault(e => e.Code == code).Classe ?? "etat-aucun";

    /// <summary>Note affichée à une décimale, virgule française (« 6,8 ») ; l'API l'a déjà arrondie.</summary>
    internal static string Note(decimal note) => note.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    internal static string Pourcentage(int part, int total) => total == 0 ? "—" : $"{Math.Round(100m * part / total, MidpointRounding.AwayFromZero)} %";

    internal static string MiseEnService(int? annee, int? mois) =>
        annee is null ? "—" : mois is { } m and >= 1 and <= 12 ? $"{Mois[m - 1]} {annee}" : $"{annee}";
}

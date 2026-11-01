using System.Globalization;

namespace GestionSite.Web.Inventaire;

/// <summary>
/// Saisie pas à pas avec la grille d'un corps d'état, commune à l'évaluation (US-11) et à l'inspection préventive (US-12) :
/// points observés, mesures, état d'un composant ou répartition d'un lot. Propriétés publiques : lues du formulaire.
/// </summary>
internal abstract class SaisieGrille
{
    public Guid CommandeId { get; set; }

    public int? GrilleVersion { get; set; }

    public int Etape { get; set; } = 1;

    public string? Action { get; set; }

    // Une valeur par point, dans l'ordre de la grille (« oui », « non », vide : non vérifié ; « danger » pour une inspection).
    public List<string?>? Constats { get; set; } = [];

    // Texte saisi (virgule ou point), une valeur par mesure de la grille.
    public List<string?>? Mesures { get; set; } = [];

    public string? Etat { get; set; }

    public int? Bon { get; set; }

    public int? Satisfaisant { get; set; }

    public int? Degrade { get; set; }

    public int? Mauvais { get; set; }

    public int? Critique { get; set; }

    /// <summary>Photo exigée par l'évaluation pour un état mauvais ou critique (DEC-01, US-11).</summary>
    internal bool PhotoExigee => Etat is "Mauvais" or "Critique" || Mauvais > 0 || Critique > 0;

    // Listes absentes du formulaire (étape envoyée sans elles) : lues comme vides.
    internal string Constat(int rang) => Constats is { } constats && rang < constats.Count ? constats[rang] ?? "" : "";

    internal string? Mesure(int rang) => Mesures is { } mesures && rang < mesures.Count ? mesures[rang] : null;

    internal int? Nombre(string cle) => cle switch
    {
        "bon" => Bon,
        "satisfaisant" => Satisfaisant,
        "degrade" => Degrade,
        "mauvais" => Mauvais,
        _ => Critique,
    };

    /// <summary>Répartition d'un lot envoyée à l'API (quantités absentes : zéro).</summary>
    internal object Repartition() =>
        new { bon = Bon ?? 0, satisfaisant = Satisfaisant ?? 0, degrade = Degrade ?? 0, mauvais = Mauvais ?? 0, critique = Critique ?? 0 };

    // Mesures lues (vide : non relevée) ; null si l'une n'est pas un nombre.
    internal List<decimal?>? MesuresLues(int nombre)
    {
        var lues = new List<decimal?>(nombre);
        for (var i = 0; i < nombre; i++)
        {
            if (!Decimal(Mesure(i), out var valeur))
            {
                return null;
            }

            lues.Add(valeur);
        }

        return lues;
    }

    internal string? ErreurRepartition(int quantite)
    {
        var nombres = new[] { Bon, Satisfaisant, Degrade, Mauvais, Critique };
        if (nombres.Any(n => n < 0))
        {
            return "Des quantités positives ou nulles.";
        }

        var total = nombres.Sum(n => n ?? 0);
        return total == quantite ? null : $"Répartition : {total} pour une quantité de {quantite}.";
    }

    /// <summary>Nombre saisi (virgule ou point, trois décimales au plus) ; vide : null ; faux s'il est illisible.</summary>
    internal static bool Decimal(string? texte, out decimal? valeur)
    {
        valeur = null;
        var nettoye = texte?.Trim().Replace(',', '.');
        if (string.IsNullOrEmpty(nettoye))
        {
            return true;
        }

        if (decimal.TryParse(nettoye, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var lu)
            && Math.Round(lu, 3) == lu)
        {
            valeur = lu;
            return true;
        }

        return false;
    }
}

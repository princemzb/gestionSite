using System.Numerics;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>
/// Nombre rationnel exact (fraction réduite, dénominateur positif) pour les moyennes de notes : « précision complète au
/// calcul » (RG-12). Une moyenne de moyennes en décimal arrondit chaque étape (19/3, 49/6…) et une note exacte de 7,25
/// deviendrait 7,2499…, affichée 7,2 au lieu de 7,3.
/// </summary>
internal readonly record struct Rationnel : IComparable<Rationnel>
{
    private static readonly BigInteger Echelle20 = BigInteger.Pow(10, 20);

    private Rationnel(BigInteger numerateur, BigInteger denominateur)
    {
        if (denominateur.IsZero)
        {
            throw new DivideByZeroException("Dénominateur nul.");
        }

        var pgcd = BigInteger.GreatestCommonDivisor(numerateur, denominateur);
        var signe = denominateur.Sign;
        Numerateur = signe * numerateur / pgcd;
        Denominateur = signe * denominateur / pgcd;
    }

    internal BigInteger Numerateur { get; }

    internal BigInteger Denominateur { get; }

    internal static Rationnel Fraction(BigInteger numerateur, BigInteger denominateur) => new(numerateur, denominateur);

    internal static Rationnel Entier(long valeur) => new(valeur, BigInteger.One);

    /// <summary>Moyenne exacte d'une liste non vide.</summary>
    internal static Rationnel Moyenne(IReadOnlyCollection<Rationnel> valeurs)
    {
        if (valeurs.Count == 0)
        {
            throw new ArgumentException("Moyenne d'une liste vide.", nameof(valeurs));
        }

        var somme = valeurs.Aggregate(Entier(0), (total, v) => new Rationnel((total.Numerateur * v.Denominateur) + (v.Numerateur * total.Denominateur), total.Denominateur * v.Denominateur));
        return new Rationnel(somme.Numerateur, somme.Denominateur * valeurs.Count);
    }

    /// <summary>Valeur arrondie au dixième, la moitié vers le haut (0,05 → 0,1), calculée exactement (valeurs positives).</summary>
    internal decimal Dixieme()
    {
        // ⌊10 n / d + 1/2⌋ = ⌊(20 n + d) / 2 d⌋
        var dixiemes = BigInteger.Divide((20 * Numerateur) + Denominateur, 2 * Denominateur);
        return (decimal)dixiemes / 10m;
    }

    /// <summary>Approximation décimale à 20 décimales (valeurs bornées : notes, parts), sans dépassement du type decimal.</summary>
    internal decimal Approche() => (decimal)BigInteger.Divide(Numerateur * Echelle20, Denominateur) / 100_000_000_000_000_000_000m;

    public int CompareTo(Rationnel other) => (Numerateur * other.Denominateur).CompareTo(other.Numerateur * Denominateur);
}

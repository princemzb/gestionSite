using GestionSite.Modules.Patrimoine.Domain;

namespace GestionSite.Modules.Tests;

/// <summary>Échelle commune et libellés des notes (DEC-01, RG-12).</summary>
public sealed class EchelleTests
{
    [Theory]
    [InlineData(796, 100, 8.0, "Bon")] // le libellé suit le chiffre affiché
    [InlineData(794, 100, 7.9, "Satisfaisant")]
    [InlineData(595, 100, 6.0, "Satisfaisant")] // 0,05 arrondi vers le haut
    [InlineData(4, 1, 4.0, "Degrade")]
    [InlineData(195, 100, 2.0, "Mauvais")]
    [InlineData(1, 1, 1.0, "Critique")]
    [InlineData(1, 3, 0.3, "Critique")]
    public void TheLabelFollowsTheDisplayedRoundedScore(long numerateur, long denominateur, double affichee, string libelle)
    {
        var note = Rationnel.Fraction(numerateur, denominateur);

        Assert.Equal((decimal)affichee, Echelle.Arrondie(note));
        Assert.Equal(libelle, Echelle.Libelle(note).ToString());
    }

    [Fact]
    public void ALotScoreIsTheQuantityWeightedMeanAtFullPrecision()
    {
        Assert.Equal(Rationnel.Fraction(78, 10), Echelle.Note(null, new Repartition(30, 0, 8, 0, 2)));
        Assert.Equal(Rationnel.Fraction(11, 3), Echelle.Note(null, new Repartition(1, 0, 0, 0, 2))); // 3,666…, jamais arrondie au calcul
        Assert.Equal(3.66666666666666666666m, Rationnel.Fraction(11, 3).Approche()); // 20 décimales, tronquées
        Assert.Equal(Rationnel.Entier(5), Echelle.Note(EtatComposant.Degrade, null));
        Assert.True(Echelle.Critique(null, new Repartition(39, 0, 0, 0, 1)));
        Assert.True(Rationnel.Fraction(19, 3) < Rationnel.Fraction(49, 6));
        Assert.True(Rationnel.Fraction(2, 4) == Rationnel.Fraction(1, 2));
        Assert.Equal(Rationnel.Entier(0), default); // valeur par défaut : zéro, jamais une division par zéro
        Assert.Equal(0m, default(Rationnel).Dixieme());
    }
}

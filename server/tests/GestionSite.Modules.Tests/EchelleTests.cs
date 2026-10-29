using GestionSite.Modules.Patrimoine.Domain;

namespace GestionSite.Modules.Tests;

/// <summary>Échelle commune et libellés des notes (DEC-01, RG-12).</summary>
public sealed class EchelleTests
{
    [Theory]
    [InlineData(7.96, 8.0, "Bon")] // le libellé suit le chiffre affiché
    [InlineData(7.94, 7.9, "Satisfaisant")]
    [InlineData(5.95, 6.0, "Satisfaisant")] // 0,05 arrondi vers le haut
    [InlineData(4.0, 4.0, "Degrade")]
    [InlineData(1.95, 2.0, "Mauvais")]
    [InlineData(1.0, 1.0, "Critique")]
    public void TheLabelFollowsTheDisplayedRoundedScore(double note, double affichee, string libelle)
    {
        Assert.Equal((decimal)affichee, Echelle.Arrondie((decimal)note));
        Assert.Equal(libelle, Echelle.Libelle((decimal)note).ToString());
    }

    [Fact]
    public void ALotScoreIsTheQuantityWeightedMeanAtFullPrecision()
    {
        Assert.Equal(7.8m, Echelle.Note(new Repartition(30, 0, 8, 0, 2)));
        Assert.Equal(11m / 3, Echelle.Note(new Repartition(1, 0, 0, 0, 2))); // 3,666…, jamais arrondie au calcul
    }
}

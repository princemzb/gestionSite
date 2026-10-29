using GestionSite.Web.Inventaire;
using GestionSite.Web.Session;

namespace GestionSite.Api.Tests;

/// <summary>Libellés de l'inventaire et de l'évaluation (US-07, US-11) : échelle, répartition, notes, textes bornés.</summary>
public sealed class LibellesInventaireTests
{
    [Fact]
    public void TheScaleIsDerivedFromTheStatesWithApiCodesAndNotes()
    {
        Assert.Equal(["Bon", "Satisfaisant", "Degrade", "Mauvais", "Critique"], Libelles.Echelle.Select(e => e.Code));
        Assert.Equal([9, 7, 5, 3, 1], Libelles.Echelle.Select(e => e.Note));
        Assert.Equal("Dégradé", Libelles.Etat("Degrade"));
        Assert.Equal("Non évalué", Libelles.Etat(null));
        Assert.Equal("etat-critique", Libelles.ClasseEtat("Critique"));
        Assert.Equal("etat-aucun", Libelles.ClasseEtat(null));
    }

    [Fact]
    public void ADistributionListsPresentStatesFromBestToWorst()
    {
        var repartition = new RepartitionApi(32, 0, 8, 0, 1);

        Assert.Equal("32 bon, 8 dégradé, 1 critique", Libelles.Repartition(cle => Libelles.Nombre(repartition, cle)));
        Assert.Equal(0, Libelles.Nombre(repartition, "mauvais"));
    }

    [Theory]
    [InlineData(7.8, "7,8")]
    [InlineData(1, "1,0")]
    public void NotesShowOneDecimalWithAFrenchComma(decimal note, string affichee) => Assert.Equal(affichee, Libelles.Note(note));

    [Theory]
    [InlineData(1, 3, "33 %")]
    [InlineData(1, 8, "13 %")] // 12,5 arrondi vers le haut
    [InlineData(0, 0, "—")]
    public void PercentagesRoundHalfUp(int part, int total, string attendu) => Assert.Equal(attendu, Libelles.Pourcentage(part, total));

    [Fact]
    public void TruncationNeverSplitsAComposedCharacter()
    {
        var texte = new string('a', 9) + "🙂" + "fin";

        Assert.Equal(new string('a', 9), Libelles.Tronquer(texte, 10)); // l'émoji (2 unités) ne tient pas : coupé avant
        Assert.Equal(texte[..11], Libelles.Tronquer(texte, 11));
        Assert.Equal("court", Libelles.Tronquer("court", 10));
        Assert.Null(Libelles.Tronquer(null, 10));
    }
}

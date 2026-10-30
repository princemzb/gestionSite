using GestionSite.Modules.Maintenance.Domain;

namespace GestionSite.Modules.Tests;

/// <summary>Constats d'inspection (US-12, S12-05) : clé de critère normalisée, dossier ouvert par un constat.</summary>
public sealed class ConstatControleTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Fuite d'eau", "fuite d eau")]
    [InlineData("  FUITE   d’EAU !! ", "fuite d eau")]
    [InlineData("Écaillage du béton (côté nord)", "ecaillage du beton cote nord")]
    [InlineData("…", "")]
    public void TextsAreNormalisedWithoutAccentsCaseOrPunctuation(string texte, string attendu)
    {
        Assert.Equal(attendu, CleConstat.Normaliser(texte));
    }

    [Fact]
    public void KeysSeparateTradesAndObservationsAndStayBounded()
    {
        Assert.Equal("point:Plomberie:fuite", CleConstat.Point("Plomberie", "Fuite !"));
        Assert.Equal("point:-:fuite", CleConstat.Point(null, "fuite"));
        Assert.NotEqual(CleConstat.Point("Plomberie", "Fuite"), CleConstat.Point("Gaz", "Fuite"));
        Assert.Equal("observation:prise arrachee", CleConstat.Observation("Prise  ARRACHÉE"));
        Assert.Equal(ConstatControle.CleMax, CleConstat.Observation(new string('a', 1000)).Length);
    }

    [Fact]
    public void AnInspectionCaseIsToQualifyWithItsPlaceAndCriterion()
    {
        var composant = Guid.NewGuid();
        var zone = Guid.NewGuid();
        var controle = Guid.NewGuid();
        var compte = Guid.NewGuid();

        var dossier = Signalement.Constat(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('x', 2500), "Église · Nef", zone, composant, danger: true,
            compte, "Marc", "point:Plomberie:fuite", controle, Guid.NewGuid(), Instant);

        Assert.Equal((OrigineDossier.Inspection, StatutDossier.AQualifier), (dossier.Origine, dossier.Statut));
        Assert.Equal((zone, composant, controle), (dossier.ZoneId!.Value, dossier.ComposantId!.Value, dossier.ControleId!.Value));
        Assert.Equal(("point:Plomberie:fuite", compte), (dossier.CleConstat, dossier.DeclarantCompteId!.Value));
        Assert.Equal(Signalement.DescriptionMax, dossier.Description.Length); // description raccourcie, jamais refusée
        Assert.Equal("Église · Nef", dossier.Emplacement);
        Assert.True(dossier.Danger);
    }
}

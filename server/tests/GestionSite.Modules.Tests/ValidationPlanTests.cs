using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Tests;

/// <summary>Validation géométrique des plans de bâtiment (US-21, S21-02, RP-21.2 à RP-21.7).</summary>
public sealed class ValidationPlanTests
{
    private static readonly Guid Niveau = Guid.NewGuid();
    private static readonly Guid Etage = Guid.NewGuid();
    private static readonly Guid Nef = Guid.NewGuid();
    private static readonly Guid Sacristie = Guid.NewGuid();
    private static readonly Guid Bureau = Guid.NewGuid();
    private static readonly Guid AutreNiveau = Guid.NewGuid();
    private static readonly Guid Communes = Guid.NewGuid();
    private static readonly Guid Extincteur = Guid.NewGuid();
    private static readonly Guid Baes = Guid.NewGuid();
    private static readonly Guid Retire = Guid.NewGuid();
    private static readonly Guid Calice = Guid.NewGuid();
    private static readonly Guid AEtage = Guid.NewGuid();

    private static readonly CadrePlan Cadre = new(
        Niveau,
        new Dictionary<Guid, ZoneCadre>
        {
            [Nef] = new(Nef, Niveau, false),
            [Sacristie] = new(Sacristie, Niveau, false),
            [Bureau] = new(Bureau, Niveau, false),
            [AutreNiveau] = new(AutreNiveau, Etage, false),
            [Communes] = new(Communes, null, true),
        },
        new HashSet<Guid> { Etage },
        new Dictionary<Guid, ComposantCadre>
        {
            [Extincteur] = new(Extincteur, Nef, CorpsEtat.SecuriteIncendie, false),
            [Baes] = new(Baes, Sacristie, CorpsEtat.EclairageSecurite, false),
            [Retire] = new(Retire, Nef, CorpsEtat.Mobilier, true),
            [Calice] = new(Calice, Sacristie, CorpsEtat.EquipementsCulte, false),
            [AEtage] = new(AEtage, AutreNiveau, CorpsEtat.Mobilier, false),
        });

    [Fact]
    public void AValidPlanIsKeptInMetresRoundedToTheCentimetre()
    {
        var plan = ValidationPlan.Valider(Plan(), Cadre);

        Assert.Empty(plan.Avertissements);
        Assert.Equal([Nef, Sacristie], plan.Contenu.Pieces.Select(p => p.ZoneId));
        Assert.Equal(new PointPlan(10.01, 0), plan.Contenu.Pieces[0].Contour[1]); // 10,006 m
        Assert.Equal(12.5, plan.Contenu.Nord);
        Assert.Equal(1, plan.Contenu.Schema);
        Assert.Equal(3.2, plan.Contenu.HauteurSousPlafond);
        Assert.Equal("Porte", plan.Contenu.Ouvertures[0].Type);
        Assert.Equal(Etage, plan.Contenu.Escaliers[0].VersNiveauId);
        Assert.Equal(new MarqueurPlan(Extincteur, Nef, new PointPlan(2, 2)), plan.Contenu.Composants[0]);
    }

    [Theory]
    [InlineData(1.125, 1.13)]
    [InlineData(-1.125, -1.13)]
    [InlineData(1.005, 1)] // 1,00499… en binaire, comme dans l'éditeur
    [InlineData(-0.001, 0)]
    public void RoundingIsSymmetricAndMatchesTheEditor(double metres, double attendu)
    {
        var arrondi = ValidationPlan.Centimetre(metres);

        Assert.Equal(attendu, arrondi);
        Assert.False(double.IsNegative(arrondi) && arrondi == 0); // jamais -0
    }

    [Fact]
    public void NorthJustBelow360DegreesWrapsToZero() =>
        Assert.Equal(0, ValidationPlan.Valider(Plan() with { Nord = 359.97 }, Cadre).Contenu.Nord);

    [Fact]
    public void MarkersOutsideTheirZoneAreWarnedNotRefused()
    {
        var plan = ValidationPlan.Valider(Plan() with
        {
            Composants = [Marqueur(Extincteur, 15, 2), Marqueur(Baes, 12, 3)],
            Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 8), (0, 8))],
            Ouvertures = [],
        }, Cadre);

        Assert.Equal(
            [new AvertissementPlan(ValidationPlan.MarqueurHorsZone, Extincteur), new AvertissementPlan(ValidationPlan.MarqueurHorsZone, Baes)],
            plan.Avertissements);
    }

    [Fact]
    public void RoomsMayShareAWallOrOverlapByFiveCentimetresAtMost()
    {
        var partage = ValidationPlan.Valider(Plan() with { Ouvertures = [] }, Cadre);
        var quatreCentimetres = ValidationPlan.Valider(Plan() with
        {
            Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 8), (0, 8)), Piece(Sacristie, (9.96, 0), (14, 0), (14, 8), (9.96, 8))],
            Ouvertures = [],
        }, Cadre);

        Assert.Equal(2, partage.Contenu.Pieces.Count);
        Assert.Equal(2, quatreCentimetres.Contenu.Pieces.Count);
    }

    [Fact]
    public void AnEmptyPlanIsValid()
    {
        var plan = ValidationPlan.Valider(new SaisieContenuPlan(1, null, null, null, null, null, null, null), Cadre);

        Assert.Null(plan.Contenu.Nord);
        Assert.Empty(plan.Contenu.Ouvertures);
        Assert.Empty(plan.Contenu.Escaliers);
        Assert.Empty(plan.Contenu.Composants);
        Assert.Empty(plan.Contenu.Pieces);
    }

    public static TheoryData<string, string> Refus() => new()
    {
        { "schema", "contenu.schema" },
        { "nord", "contenu.nord" },
        { "altitude", "contenu.altitude" },
        { "hauteur", "contenu.hauteurSousPlafond" },
        { "deux sommets", "contenu.pieces[0].contour" },
        { "auto-sécant", "contenu.pieces[0].contour" },
        { "sommet répété", "contenu.pieces[0].contour" },
        { "trop petite", "contenu.pieces[0].contour" },
        { "trop étroite", "contenu.pieces[1].contour" },
        { "hors bornes", "contenu.pieces[0].contour[2]" },
        { "coordonnée absente", "contenu.pieces[0].contour[1]" },
        { "pièce absente", "contenu.pieces[1]" },
        { "chevauchement", "contenu.pieces[1].contour" },
        { "zone d'un autre niveau", "contenu.pieces[0].zoneId" },
        { "zone inconnue", "contenu.pieces[0].zoneId" },
        { "parties communes", "contenu.pieces[0].zoneId" },
        { "zone dessinée deux fois", "contenu.pieces[1].zoneId" },
        { "trop de pièces", "contenu.pieces" },
        { "trop de sommets", "contenu.pieces[0].contour" },
        { "porte hors arête", "contenu.ouvertures[0].zones" },
        { "porte vers une zone non dessinée", "contenu.ouvertures[0].zones" },
        { "porte sans zone", "contenu.ouvertures[0].zones" },
        { "porte trop étroite", "contenu.ouvertures[0].a" },
        { "ouverture inconnue", "contenu.ouvertures[0].type" },
        { "escalier vers son niveau", "contenu.escaliers[0].versNiveauId" },
        { "escalier inconnu", "contenu.escaliers[0].type" },
        { "composant retiré", "contenu.composants[0].composantId" },
        { "objet de valeur", "contenu.composants[0].composantId" },
        { "composant d'un autre niveau", "contenu.composants[0].composantId" },
        { "composant inconnu", "contenu.composants[0].composantId" },
        { "composant placé deux fois", "contenu.composants[1].composantId" },
        { "position absente", "contenu.composants[0].position" },
    };

    [Theory]
    [MemberData(nameof(Refus))]
    public void InvalidPlansAreRefusedWithTheFaultyField(string cas, string champ)
    {
        var plan = Plan();
        var invalide = cas switch
        {
            "schema" => plan with { Schema = 2 },
            "nord" => plan with { Nord = 360 },
            "altitude" => plan with { Altitude = 1_000.01 },
            "hauteur" => plan with { HauteurSousPlafond = 0 },
            "deux sommets" => plan with { Pieces = [Piece(Nef, (0, 0), (10, 0))], Ouvertures = [] },
            "auto-sécant" => plan with { Pieces = [Piece(Nef, (0, 0), (10, 10), (10, 0), (0, 10))], Ouvertures = [] },
            "sommet répété" => plan with { Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 0.001), (0, 8))], Ouvertures = [] },
            "trop petite" => plan with { Pieces = [Piece(Nef, (0, 0), (0.4, 0), (0.4, 0.4), (0, 0.4))], Ouvertures = [] },
            "trop étroite" => plan with
            {
                Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 8), (0, 8)), Piece(Sacristie, (1, 1), (8, 1), (8, 1.04), (1, 1.04))],
                Ouvertures = [],
            },
            "hors bornes" => plan with { Pieces = [Piece(Nef, (0, 0), (10, 0), (5_000.01, 8))], Ouvertures = [] },
            "coordonnée absente" => plan with { Pieces = [new SaisiePiece(Nef, [new(0, 0), new(10, null), new(10, 8)])], Ouvertures = [] },
            "pièce absente" => plan with { Pieces = [plan.Pieces![0], null], Ouvertures = [] },
            "chevauchement" => plan with
            {
                Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 8), (0, 8)), Piece(Sacristie, (9.94, 0), (14, 0), (14, 8), (9.94, 8))],
                Ouvertures = [],
            },
            "zone d'un autre niveau" => plan with { Pieces = [Piece(AutreNiveau, (0, 0), (10, 0), (10, 8))], Ouvertures = [], Composants = [] },
            "zone inconnue" => plan with { Pieces = [Piece(Guid.NewGuid(), (0, 0), (10, 0), (10, 8))], Ouvertures = [], Composants = [] },
            "parties communes" => plan with { Pieces = [Piece(Communes, (0, 0), (10, 0), (10, 8))], Ouvertures = [], Composants = [] },
            "zone dessinée deux fois" => plan with
            {
                Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 8)), Piece(Nef, (20, 0), (30, 0), (30, 8))],
                Ouvertures = [],
            },
            "trop de pièces" => plan with { Pieces = [.. Enumerable.Range(0, ValidationPlan.PiecesMax + 1).Select(_ => plan.Pieces![0])] },
            "trop de sommets" => plan with
            {
                Pieces = [new SaisiePiece(Nef, [.. Enumerable.Range(0, ValidationPlan.SommetsMax + 1)
                    .Select(i => (SaisiePoint?)new SaisiePoint(10 * Math.Cos(i * 2 * Math.PI / 501), 10 * Math.Sin(i * 2 * Math.PI / 501)))])],
                Ouvertures = [],
            },
            "porte hors arête" => plan with { Ouvertures = [Ouverture("Porte", (5, 4), (6, 4), Nef)] },
            "porte vers une zone non dessinée" => plan with { Ouvertures = [Ouverture("Porte", (10, 3), (10, 4), Bureau)] },
            "porte sans zone" => plan with { Ouvertures = [Ouverture("Porte", (10, 3), (10, 4))] },
            "porte trop étroite" => plan with { Ouvertures = [Ouverture("Porte", (10, 3), (10, 3.1), Nef)] },
            "ouverture inconnue" => plan with { Ouvertures = [Ouverture("Trappe", (10, 3), (10, 4), Nef)] },
            "escalier vers son niveau" => plan with { Escaliers = [Escalier("Escalier", Niveau)] },
            "escalier inconnu" => plan with { Escaliers = [Escalier("Echelle", Etage)] },
            "composant retiré" => plan with { Composants = [Marqueur(Retire, 2, 2)] },
            "objet de valeur" => plan with { Composants = [Marqueur(Calice, 12, 2)] },
            "composant d'un autre niveau" => plan with { Composants = [Marqueur(AEtage, 2, 2)] },
            "composant inconnu" => plan with { Composants = [Marqueur(Guid.NewGuid(), 2, 2)] },
            "composant placé deux fois" => plan with { Composants = [Marqueur(Extincteur, 2, 2), Marqueur(Extincteur, 3, 3)] },
            "position absente" => plan with { Composants = [new SaisieMarqueur(Extincteur, null)] },
            _ => throw new ArgumentOutOfRangeException(nameof(cas), cas, null),
        };

        var refus = Assert.Throws<SaisieInvalideException>(() => ValidationPlan.Valider(invalide, Cadre));

        Assert.Equal(champ, refus.Champ);
    }

    [Fact]
    public void ALevelLessSitePlanHasNoStairs()
    {
        var masse = Cadre with { NiveauId = null, Zones = new Dictionary<Guid, ZoneCadre> { [Nef] = new(Nef, null, false) } };

        var refus = Assert.Throws<SaisieInvalideException>(() => ValidationPlan.Valider(
            Plan() with { Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 8), (0, 8))], Ouvertures = [], Composants = [] }, masse));
        var sansEscalier = ValidationPlan.Valider(
            Plan() with { Pieces = [Piece(Nef, (0, 0), (10, 0), (10, 8), (0, 8))], Ouvertures = [], Composants = [], Escaliers = [] }, masse);

        Assert.Equal("contenu.escaliers[0].versNiveauId", refus.Champ);
        Assert.Single(sansEscalier.Contenu.Pieces);
    }

    [Fact]
    public void AMissingContentIsRefused() =>
        Assert.Equal("contenu", Assert.Throws<SaisieInvalideException>(() => ValidationPlan.Valider(null, Cadre)).Champ);

    [Fact]
    public void ValuablesAreNeverPlaced()
    {
        Assert.True(ValidationPlan.ObjetDeValeur(CorpsEtat.EquipementsCulte));
        Assert.False(ValidationPlan.ObjetDeValeur(CorpsEtat.SecuriteIncendie));
    }

    // Nef (10 × 8 m) et sacristie (4 × 8 m) côte à côte, porte sur le mur commun, escalier vers l'étage, extincteur dans la nef.
    private static SaisieContenuPlan Plan() => new(
        1, 12.5, 0, 3.2,
        [Piece(Nef, (0, 0), (10.006, 0), (10, 8), (0, 8)), Piece(Sacristie, (10, 0), (14, 0), (14, 8), (10, 8))],
        [Ouverture("Porte", (10, 3), (10, 3.9), Nef, Sacristie)],
        [Escalier("Escalier", Etage)],
        [Marqueur(Extincteur, 2, 2)]);

    private static SaisiePiece Piece(Guid zone, params (double X, double Y)[] sommets) =>
        new(zone, [.. sommets.Select(s => (SaisiePoint?)new SaisiePoint(s.X, s.Y))]);

    private static SaisieOuverture Ouverture(string type, (double X, double Y) de, (double X, double Y) a, params Guid[] zones) =>
        new(type, new SaisiePoint(de.X, de.Y), new SaisiePoint(a.X, a.Y), zones);

    private static SaisieLiaison Escalier(string type, Guid vers) =>
        new(type, [new SaisiePoint(1, 6), new SaisiePoint(3, 6), new SaisiePoint(3, 7.5), new SaisiePoint(1, 7.5)], vers);

    private static SaisieMarqueur Marqueur(Guid composant, double x, double y) => new(composant, new SaisiePoint(x, y));
}

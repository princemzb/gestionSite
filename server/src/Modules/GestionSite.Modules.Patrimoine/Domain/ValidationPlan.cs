using GestionSite.SharedKernel;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

namespace GestionSite.Modules.Patrimoine.Domain;

// Saisie reçue, champs nullables : un champ absent est une saisie invalide (422), jamais une valeur par défaut silencieuse.
internal sealed record SaisiePoint(double? X, double? Y);

internal sealed record SaisiePiece(Guid? ZoneId, IReadOnlyList<SaisiePoint?>? Contour);

internal sealed record SaisieOuverture(string? Type, SaisiePoint? De, SaisiePoint? A, IReadOnlyList<Guid>? Zones);

internal sealed record SaisieLiaison(string? Type, IReadOnlyList<SaisiePoint?>? Contour, Guid? VersNiveauId);

internal sealed record SaisieMarqueur(Guid? ComposantId, SaisiePoint? Position);

internal sealed record SaisieContenuPlan(
    int? Schema, double? Nord, double? Altitude, double? HauteurSousPlafond, IReadOnlyList<SaisiePiece?>? Pieces,
    IReadOnlyList<SaisieOuverture?>? Ouvertures, IReadOnlyList<SaisieLiaison?>? Escaliers, IReadOnlyList<SaisieMarqueur?>? Composants);

internal enum TypeOuverture
{
    Porte = 1,
    Baie = 2,
    Fenetre = 3,
}

internal enum TypeLiaison
{
    Escalier = 1,
    Ascenseur = 2,
}

/// <summary>Zone du bâtiment du plan, telle que l'inventaire la connaît.</summary>
internal sealed record ZoneCadre(Guid Id, Guid? NiveauId, bool PartiesCommunes);

/// <summary>Composant du site désigné par un marqueur.</summary>
internal sealed record ComposantCadre(Guid Id, Guid ZoneId, CorpsEtat CorpsEtat, bool Retire);

/// <summary>
/// Ce que l'inventaire permet de dessiner sur un plan : son niveau (absent pour un plan masse), les zones de son bâtiment,
/// les autres niveaux du bâtiment (cibles des escaliers) et les composants du site désignés par la saisie.
/// </summary>
internal sealed record CadrePlan(
    Guid? NiveauId, IReadOnlyDictionary<Guid, ZoneCadre> Zones, IReadOnlySet<Guid> AutresNiveaux, IReadOnlyDictionary<Guid, ComposantCadre> Composants);

internal sealed record PlanValide(ContenuPlan Contenu, IReadOnlyList<AvertissementPlan> Avertissements);

/// <summary>
/// Validation d'un plan de bâtiment, schéma 1 (RP-21.2 à RP-21.7, US-21), côté serveur quelle que soit celle de
/// l'éditeur. Coordonnées en mètres arrondies au centimètre, bornées à ±5 km du repère ; nombres de pièces, sommets et
/// marqueurs bornés. Un refus désigne le champ fautif (« contenu.pieces[2].contour »).
/// </summary>
internal static class ValidationPlan
{
    internal const double Borne = 5_000;
    internal const int PiecesMax = 300;
    internal const int SommetsMax = 500;
    internal const int PointsMax = 20_000;
    internal const int OuverturesMax = 1_000;
    internal const int EscaliersMax = 100;
    internal const int MarqueursMax = 2_000;
    internal const double AireMin = 0.25;
    internal const double ChevauchementMax = 0.05;
    internal const double ToleranceArete = 0.05;
    internal const double OuvertureMin = 0.2;
    internal const double OuvertureMax = 30;
    internal const string MarqueurHorsZone = "marqueur_hors_zone";

    private static readonly GeometryFactory Fabrique = NtsGeometryServices.Instance.CreateGeometryFactory();

    internal static PlanValide Valider(SaisieContenuPlan? saisie, CadrePlan cadre)
    {
        ArgumentNullException.ThrowIfNull(cadre);
        var contenu = saisie ?? throw Refus("contenu");
        if (contenu.Schema != VersionPlanBatiment.SchemaCourant)
        {
            throw Refus("contenu.schema");
        }

        var nord = Facultatif(contenu.Nord, "contenu.nord", n => n is >= 0 and < 360, 10);
        if (nord >= 360)
        {
            nord = 0; // 359,97° s'arrondit au dixième à 360°, soit 0°
        }

        var altitude = Facultatif(contenu.Altitude, "contenu.altitude", a => a is >= -100 and <= 1_000, 100);
        var hauteur = Facultatif(contenu.HauteurSousPlafond, "contenu.hauteurSousPlafond", h => h is > 0 and <= 30, 100);
        var points = 0;
        var pieces = Pieces(Liste(contenu.Pieces, PiecesMax, "contenu.pieces"), cadre, ref points);
        var escaliers = Escaliers(Liste(contenu.Escaliers, EscaliersMax, "contenu.escaliers"), cadre, ref points);
        var ouvertures = Ouvertures(Liste(contenu.Ouvertures, OuverturesMax, "contenu.ouvertures"), pieces);
        var (marqueurs, avertissements) = Marqueurs(Liste(contenu.Composants, MarqueursMax, "contenu.composants"), cadre, pieces);
        return new PlanValide(
            new ContenuPlan(VersionPlanBatiment.SchemaCourant, nord, altitude, hauteur, [.. pieces.Select(p => p.Piece)], ouvertures, escaliers, marqueurs), avertissements);
    }

    /// <summary>Arrondi au centimètre, le demi-centimètre s'éloignant de zéro, jamais -0 (même règle que l'éditeur, ADR-11).</summary>
    internal static double Centimetre(double metres) => Arrondi(metres, 100);

    private static double Arrondi(double valeur, int parUnite)
    {
        var arrondi = Math.Round(valeur * parUnite, MidpointRounding.AwayFromZero) / parUnite;
        return arrondi == 0 ? 0 : arrondi;
    }

    // Valeur saisie contrôlée dans son domaine, puis arrondie.
    private static double? Facultatif(double? valeur, string champ, Func<double, bool> permise, int parUnite) => valeur switch
    {
        null => null,
        { } v when double.IsFinite(v) && permise(v) => Arrondi(v, parUnite),
        _ => throw Refus(champ),
    };

    private static List<T> Liste<T>(IReadOnlyList<T?>? liste, int max, string champ) where T : class
    {
        if (liste is null)
        {
            return [];
        }

        if (liste.Count > max)
        {
            throw Refus(champ);
        }

        return [.. liste.Select((element, rang) => element ?? throw Refus($"{champ}[{rang}]"))];
    }

    private static List<(PiecePlan Piece, Polygon Forme)> Pieces(List<SaisiePiece> saisies, CadrePlan cadre, ref int points)
    {
        var pieces = new List<(PiecePlan Piece, Polygon Forme)>();
        var zones = new HashSet<Guid>();
        for (var rang = 0; rang < saisies.Count; rang++)
        {
            var champ = $"contenu.pieces[{rang}]";
            var saisie = saisies[rang];
            // Zone de ce bâtiment, du niveau du plan (aucun niveau : plan masse), hors Parties communes, une fois par version.
            if (saisie.ZoneId is not { } zoneId || !cadre.Zones.TryGetValue(zoneId, out var zone) || zone.PartiesCommunes
                || zone.NiveauId != cadre.NiveauId || !zones.Add(zoneId))
            {
                throw Refus($"{champ}.zoneId");
            }

            var (contour, forme) = Contour(saisie.Contour, $"{champ}.contour", ref points);
            pieces.Add((new PiecePlan(zoneId, contour), forme));
        }

        // Chevauchement de plus de 5 cm entre deux pièces : chacune réduite de 2,5 cm, leurs intérieurs se rencontrent encore.
        // Une pièce plus étroite que 5 cm partout disparaîtrait à la réduction et échapperait au contrôle : refusée.
        var reduites = pieces.Select(p => p.Forme.Buffer(-ChevauchementMax / 2)).ToList();
        var etroite = reduites.FindIndex(r => r.IsEmpty);
        if (etroite >= 0)
        {
            throw Refus($"contenu.pieces[{etroite}].contour");
        }

        for (var i = 0; i < pieces.Count; i++)
        {
            for (var j = i + 1; j < pieces.Count; j++)
            {
                if (pieces[i].Forme.EnvelopeInternal.Intersects(pieces[j].Forme.EnvelopeInternal)
                    && reduites[i].Intersects(reduites[j]) && !reduites[i].Touches(reduites[j]))
                {
                    throw Refus($"contenu.pieces[{j}].contour");
                }
            }
        }

        return pieces;
    }

    private static List<LiaisonPlan> Escaliers(List<SaisieLiaison> saisies, CadrePlan cadre, ref int points)
    {
        var escaliers = new List<LiaisonPlan>();
        for (var rang = 0; rang < saisies.Count; rang++)
        {
            var champ = $"contenu.escaliers[{rang}]";
            var saisie = saisies[rang];
            if (!NomExact.Essayer<TypeLiaison>(saisie.Type, out var type))
            {
                throw Refus($"{champ}.type");
            }

            // Vers un autre niveau du même bâtiment ; un plan masse (sans niveau) n'en a pas.
            if (cadre.NiveauId is null || saisie.VersNiveauId is not { } vers || !cadre.AutresNiveaux.Contains(vers))
            {
                throw Refus($"{champ}.versNiveauId");
            }

            var (contour, _) = Contour(saisie.Contour, $"{champ}.contour", ref points);
            escaliers.Add(new LiaisonPlan(type.ToString(), contour, vers));
        }

        return escaliers;
    }

    private static List<OuverturePlan> Ouvertures(List<SaisieOuverture> saisies, List<(PiecePlan Piece, Polygon Forme)> pieces)
    {
        var formes = pieces.ToDictionary(p => p.Piece.ZoneId, p => p.Forme);
        var ouvertures = new List<OuverturePlan>();
        for (var rang = 0; rang < saisies.Count; rang++)
        {
            var champ = $"contenu.ouvertures[{rang}]";
            var saisie = saisies[rang];
            if (!NomExact.Essayer<TypeOuverture>(saisie.Type, out var type))
            {
                throw Refus($"{champ}.type");
            }

            var de = Point(saisie.De, $"{champ}.de");
            var a = Point(saisie.A, $"{champ}.a");
            var largeur = Centimetre(Math.Sqrt(((a.X - de.X) * (a.X - de.X)) + ((a.Y - de.Y) * (a.Y - de.Y))));
            if (largeur is < OuvertureMin or > OuvertureMax)
            {
                throw Refus($"{champ}.a");
            }

            // Une ou deux pièces dessinées sur ce plan (une porte relie deux pièces, ou une pièce et l'extérieur), et le
            // segment posé sur une arête de l'une d'elles, à 5 cm près.
            var zones = saisie.Zones ?? [];
            if (zones.Count is 0 or > 2 || zones.Distinct().Count() != zones.Count || !zones.All(formes.ContainsKey)
                || !zones.Any(z => SurUneArete(formes[z], de, a)))
            {
                throw Refus($"{champ}.zones");
            }

            ouvertures.Add(new OuverturePlan(type.ToString(), de, a, [.. zones]));
        }

        return ouvertures;
    }

    private static (List<MarqueurPlan> Marqueurs, List<AvertissementPlan> Avertissements) Marqueurs(
        List<SaisieMarqueur> saisies, CadrePlan cadre, List<(PiecePlan Piece, Polygon Forme)> pieces)
    {
        var formes = pieces.ToDictionary(p => p.Piece.ZoneId, p => p.Forme);
        var marqueurs = new List<MarqueurPlan>();
        var avertissements = new List<AvertissementPlan>();
        var places = new HashSet<Guid>();
        for (var rang = 0; rang < saisies.Count; rang++)
        {
            var champ = $"contenu.composants[{rang}]";
            var saisie = saisies[rang];
            // Composant actif du site, dans une zone de ce bâtiment et de ce niveau, placé une seule fois. Les objets de
            // valeur (équipements de culte) ne sont jamais placés (décision du porteur du 30/10, RP-21.7).
            if (saisie.ComposantId is not { } composantId || !cadre.Composants.TryGetValue(composantId, out var composant)
                || composant.Retire || ObjetDeValeur(composant.CorpsEtat)
                || !cadre.Zones.TryGetValue(composant.ZoneId, out var zone) || zone.NiveauId != cadre.NiveauId || !places.Add(composantId))
            {
                throw Refus($"{champ}.composantId");
            }

            var position = Point(saisie.Position, $"{champ}.position");
            marqueurs.Add(new MarqueurPlan(composantId, zone.Id, position));
            if (!formes.TryGetValue(zone.Id, out var forme) || !forme.Covers(Fabrique.CreatePoint(new Coordinate(position.X, position.Y))))
            {
                avertissements.Add(new AvertissementPlan(MarqueurHorsZone, composantId));
            }
        }

        return (marqueurs, avertissements);
    }

    /// <summary>Objet de valeur (trésor, objets de culte, coffre) : jamais de marqueur sur un plan (décision du 30/10).</summary>
    internal static bool ObjetDeValeur(CorpsEtat corpsEtat) => corpsEtat == CorpsEtat.EquipementsCulte;

    // Polygone simple : 3 à 500 sommets distincts consécutifs, sans auto-intersection, d'au moins 0,25 m².
    private static (List<PointPlan> Contour, Polygon Forme) Contour(IReadOnlyList<SaisiePoint?>? saisie, string champ, ref int points)
    {
        if (saisie is null || saisie.Count is < 3 or > SommetsMax)
        {
            throw Refus(champ);
        }

        points += saisie.Count;
        if (points > PointsMax)
        {
            throw Refus(champ);
        }

        var contour = saisie.Select((p, rang) => Point(p, $"{champ}[{rang}]")).ToList();
        if (contour.Where((p, rang) => p == contour[(rang + 1) % contour.Count]).Any())
        {
            throw Refus(champ); // sommet répété
        }

        var forme = Fabrique.CreatePolygon([.. contour.Select(p => new Coordinate(p.X, p.Y)), new Coordinate(contour[0].X, contour[0].Y)]);
        if (!new IsValidOp(forme).IsValid || forme.Area < AireMin)
        {
            throw Refus(champ);
        }

        return (contour, forme);
    }

    private static PointPlan Point(SaisiePoint? saisie, string champ) =>
        saisie is { X: { } x, Y: { } y } && double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x) <= Borne && Math.Abs(y) <= Borne
            ? new PointPlan(Centimetre(x), Centimetre(y))
            : throw Refus(champ);

    private static bool SurUneArete(Polygon piece, PointPlan de, PointPlan a)
    {
        var sommets = piece.ExteriorRing.Coordinates;
        var debut = new Coordinate(de.X, de.Y);
        var fin = new Coordinate(a.X, a.Y);
        for (var i = 0; i < sommets.Length - 1; i++)
        {
            var arete = new LineSegment(sommets[i], sommets[i + 1]);
            if (arete.Distance(debut) <= ToleranceArete && arete.Distance(fin) <= ToleranceArete)
            {
                return true;
            }
        }

        return false;
    }

    private static SaisieInvalideException Refus(string champ) => new(champ, $"{champ} : valeur refusée par le plan de bâtiment.");
}

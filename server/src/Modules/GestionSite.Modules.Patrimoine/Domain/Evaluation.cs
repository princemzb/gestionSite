using System.Text.Json;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>Échelle commune d'état (DEC-01) : la valeur de chaque état est sa note.</summary>
internal enum EtatComposant
{
    Critique = 1,
    Mauvais = 3,
    Degrade = 5,
    Satisfaisant = 7,
    Bon = 9,
}

/// <summary>Notes et libellés de l'échelle commune (DEC-01, RG-12).</summary>
internal static class Echelle
{
    /// <summary>Note d'un lot : moyenne des états pondérée par les quantités, précision complète.</summary>
    internal static decimal Note(Repartition repartition) =>
        ((9m * repartition.Bon) + (7m * repartition.Satisfaisant) + (5m * repartition.Degrade) + (3m * repartition.Mauvais) + repartition.Critique)
        / repartition.Total;

    /// <summary>Note affichée : une décimale, arrondi au plus proche (0,05 vers le haut).</summary>
    internal static decimal Arrondie(decimal note) => Math.Round(note, 1, MidpointRounding.AwayFromZero);

    /// <summary>Libellé de la note arrondie affichée : [0;2[ Critique … [8;10] Bon, jamais en contradiction avec le chiffre.</summary>
    internal static EtatComposant Libelle(decimal note) => Arrondie(note) switch
    {
        >= 8 => EtatComposant.Bon,
        >= 6 => EtatComposant.Satisfaisant,
        >= 4 => EtatComposant.Degrade,
        >= 2 => EtatComposant.Mauvais,
        _ => EtatComposant.Critique,
    };
}

/// <summary>Point de la grille présenté à l'évaluateur et son constat : vrai conforme, faux défaut, absent non vérifié.</summary>
internal sealed record PointEvalue(string Texte, bool? Conforme);

/// <summary>Mesure de la grille présentée, et la valeur relevée si l'évaluateur l'a saisie.</summary>
internal sealed record MesureEvaluee(string Libelle, string Unite, decimal? Valeur);

/// <summary>Saisie de l'évaluateur, avant contrôle (grille en vigueur et composant connus du service).</summary>
internal sealed record SaisieEvaluation(
    Guid CommandeId, int? GrilleVersion, IReadOnlyList<bool?>? Constats, IReadOnlyList<decimal?>? Mesures, EtatComposant? Etat,
    Repartition? Repartition, string? Remarque, int Photos);

/// <summary>
/// Évaluation d'un composant (US-11, S11-02) : état choisi par l'évaluateur (individuel) ou répartition par état (lot),
/// guidé par la grille du site sans calcul de note à partir des constats (DEC-01). Elle garde la version et le texte des
/// points et mesures présentés : son historique ne dépend d'aucune version ultérieure. Jamais modifiée ensuite.
/// </summary>
internal sealed class Evaluation : IAppartientAOrganisation
{
    internal const int RemarqueMax = 2000;
    internal const decimal MesureMax = 1_000_000m;
    internal const string ContrainteCommande = "ix_evaluations_commande";

    private Evaluation()
    {
    }

    private Evaluation(Guid id, Composant composant, GrilleEvaluee grille, SaisieEvaluation saisie, IReadOnlyList<PointEvalue> points,
        IReadOnlyList<MesureEvaluee> mesures, string? remarque, Guid evaluateurCompteId, DateTimeOffset le)
    {
        Id = id;
        OrganisationId = composant.OrganisationId;
        SiteId = composant.SiteId;
        ComposantId = composant.Id;
        ZoneId = composant.ZoneId;
        CorpsEtat = composant.CorpsEtat;
        CommandeId = saisie.CommandeId;
        GrilleVersion = grille.Version;
        Points = JsonSerializer.Serialize(points);
        Mesures = JsonSerializer.Serialize(mesures);
        Etat = saisie.Etat;
        Bon = saisie.Repartition?.Bon;
        Satisfaisant = saisie.Repartition?.Satisfaisant;
        Degrade = saisie.Repartition?.Degrade;
        Mauvais = saisie.Repartition?.Mauvais;
        Critique = saisie.Repartition?.Critique;
        Remarque = remarque;
        EvaluateurCompteId = evaluateurCompteId;
        EvalueLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid ComposantId { get; private set; }

    /// <summary>Zone du composant au moment de l'évaluation.</summary>
    public Guid ZoneId { get; private set; }

    public CorpsEtat CorpsEtat { get; private set; }

    /// <summary>Identifiant d'envoi choisi par l'appareil : un envoi rejoué ne crée pas une seconde évaluation.</summary>
    public Guid CommandeId { get; private set; }

    public int GrilleVersion { get; private set; }

    public string Points { get; private set; } = "[]";

    public string Mesures { get; private set; } = "[]";

    /// <summary>État d'un composant individuel ; absent pour un lot.</summary>
    public EtatComposant? Etat { get; private set; }

    public int? Bon { get; private set; }

    public int? Satisfaisant { get; private set; }

    public int? Degrade { get; private set; }

    public int? Mauvais { get; private set; }

    public int? Critique { get; private set; }

    public string? Remarque { get; private set; }

    public Guid EvaluateurCompteId { get; private set; }

    public DateTimeOffset EvalueLe { get; private set; }

    /// <summary>Répartition d'un lot ; absente pour un composant individuel.</summary>
    internal Repartition? Repartition => Bon is { } bon
        ? new Repartition(bon, Satisfaisant ?? 0, Degrade ?? 0, Mauvais ?? 0, Critique ?? 0)
        : null;

    internal decimal Note => Repartition is { } repartition ? Echelle.Note(repartition) : (int)Etat!.Value;

    /// <summary>« Critique » constaté (au moins une unité d'un lot) : l'application propose d'ouvrir un dossier, sans le créer.</summary>
    internal bool ProposerDossier => Etat == EtatComposant.Critique || Critique > 0;

    internal IReadOnlyList<PointEvalue> PointsLus => JsonSerializer.Deserialize<List<PointEvalue>>(Points) ?? [];

    internal IReadOnlyList<MesureEvaluee> MesuresLues => JsonSerializer.Deserialize<List<MesureEvaluee>>(Mesures) ?? [];

    /// <summary>
    /// Évaluation contrôlée : composant actif, grille en vigueur (version lue), état pour un composant individuel ou
    /// répartition égale à la quantité pour un lot, constats et mesures alignés sur la grille, photo pour « Mauvais » et
    /// « Critique » (décision du 29/10). Tout est vérifié avant l'écriture d'une photo.
    /// </summary>
    internal static Evaluation Creer(Guid id, Composant composant, GrilleEvaluee grille, SaisieEvaluation saisie, Guid evaluateurCompteId, DateTimeOffset le)
    {
        if (composant.Retire)
        {
            throw new ComposantRetireException();
        }

        if (saisie.GrilleVersion is not { } version)
        {
            throw new SaisieInvalideException("grilleVersion", "Version de la grille utilisée obligatoire.");
        }

        if (version != grille.Version)
        {
            throw new GrilleModifieeException();
        }

        if (composant.Lot is { } lot)
        {
            if (saisie.Etat is not null || saisie.Repartition is not { } repartition)
            {
                throw new SaisieInvalideException("repartition", "Lot : indiquer combien d'unités sont dans chaque état.");
            }

            if (!repartition.Positive || repartition.Total != lot.Quantite)
            {
                throw new SaisieInvalideException("repartition", $"Répartition : {repartition.Total} pour une quantité de {lot.Quantite}.");
            }
        }
        else if (saisie.Repartition is not null || saisie.Etat is not { } etat || !Enum.IsDefined(etat))
        {
            throw new SaisieInvalideException("etat", "État constaté obligatoire : Bon, Satisfaisant, Degrade, Mauvais ou Critique.");
        }

        var points = Aligner(saisie.Constats, grille.Criteres, "constats", (texte, constat) => new PointEvalue(texte, constat));
        var mesures = Aligner(saisie.Mesures, grille.Mesures, "mesures", (mesure, valeur) => new MesureEvaluee(mesure.Libelle, mesure.Unite, Valeur(valeur)));
        var remarque = string.IsNullOrWhiteSpace(saisie.Remarque)
            ? null
            : TexteAffichable.Nettoyer(saisie.Remarque, RemarqueMax, multiligne: true)
                ?? throw new SaisieInvalideException("remarque", $"Remarque : texte visible de {RemarqueMax} caractères au plus.");
        var evaluation = new Evaluation(id, composant, grille, saisie, points, mesures, remarque, evaluateurCompteId, le);
        if (evaluation.PhotoExigee && saisie.Photos == 0)
        {
            throw new SaisieInvalideException("photos", "Une photo au moins pour un état « Mauvais » ou « Critique ».");
        }

        return evaluation;
    }

    /// <summary>Même envoi rejoué : même évaluateur, même composant, même état ou même répartition.</summary>
    internal bool MemeEnvoi(Guid evaluateurCompteId, Guid composantId, SaisieEvaluation saisie) =>
        evaluateurCompteId == EvaluateurCompteId && composantId == ComposantId && saisie.Etat == Etat && saisie.Repartition == Repartition;

    private bool PhotoExigee => Etat is EtatComposant.Mauvais or EtatComposant.Critique || Mauvais > 0 || Critique > 0;

    // Une réponse par point de la grille, dans l'ordre ; les réponses manquantes en fin de liste valent « non vérifié ».
    private static List<TResultat> Aligner<TGrille, TReponse, TResultat>(
        IReadOnlyList<TReponse>? reponses, IReadOnlyList<TGrille> grille, string champ, Func<TGrille, TReponse?, TResultat> associer)
    {
        reponses ??= [];
        if (reponses.Count > grille.Count)
        {
            throw new SaisieInvalideException(champ, $"{grille.Count} réponses au plus, une par élément de la grille.");
        }

        return [.. grille.Select((element, i) => associer(element, i < reponses.Count ? reponses[i] : default))];
    }

    private static decimal? Valeur(decimal? valeur) => valeur switch
    {
        null => null,
        > MesureMax or < -MesureMax => throw new SaisieInvalideException("mesures", $"Mesure entre -{MesureMax} et {MesureMax}."),
        { } v when Math.Round(v, 3) != v => throw new SaisieInvalideException("mesures", "Mesure : trois décimales au plus."),
        { } v => v / 1.000000000000000000000000000m, // zéros de fin retirés : 48,500 enregistré 48,5
    };
}

/// <summary>Grille présentée à l'évaluateur : version et contenu en vigueur sur le site pour le corps d'état du composant.</summary>
internal sealed record GrilleEvaluee(int Version, IReadOnlyList<string> Criteres, IReadOnlyList<MesureGrille> Mesures);

/// <summary>Photo prise à l'évaluation (JPEG réencodé), sous une clé propre à cet envoi.</summary>
internal sealed class PhotoEvaluation : IAppartientAOrganisation
{
    internal const int CleMax = 200;

    private PhotoEvaluation()
    {
    }

    internal PhotoEvaluation(Guid id, Evaluation evaluation, int rang, int taille, DateTimeOffset le)
    {
        Id = id;
        OrganisationId = evaluation.OrganisationId;
        EvaluationId = evaluation.Id;
        Rang = rang;
        Cle = $"organisations/{evaluation.OrganisationId:D}/evaluations/{evaluation.Id:D}/{id:D}.jpg";
        Taille = taille;
        CreeLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid EvaluationId { get; private set; }

    /// <summary>1 à 3, dans l'ordre d'envoi.</summary>
    public int Rang { get; private set; }

    public string Cle { get; private set; } = string.Empty;

    public int Taille { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }
}

/// <summary>La grille a changé depuis son affichage (409 « grille_modifiee ») : relire la grille puis refaire l'évaluation.</summary>
internal sealed class GrilleModifieeException() : Exception("La grille de ce corps d'état vient d'être modifiée : rechargez-la puis recommencez.");

/// <summary>Identifiant d'envoi déjà utilisé pour une autre évaluation (409 « commande_reutilisee »).</summary>
internal sealed class CommandeReutiliseeException() : Exception("Cet identifiant d'envoi a déjà servi pour une autre évaluation.");

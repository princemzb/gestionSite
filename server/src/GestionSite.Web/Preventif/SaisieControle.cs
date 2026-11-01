using GestionSite.Web.Inventaire;
using GestionSite.Web.Session;

namespace GestionSite.Web.Preventif;

/// <summary>
/// Saisie d'un contrôle préventif sur téléphone (US-12, S12-07, maquette A : même parcours pas à pas que l'évaluation).
/// Inspection : points de la grille (sans défaut, défaut, défaut avec danger, non vérifié) et mesures, puis l'état du
/// composant (évaluation US-11 de la même saisie) ; entretien : relevés ; vérification extérieure : prestataire, numéro
/// d'agrément, observations (zone, danger) et relevés ; enfin date réelle, résultat déclaré, photos, compte rendu et,
/// pour une vérification, le rapport PDF. Propriétés publiques : lues du formulaire.
/// </summary>
internal sealed class SaisieControle : SaisieGrille
{
    internal const int RelevesMax = 8;

    internal const int ObservationsMax = 50;

    public List<ReleveSaisi>? Releves { get; set; } = [];

    public List<ObservationSaisie>? Observations { get; set; } = [];

    // Échéance affichée à l'ouverture : l'envoi la vise, jamais l'échéance suivante (saut ou double envoi entre-temps).
    public DateOnly? Echeance { get; set; }

    public string? Prestataire { get; set; }

    public string? NumeroAgrement { get; set; }

    public DateOnly? RealiseLe { get; set; }

    public string? Resultat { get; set; }

    public string? CompteRendu { get; set; }

    /// <summary>Étapes du parcours selon la nature du plan ; l'état n'est demandé que pour l'inspection d'un composant.</summary>
    internal static IReadOnlyList<(string Cle, string Titre)> Etapes(string nature, bool composant) => nature switch
    {
        "InspectionInterne" when composant => [("points", "Points à observer"), ("etat", "État"), ("resultat", "Résultat et photos")],
        "InspectionInterne" => [("points", "Points à observer"), ("resultat", "Résultat et photos")],
        "VerificationExterieure" => [("observations", "Intervenant et observations"), ("resultat", "Rapport et résultat")],
        _ => [("releves", "Relevés"), ("resultat", "Résultat et compte rendu")],
    };

    /// <summary>Point en défaut (avec ou sans danger) ou observation d'un rapport : le résultat « sans observation » est exclu.</summary>
    internal bool Ecart => Enumerable.Range(0, Constats?.Count ?? 0).Any(i => Constat(i) is "non" or "danger") || ObservationsRemplies.Any();

    internal IEnumerable<ReleveSaisi> RelevesRemplis => (Releves ?? []).Where(r => !string.IsNullOrWhiteSpace(r.Libelle) || !string.IsNullOrWhiteSpace(r.Valeur) || !string.IsNullOrWhiteSpace(r.Unite));

    internal IEnumerable<ObservationSaisie> ObservationsRemplies => (Observations ?? []).Where(o => !string.IsNullOrWhiteSpace(o.Libelle));

    /// <summary>Résultats proposés : jamais « sans observation » après un défaut ou une observation.</summary>
    internal IEnumerable<(string Code, string Libelle)> Resultats() => LibellesPreventif.Resultats.Where(r => !Ecart || r.Code != "Conforme");

    /// <summary>
    /// Rapport PDF exigé (décision du 30/10) : vérification de fondement réglementaire faite par un prestataire, saisi ici
    /// ou désigné à l'affectation. L'API reste juge.
    /// </summary>
    internal bool RapportExige(PlanApi plan) =>
        plan is { Nature: "VerificationExterieure", Base: "Reglementaire" }
        && (!string.IsNullOrWhiteSpace(Prestataire) || plan.Prochaine?.Intervenant?.Prestataire is not null);

    /// <summary>Lignes affichées pour une liste à compléter : au moins une, vide.</summary>
    internal static List<T> AuMoinsUne<T>(List<T>? lignes)
        where T : new() => lignes is { Count: > 0 } ? lignes : [new T()];

    /// <summary>Ligne vide ajoutée (relevé ou observation), dans la limite de l'API.</summary>
    internal void Ajouter(string liste)
    {
        if (liste == "releve")
        {
            Releves = AuMoinsUne(Releves);
            if (Releves.Count < RelevesMax)
            {
                Releves.Add(new ReleveSaisi());
            }
        }
        else
        {
            Observations = AuMoinsUne(Observations);
            if (Observations.Count < ObservationsMax)
            {
                Observations.Add(new ObservationSaisie());
            }
        }
    }

    /// <summary>Contrôle de l'étape avant de passer à la suivante : message, ou null si l'étape est complète.</summary>
    internal string? ErreurEtape(string etape, GrilleApi? grille, LotApi? lot) => etape switch
    {
        "points" when MesuresLues(grille?.Mesures.Count ?? 0) is null => "Mesure : un nombre (virgule ou point), trois décimales au plus.",
        "etat" when lot is not null => ErreurRepartition(lot.Quantite),
        "etat" when Etat is null => "Choisissez l'état constaté.",
        "releves" or "observations" when RelevesRemplis.Any(r => string.IsNullOrWhiteSpace(r.Libelle) || string.IsNullOrWhiteSpace(r.Unite)
            || !Decimal(r.Valeur, out var v) || v is null) =>
            "Relevé : un libellé, une valeur (nombre, virgule ou point, trois décimales au plus) et une unité.",
        // Une observation sans libellé n'est pas envoyée : un danger signalé sans texte serait perdu.
        "observations" when (Observations ?? []).Any(o => o.Danger && string.IsNullOrWhiteSpace(o.Libelle)) =>
            "Observation : décrivez l'écart signalé comme dangereux.",
        _ => null,
    };

    /// <summary>Corps de POST plans/{id}/perform (CONTRATS : exécution des occurrences, vérification extérieure).</summary>
    internal object Demande(PlanApi plan, GrilleApi? grille, LotApi? lot)
    {
        var inspection = plan.Nature == "InspectionInterne";
        var composant = inspection && plan.Cible.ComposantId is not null;
        return new
        {
            commandeId = CommandeId,
            echeance = Echeance ?? plan.Prochaine?.Echeance,
            realiseLe = RealiseLe,
            resultat = Resultat,
            compteRendu = CompteRendu,
            prestataire = inspection ? null : Prestataire,
            numeroAgrement = inspection ? null : NumeroAgrement,
            grilleVersion = inspection ? GrilleVersion : null,
            points = inspection ? Enumerable.Range(0, grille?.Criteres.Count ?? 0).Select(Point).ToList() : null,
            mesures = inspection ? MesuresLues(grille?.Mesures.Count ?? 0) : null,
            etat = composant && lot is null ? Etat : null,
            repartition = composant && lot is not null ? Repartition() : null,
            releves = inspection ? null : RelevesRemplis.Select(r => new { libelle = r.Libelle, unite = r.Unite, valeur = Valeur(r.Valeur) }).ToList(),
            observations = plan.Nature == "VerificationExterieure"
                ? ObservationsRemplies.Select(o => new { libelle = o.Libelle, zoneId = o.ZoneId, danger = o.Danger }).ToList()
                : null,
        };
    }

    // « oui » sans défaut, « non » défaut, « danger » défaut avec danger, vide non vérifié.
    private object Point(int rang) => Constat(rang) switch
    {
        "oui" => new { conforme = (bool?)true, danger = false },
        "non" => new { conforme = (bool?)false, danger = false },
        "danger" => new { conforme = (bool?)false, danger = true },
        _ => new { conforme = (bool?)null, danger = false },
    };

    private static decimal? Valeur(string? texte) => Decimal(texte, out var valeur) ? valeur : null;
}

/// <summary>Relevé libre d'un entretien ou d'une vérification (libellé, unité, valeur saisie).</summary>
internal sealed class ReleveSaisi
{
    public string? Libelle { get; set; }

    public string? Unite { get; set; }

    public string? Valeur { get; set; }
}

/// <summary>Observation d'un rapport de vérification extérieure : chacune est un écart à lever (dossier).</summary>
internal sealed class ObservationSaisie
{
    public string? Libelle { get; set; }

    public Guid? ZoneId { get; set; }

    public bool Danger { get; set; }
}

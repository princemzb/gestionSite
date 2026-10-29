using System.Text;
using System.Text.Json;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>Mesure relevée pendant une évaluation, avec son unité (PRD 4.1 : humidité, dimensions, compteurs…).</summary>
internal sealed record MesureGrille(string Libelle, string Unite);

/// <summary>Contenu d'une grille : points à observer (constats oui/non) et mesures facultatives.</summary>
internal sealed record ContenuGrille(IReadOnlyList<string> Criteres, IReadOnlyList<MesureGrille> Mesures);

/// <summary>
/// Grilles fournies par l'application, une par corps d'état (décision du 29/10), rédigées du point de vue d'un
/// technicien du bâtiment : ce qu'on regarde sur place pour choisir un état de l'échelle commune. Les critères guident
/// le choix de l'état sans calcul de note (DEC-01) ; R/D les adaptent pour leur organisation (nouvelle version).
/// </summary>
internal static class GrillesParDefaut
{
    private static readonly Dictionary<CorpsEtat, ContenuGrille> Grilles = new()
    {
        [CorpsEtat.StructureGrosOeuvre] = new(
            ["Aucune fissure traversante ni en escalier", "Pas de déformation visible (flèche, bombement, affaissement)", "Pas d'éclatement de béton ni d'armature apparente",
             "Charpente sans attaque d'insectes ni pourriture visible", "Pas d'humidité ni de salpêtre en pied de mur"],
            [new("Ouverture de la plus grande fissure", "mm"), new("Humidité du mur", "%")]),
        [CorpsEtat.CouvertureEtancheite] = new(
            ["Aucune tuile ou ardoise cassée, glissée ou manquante", "Solins et faîtage en place", "Gouttières et descentes fixées, non obstruées",
             "Aucune trace d'infiltration en sous-face", "Étanchéité de toiture-terrasse sans cloque ni fissure"],
            [new("Surface concernée par un défaut", "m²")]),
        [CorpsEtat.MenuiseriesExterieures] = new(
            ["Ouvrants manœuvrables, fermeture et verrouillage efficaces", "Vitrage et vitraux sans casse ni plomb défait", "Joints et mastics en bon état, pas d'entrée d'air ou d'eau",
             "Bois sans pourriture, métal sans corrosion perforante"],
            []),
        [CorpsEtat.MenuiseriesInterieures] = new(
            ["Portes manœuvrables, ferme-portes efficaces", "Issues de secours libres et ouvrables sans clé, barre anti-panique fonctionnelle",
             "Serrures et quincaillerie complètes", "Cloisons sans fissure ni trou"],
            []),
        [CorpsEtat.RevetementsFinitions] = new(
            ["Peinture sans écaillage ni cloquage", "Sols sans fissure, décollement ni trou (risque de chute)", "Plafonds sans tache d'humidité ni décollement",
             "Faïences et plinthes fixées"],
            [new("Surface dégradée", "m²")]),
        [CorpsEtat.CourantsForts] = new(
            ["Capot et portes fermés, aucune partie sous tension accessible", "Repérage des circuits lisible", "Pas de trace d'échauffement (noircissement, odeur)",
             "Bouton test du différentiel 30 mA : déclenche", "Prises et interrupteurs fixés, sans casse"],
            [new("Température au point chaud", "°C")]),
        [CorpsEtat.EclairageSecurite] = new(
            ["Voyant de charge allumé", "Allumage au test de coupure", "Autonomie d'au moins 1 heure à l'essai", "Pictogramme lisible et bien orienté vers l'issue"],
            [new("Autonomie mesurée", "min")]),
        [CorpsEtat.SecuriteIncendie] = new(
            ["Extincteur accessible, signalé, fixé, poignée à 1,20 m du sol au plus", "Plomb et goupille présents, manomètre dans le vert",
             "Vérification annuelle à jour (étiquette)", "Tableau d'alarme sans défaut affiché, déclencheurs manuels intacts", "Plans d'évacuation affichés et à jour"],
            [new("Pression au manomètre", "bar")]),
        [CorpsEtat.CourantsFaibles] = new(
            ["Fonctionne à l'essai (son, image, réseau)", "Câbles fixés et protégés, sans gaine abîmée", "Équipements ventilés, sans surchauffe"],
            []),
        [CorpsEtat.ChauffageVentilation] = new(
            ["Démarre et assure la température demandée", "Pas de fuite (eau, fioul, gaz) ni de corrosion sur les raccords", "Bouches et filtres propres, ventilation efficace",
             "Entretien annuel de la chaudière à jour (attestation)"],
            [new("Pression du circuit de chauffage", "bar"), new("Température de départ", "°C")]),
        [CorpsEtat.Plomberie] = new(
            ["Pas de fuite visible ni de trace au sol", "Robinetterie et chasses d'eau fonctionnelles", "Évacuations sans engorgement ni odeur",
             "Eau chaude sanitaire disponible"],
            [new("Température d'eau chaude au point de puisage", "°C")]),
        [CorpsEtat.Gaz] = new(
            ["Aucune odeur de gaz", "Robinet de coupure accessible et manœuvrable", "Flexibles dans leur date de validité", "Ventilation haute et basse du local non obstruée"],
            []),
        [CorpsEtat.Ascenseurs] = new(
            ["Mise à niveau correcte de la cabine à l'arrêt", "Alarme et téléphone de cabine fonctionnels", "Portes palières verrouillées, sans choc",
             "Contrat d'entretien et contrôle technique à jour"],
            []),
        [CorpsEtat.Paratonnerre] = new(
            ["Pointe et conducteur de descente en place, fixés", "Raccordement à la terre visible et non sectionné", "Vérification périodique à jour"],
            [new("Résistance de la prise de terre", "Ω")]),
        [CorpsEtat.Campanaire] = new(
            ["Cloche et battant sans fissure visible", "Jougs et fixations serrés, sans jeu anormal", "Moteurs et commande de volée fonctionnels",
             "Beffroi sans pourriture ni corrosion"],
            []),
        [CorpsEtat.Accessibilite] = new(
            ["Rampe d'accès praticable, pente et main courante conformes", "Place de stationnement réservée signalée", "Sanitaire adapté libre d'accès, barre d'appui fixée",
             "Bandes d'éveil et contremarches contrastées en place"],
            []),
        [CorpsEtat.Mobilier] = new(
            ["Assise et dossier sans fissure ni écharde", "Pieds stables, patins présents", "Empilage ou rangement sans déformation"],
            []),
        [CorpsEtat.EquipementsCulte] = new(
            ["Stable et fixé, sans élément détaché", "Matériaux sans fissure ni attaque (bois, pierre, métal)", "Instrument accordé et fonctionnel (orgue, sonorisation liée)"],
            []),
        [CorpsEtat.ExterieursVoirie] = new(
            ["Sol sans nid-de-poule ni dalle soulevée (risque de chute)", "Clôtures et portails fixés, fermeture efficace", "Éclairage extérieur fonctionnel",
             "Évacuation des eaux pluviales sans stagnation"],
            [new("Surface dégradée", "m²")]),
        [CorpsEtat.Autre] = new(
            ["Fonctionne normalement", "Pas de risque pour les personnes", "Pas de dégradation visible"],
            []),
    };

    internal static ContenuGrille Pour(CorpsEtat corpsEtat) => Grilles[corpsEtat];

    internal static IReadOnlyCollection<CorpsEtat> CorpsEtats => Grilles.Keys;
}

/// <summary>
/// Version adaptée d'une grille pour un site (R/D du site, PERMISSIONS : chaque modification crée une version). La
/// version 1 est la grille par défaut de l'application ; les suivantes sont enregistrées ici, jamais modifiées.
/// </summary>
internal sealed class VersionGrille : IAppartientAOrganisation
{
    internal const int CriteresMax = 15;
    internal const int CritereMax = 160;
    internal const int MesuresMax = 8;
    internal const int MesureMax = 80;
    internal const int UniteMax = 16;
    internal const string ContrainteVersion = "ix_grilles_version";

    private VersionGrille()
    {
    }

    internal VersionGrille(Guid id, Guid organisationId, Guid siteId, CorpsEtat corpsEtat, int version, ContenuGrille contenu, Guid? auteurCompteId, DateTimeOffset creeLe)
    {
        Id = id;
        OrganisationId = organisationId;
        SiteId = siteId;
        CorpsEtat = corpsEtat;
        Version = version;
        Criteres = JsonSerializer.Serialize(contenu.Criteres);
        Mesures = JsonSerializer.Serialize(contenu.Mesures);
        AuteurCompteId = auteurCompteId;
        CreeLe = creeLe;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public CorpsEtat CorpsEtat { get; private set; }

    public int Version { get; private set; }

    public string Criteres { get; private set; } = "[]";

    public string Mesures { get; private set; } = "[]";

    public Guid? AuteurCompteId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    internal ContenuGrille Contenu => new(
        JsonSerializer.Deserialize<List<string>>(Criteres) ?? [],
        JsonSerializer.Deserialize<List<MesureGrille>>(Mesures) ?? []);

    /// <summary>
    /// Contenu saisi par R/D : 1 à 15 points distincts, 0 à 8 mesures distinctes avec unité (casse ignorée). Une ligne
    /// sans caractère visible est ignorée ; une unité sans libellé est refusée.
    /// </summary>
    internal static ContenuGrille Valider(IReadOnlyList<string?>? criteres, IReadOnlyList<(string? Libelle, string? Unite)>? mesures)
    {
        var lus = (criteres ?? []).Where(Visible)
            .Select(c => TexteAffichable.Nettoyer(c, CritereMax)
                ?? throw new SaisieInvalideException("criteres", $"Chaque point à observer : une ligne visible de {CritereMax} caractères au plus."))
            .ToList();
        if (lus.Count is 0 or > CriteresMax || !Distincts(lus))
        {
            throw new SaisieInvalideException("criteres", $"De 1 à {CriteresMax} points à observer, tous différents.");
        }

        var releves = (mesures ?? []).Where(m => Visible(m.Libelle) || Visible(m.Unite))
            .Select(m => new MesureGrille(
                TexteAffichable.Nettoyer(m.Libelle, MesureMax)
                    ?? throw new SaisieInvalideException("mesures", $"Libellé de mesure obligatoire : une ligne visible de {MesureMax} caractères au plus."),
                TexteAffichable.Nettoyer(m.Unite, UniteMax)
                    ?? throw new SaisieInvalideException("mesures", $"Unité de mesure obligatoire, {UniteMax} caractères au plus.")))
            .ToList();
        return releves.Count > MesuresMax || !Distincts([.. releves.Select(m => m.Libelle)])
            ? throw new SaisieInvalideException("mesures", $"{MesuresMax} mesures au plus, toutes différentes.")
            : new ContenuGrille(lus, releves);
    }

    // Ligne laissée vide, y compris d'espaces ou de caractères invisibles (espace sans chasse collée par exemple).
    private static bool Visible(string? texte) =>
        texte is not null && texte.EnumerateRunes().Any(r => Rune.IsLetter(r) || Rune.IsNumber(r) || Rune.IsPunctuation(r) || Rune.IsSymbol(r));

    private static bool Distincts(List<string> textes) => textes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == textes.Count;
}

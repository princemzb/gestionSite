using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>Catégorie d'un composant : corps d'état du bâtiment (REGLES « Inventaire », liste fermée).</summary>
internal enum CorpsEtat
{
    StructureGrosOeuvre = 1,
    CouvertureEtancheite = 2,
    MenuiseriesExterieures = 3,
    MenuiseriesInterieures = 4,
    RevetementsFinitions = 5,
    CourantsForts = 6,
    EclairageSecurite = 7,
    SecuriteIncendie = 8,
    CourantsFaibles = 9,
    ChauffageVentilation = 10,
    Plomberie = 11,
    Gaz = 12,
    Ascenseurs = 13,
    Paratonnerre = 14,
    Campanaire = 15,
    Accessibilite = 16,
    Mobilier = 17,
    EquipementsCulte = 18,
    ExterieursVoirie = 19,
    Autre = 20,
}

internal static class CorpsEtats
{
    // Vérifications périodiques habituelles d'un ERP (installations électriques, éclairage de sécurité, moyens de
    // secours, chaudières, gaz, ascenseurs, paratonnerre, cloches) : proposition seulement, modifiable composant par composant.
    // Travaux à risque ou à contrôler : la clôture d'une intervention exige une photo après intervention (US-09, 29/10).
    internal static bool PhotoApresIntervention(CorpsEtat corpsEtat) => corpsEtat is CorpsEtat.CourantsForts or CorpsEtat.EclairageSecurite
        or CorpsEtat.SecuriteIncendie or CorpsEtat.Gaz or CorpsEtat.CouvertureEtancheite or CorpsEtat.StructureGrosOeuvre;

    internal static bool VerificationProposee(CorpsEtat corpsEtat) => corpsEtat is CorpsEtat.CourantsForts or CorpsEtat.EclairageSecurite
        or CorpsEtat.SecuriteIncendie or CorpsEtat.ChauffageVentilation or CorpsEtat.Gaz or CorpsEtat.Ascenseurs
        or CorpsEtat.Paratonnerre or CorpsEtat.Campanaire;
}

internal enum UniteLot
{
    Unite = 1,
    MetreCarre = 2,
    MetreLineaire = 3,
    MetreCube = 4,
    Ensemble = 5,
}

/// <summary>Quantités d'un lot par état de l'échelle commune ; leur somme égale la quantité du lot (RG-10).</summary>
internal sealed record Repartition(int Bon, int Satisfaisant, int Degrade, int Mauvais, int Critique)
{
    internal long Total => (long)Bon + Satisfaisant + Degrade + Mauvais + Critique;

    internal bool Positive => Bon >= 0 && Satisfaisant >= 0 && Degrade >= 0 && Mauvais >= 0 && Critique >= 0;
}

internal sealed record Lot(UniteLot Unite, int Quantite, Repartition? Repartition);

/// <summary>
/// Saisie d'un composant : un lot absent désigne un composant individuel ; « soumis à vérification » absent prend, à la
/// création seulement, la proposition du corps d'état.
/// </summary>
internal sealed record FicheComposant(
    string? Nom, CorpsEtat CorpsEtat, string? Emplacement, string? Marque, string? Modele, string? NumeroSerie,
    int? AnneeMiseEnService, int? MoisMiseEnService, string? Remarques, bool? SoumisAVerification, Lot? Lot);

/// <summary>
/// Élément suivi d'une zone (US-07, S07-02), individuel ou en lot. Retiré, il sort du calcul du score et reste
/// consultable (DEC-01) : il n'est jamais supprimé ni modifié.
/// </summary>
internal sealed class Composant : IAppartientAOrganisation
{
    internal const int QuantiteMax = 100_000;
    internal const int AnneeMin = 1000; // cloches et charpentes anciennes
    internal const int TexteCourtMax = 120;
    internal const int EmplacementMax = 200;
    internal const int RemarquesMax = 2000;
    internal const int MotifMax = 500;

    private Composant()
    {
    }

    internal Composant(Guid id, Zone zone, FicheComposant fiche, DateTimeOffset maintenant)
    {
        Id = id;
        OrganisationId = zone.OrganisationId;
        SiteId = zone.SiteId;
        CreeLe = maintenant;
        Appliquer(zone, fiche with { SoumisAVerification = fiche.SoumisAVerification ?? CorpsEtats.VerificationProposee(fiche.CorpsEtat) }, maintenant);
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid ZoneId { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public CorpsEtat CorpsEtat { get; private set; }

    public string? Emplacement { get; private set; }

    public string? Marque { get; private set; }

    public string? Modele { get; private set; }

    public string? NumeroSerie { get; private set; }

    public int? AnneeMiseEnService { get; private set; }

    public int? MoisMiseEnService { get; private set; }

    public string? Remarques { get; private set; }

    public bool SoumisAVerification { get; private set; }

    public UniteLot? Unite { get; private set; }

    public int? Quantite { get; private set; }

    public int? QuantiteBon { get; private set; }

    public int? QuantiteSatisfaisant { get; private set; }

    public int? QuantiteDegrade { get; private set; }

    public int? QuantiteMauvais { get; private set; }

    public int? QuantiteCritique { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset? RetireLe { get; private set; }

    public string? MotifRetrait { get; private set; }

    internal bool Retire => RetireLe is not null;

    internal Lot? Lot => Unite is { } unite && Quantite is { } quantite
        ? new Lot(unite, quantite, QuantiteBon is { } bon
            ? new Repartition(bon, QuantiteSatisfaisant ?? 0, QuantiteDegrade ?? 0, QuantiteMauvais ?? 0, QuantiteCritique ?? 0)
            : null)
        : null;

    internal void Modifier(Zone zone, FicheComposant fiche, DateTimeOffset maintenant)
    {
        ExigerActif();
        Appliquer(zone, fiche, maintenant);
    }

    internal void Retirer(string? motif, DateTimeOffset maintenant)
    {
        ExigerActif();
        MotifRetrait = TexteAffichable.Nettoyer(motif, MotifMax)
            ?? throw new SaisieInvalideException("motif", $"motif : une ligne visible de {MotifMax} caractères au plus.");
        RetireLe = maintenant;
    }

    /// <summary>Répartition d'un lot reprise de sa dernière évaluation (décision du 29/10), déjà contrôlée par l'évaluation.</summary>
    internal void Repartir(Repartition repartition)
    {
        ExigerActif();
        if (Quantite is not { } quantite || !repartition.Positive || repartition.Total != quantite)
        {
            throw new SaisieInvalideException("repartition", "Répartition d'un lot égale à sa quantité.");
        }

        QuantiteBon = repartition.Bon;
        QuantiteSatisfaisant = repartition.Satisfaisant;
        QuantiteDegrade = repartition.Degrade;
        QuantiteMauvais = repartition.Mauvais;
        QuantiteCritique = repartition.Critique;
    }

    private void ExigerActif()
    {
        if (Retire)
        {
            throw new ComposantRetireException();
        }
    }

    // Tout est validé avant la première affectation : une saisie refusée laisse le composant intact.
    private void Appliquer(Zone zone, FicheComposant fiche, DateTimeOffset maintenant)
    {
        if (zone.SiteId != SiteId || zone.OrganisationId != OrganisationId)
        {
            throw new SaisieInvalideException("zoneId", "Zone d'un autre site.");
        }

        if (!Enum.IsDefined(fiche.CorpsEtat))
        {
            throw new SaisieInvalideException("corpsEtat", "Corps d'état inconnu.");
        }

        var nom = Domain.Nom.Exiger(fiche.Nom);
        var emplacement = Facultatif(fiche.Emplacement, "emplacement", EmplacementMax);
        var marque = Facultatif(fiche.Marque, "marque", TexteCourtMax);
        var modele = Facultatif(fiche.Modele, "modele", TexteCourtMax);
        var numeroSerie = Facultatif(fiche.NumeroSerie, "numeroSerie", TexteCourtMax);
        var remarques = Facultatif(fiche.Remarques, "remarques", RemarquesMax, multiligne: true);
        var (annee, mois) = MiseEnService(fiche.AnneeMiseEnService, fiche.MoisMiseEnService, maintenant);
        var lot = Valider(fiche.Lot);
        var verification = fiche.SoumisAVerification
            ?? throw new SaisieInvalideException("soumisAVerification", "Indiquer si le composant est soumis à vérification.");
        ZoneId = zone.Id;
        Nom = nom;
        CorpsEtat = fiche.CorpsEtat;
        Emplacement = emplacement;
        Marque = marque;
        Modele = modele;
        NumeroSerie = numeroSerie;
        AnneeMiseEnService = annee;
        MoisMiseEnService = mois;
        Remarques = remarques;
        SoumisAVerification = verification;
        Unite = lot?.Unite;
        Quantite = lot?.Quantite;
        QuantiteBon = lot?.Repartition?.Bon;
        QuantiteSatisfaisant = lot?.Repartition?.Satisfaisant;
        QuantiteDegrade = lot?.Repartition?.Degrade;
        QuantiteMauvais = lot?.Repartition?.Mauvais;
        QuantiteCritique = lot?.Repartition?.Critique;
    }

    // Plaque signalétique : l'année seule suffit souvent ; le mois précise sans être exigé. Jamais dans le futur, jugé
    // au fuseau le plus avancé (UTC+14) : un mois déjà commencé chez l'utilisateur n'est pas refusé.
    private static (int? Annee, int? Mois) MiseEnService(int? annee, int? mois, DateTimeOffset maintenant)
    {
        maintenant = maintenant.ToOffset(TimeSpan.FromHours(14));
        if (annee is null)
        {
            return mois is null ? (null, null) : throw new SaisieInvalideException("moisMiseEnService", "Le mois précise une année de mise en service.");
        }

        if (annee < AnneeMin || annee > maintenant.Year)
        {
            throw new SaisieInvalideException("anneeMiseEnService", $"Année de mise en service entre {AnneeMin} et l'année en cours.");
        }

        if (mois is < 1 or > 12 || (annee == maintenant.Year && mois > maintenant.Month))
        {
            throw new SaisieInvalideException("moisMiseEnService", "Mois de mise en service entre 1 et 12, passé.");
        }

        return (annee, mois);
    }

    private static Lot? Valider(Lot? lot)
    {
        if (lot is null)
        {
            return null;
        }

        if (!Enum.IsDefined(lot.Unite))
        {
            throw new SaisieInvalideException("lot.unite", "Unité inconnue.");
        }

        if (lot.Quantite is < 1 or > QuantiteMax)
        {
            throw new SaisieInvalideException("lot.quantite", $"Quantité entière entre 1 et {QuantiteMax}.");
        }

        if (lot.Repartition is { } repartition && (!repartition.Positive || repartition.Total != lot.Quantite))
        {
            throw new SaisieInvalideException("lot.repartition", $"Répartition : {repartition.Total} pour une quantité de {lot.Quantite}.");
        }

        return lot;
    }

    private static string? Facultatif(string? texte, string champ, int max, bool multiligne = false)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            return null;
        }

        return TexteAffichable.Nettoyer(texte, max, multiligne)
            ?? throw new SaisieInvalideException(champ, $"{champ} : texte visible de {max} caractères au plus.");
    }
}

/// <summary>Un composant retiré ne se modifie plus (409 « composant_retire ») ; son historique reste consultable.</summary>
internal sealed class ComposantRetireException() : Exception("Ce composant est retiré de l'inventaire.");

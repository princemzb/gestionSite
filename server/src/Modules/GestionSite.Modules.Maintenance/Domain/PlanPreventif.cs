using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Qui commande le contrôle (information, sans effet sur les droits ni sur l'argent).</summary>
internal enum DonneurOrdre
{
    Paroisse = 1,
    Commune = 2,
    Diocese = 3,
    Autre = 4,
}

/// <summary>Motif d'une proposition écartée, qui reste visible avec lui (décision du 30/10) ; « Autre » exige un commentaire.</summary>
internal enum MotifEcart
{
    /// <summary>Installation ou bâtiment non concerné.</summary>
    NonConcerne = 1,

    /// <summary>Suivi ailleurs (contrat, registre papier, autre organisation).</summary>
    SuiviAilleurs = 2,
    Autre = 3,
}

/// <summary>Cible d'un plan : bâtiment toujours connu, zone (celle du composant, la zone choisie, ou les parties communes du bâtiment).</summary>
internal sealed record CibleDuPlan(CiblePlan Type, Guid BatimentId, Guid ZoneId, Guid? ComposantId, string? CorpsEtat);

/// <summary>Intervenant par défaut d'un plan : aucun, un technicien du site, ou un prestataire nommé et son référent interne (US-09).</summary>
internal sealed record IntervenantPlan(Guid? CompteId, string? Prestataire, Guid? ReferentCompteId)
{
    internal static readonly IntervenantPlan Aucun = new(null, null, null);

    internal static IntervenantPlan Valider(Guid? compteId, string? prestataire, Guid? referentCompteId)
    {
        var nom = string.IsNullOrWhiteSpace(prestataire) ? null : TexteAffichable.Nettoyer(prestataire, Intervention.PrestataireMax)
            ?? throw new SaisieInvalideException("prestataire", $"prestataire : une ligne visible de {Intervention.PrestataireMax} caractères au plus.");
        if (compteId is not null && (nom is not null || referentCompteId is not null))
        {
            throw new SaisieInvalideException("intervenantCompteId", "Un technicien interne, ou un prestataire et son référent interne.");
        }

        if (nom is not null != referentCompteId is not null)
        {
            throw new SaisieInvalideException(nom is null ? "prestataire" : "referentCompteId", "Un prestataire est suivi par un référent interne.");
        }

        return new IntervenantPlan(compteId, nom, referentCompteId);
    }
}

/// <summary>
/// Calendrier d'un plan : périodique (tous les N semaines, mois ou ans, glissant ou fixe) ou ponctuel (sans période, à
/// la première échéance). La première échéance reste dans une fenêtre raisonnable autour d'aujourd'hui.
/// </summary>
internal sealed record CalendrierPlan(Periode? Periode, ModeEcheance Mode, DateOnly PremiereEcheance)
{
    internal static CalendrierPlan Valider(Periode? periode, ModeEcheance? mode, DateOnly? premiere, DateOnly aujourdhui)
    {
        var choisi = mode ?? ModeEcheance.Glissant;
        if (!Enum.IsDefined(choisi))
        {
            throw new SaisieInvalideException("mode", "Mode d'échéance : Glissant ou Fixe.");
        }

        if (periode is null && choisi == ModeEcheance.Fixe)
        {
            throw new SaisieInvalideException("mode", "Une échéance fixe revient à chaque période : indiquer la période.");
        }

        var date = premiere ?? throw new SaisieInvalideException("premiereEcheance", "Première échéance obligatoire.");
        if (date < aujourdhui.AddYears(-5) || date > aujourdhui.AddYears(10))
        {
            throw new SaisieInvalideException("premiereEcheance", "Première échéance entre 5 ans avant et 10 ans après aujourd'hui.");
        }

        return new CalendrierPlan(periode, periode is null ? ModeEcheance.Glissant : choisi, date);
    }
}

/// <summary>Définition modifiable d'un plan ; la cible et la nature restent celles de la création.</summary>
internal sealed record DefinitionPlan(
    string? Libelle, BasePlan Base, string? Reference, IntervenantPlan Intervenant, DonneurOrdre? DonneurOrdre, CalendrierPlan Calendrier);

/// <summary>
/// Plan préventif d'un site (US-12, S12-02) : cible, nature, fondement et référence « à confirmer », intervenant par
/// défaut, calendrier. Créé par R/D, directement ou en confirmant une proposition du catalogue (clé unique : une
/// proposition confirmée et rejouée ne crée qu'un plan). Ses échéances sont calculées à la lecture, sans tâche planifiée.
/// </summary>
internal sealed class PlanPreventif : IAppartientAOrganisation
{
    internal const int LibelleMax = 160;
    internal const int ReferenceMax = 500;
    internal const int CleMax = 120;
    internal const string ContrainteCommande = "ix_plans_preventifs_commande";
    internal const string ContrainteProposition = "ix_plans_preventifs_proposition";

    private PlanPreventif()
    {
    }

    internal PlanPreventif(
        Guid id, Guid organisationId, Guid siteId, Guid commandeId, string? propositionCle, CibleDuPlan cible, NaturePlan nature,
        TypeIntervenant? typeIntervenant, DefinitionPlan definition, Guid auteurCompteId, DateTimeOffset le)
    {
        if (!Enum.IsDefined(nature))
        {
            throw new SaisieInvalideException("nature", "Nature : InspectionInterne, VerificationExterieure ou Entretien.");
        }

        if (nature == NaturePlan.InspectionInterne && cible.CorpsEtat is null)
        {
            throw new SaisieInvalideException("nature", "Une inspection interne suit la grille d'un corps d'état : composant, ou zone et corps d'état.");
        }

        Id = id;
        OrganisationId = organisationId;
        SiteId = siteId;
        CommandeId = commandeId != Guid.Empty ? commandeId : throw new SaisieInvalideException("commandeId", "Identifiant d'envoi obligatoire.");
        PropositionCle = propositionCle;
        Cible = cible.Type;
        BatimentId = cible.BatimentId;
        ZoneId = cible.ZoneId;
        ComposantId = cible.ComposantId;
        CorpsEtat = cible.CorpsEtat;
        Nature = nature;
        TypeIntervenant = typeIntervenant;
        CreeParCompteId = auteurCompteId;
        CreeLe = le;
        Appliquer(definition);
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>Identifiant d'envoi : une création rejouée ne crée pas un second plan.</summary>
    public Guid CommandeId { get; private set; }

    /// <summary>Clé de la proposition confirmée (« modele:cible:identifiant »), null pour un plan saisi.</summary>
    public string? PropositionCle { get; private set; }

    public CiblePlan Cible { get; private set; }

    public Guid BatimentId { get; private set; }

    public Guid ZoneId { get; private set; }

    public Guid? ComposantId { get; private set; }

    public string? CorpsEtat { get; private set; }

    public NaturePlan Nature { get; private set; }

    public string Libelle { get; private set; } = string.Empty;

    public BasePlan Base { get; private set; }

    public string? Reference { get; private set; }

    /// <summary>Type d'intervenant habituel, repris du modèle du catalogue (information).</summary>
    public TypeIntervenant? TypeIntervenant { get; private set; }

    public Guid? IntervenantCompteId { get; private set; }

    public string? Prestataire { get; private set; }

    public Guid? ReferentCompteId { get; private set; }

    public DonneurOrdre? DonneurOrdre { get; private set; }

    public int? PeriodeValeur { get; private set; }

    public UnitePeriode? PeriodeUnite { get; private set; }

    public ModeEcheance Mode { get; private set; }

    public DateOnly PremiereEcheance { get; private set; }

    public bool Suspendu { get; private set; }

    public string? MotifSuspension { get; private set; }

    public Guid CreeParCompteId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    internal Periode? Periode => PeriodeValeur is { } valeur && PeriodeUnite is { } unite ? new Periode(valeur, unite) : null;

    internal CalendrierPlan Calendrier => new(Periode, Mode, PremiereEcheance);

    internal CibleDuPlan CibleDuPlan => new(Cible, BatimentId, ZoneId, ComposantId, CorpsEtat);

    internal string? Modele => PropositionCle?.Split(':')[0];

    internal void Modifier(DefinitionPlan definition) => Appliquer(definition);

    internal void Suspendre(string? motif)
    {
        if (Suspendu)
        {
            throw new PlanIncompatibleException("Ce plan est déjà suspendu.");
        }

        MotifSuspension = TexteAffichable.Nettoyer(motif, Signalement.CommentaireMax, multiligne: true)
            ?? throw new SaisieInvalideException("motif", $"Motif de suspension : texte visible de {Signalement.CommentaireMax} caractères au plus.");
        Suspendu = true;
    }

    internal void Reprendre()
    {
        if (!Suspendu)
        {
            throw new PlanIncompatibleException("Ce plan n'est pas suspendu.");
        }

        Suspendu = false;
        MotifSuspension = null;
    }

    /// <summary>Même envoi rejoué : même auteur, même proposition ou même cible, même nature et même libellé.</summary>
    internal bool MemeEnvoi(PlanPreventif autre) =>
        (CreeParCompteId, PropositionCle, CibleDuPlan, Nature, Libelle) == (autre.CreeParCompteId, autre.PropositionCle, autre.CibleDuPlan, autre.Nature, autre.Libelle);

    /// <summary>Valeurs relevées pour le journal (ancienne règle tracée à chaque modification).</summary>
    internal Dictionary<string, object?> Releve() => new()
    {
        ["libelle"] = Libelle,
        ["base"] = Base.ToString(),
        ["reference"] = Reference,
        ["intervenantCompteId"] = IntervenantCompteId,
        ["prestataire"] = Prestataire,
        ["referentCompteId"] = ReferentCompteId,
        ["donneurOrdre"] = DonneurOrdre?.ToString(),
        ["periode"] = PeriodeValeur,
        ["unite"] = PeriodeUnite?.ToString(),
        ["mode"] = Mode.ToString(),
        ["premiereEcheance"] = PremiereEcheance.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
    };

    private void Appliquer(DefinitionPlan definition)
    {
        var libelle = TexteAffichable.Nettoyer(definition.Libelle, LibelleMax)
            ?? throw new SaisieInvalideException("libelle", $"libelle : une ligne visible de {LibelleMax} caractères au plus.");
        if (!Enum.IsDefined(definition.Base))
        {
            throw new SaisieInvalideException("base", "Fondement : Reglementaire ou BonnePratique.");
        }

        var reference = string.IsNullOrWhiteSpace(definition.Reference) ? null : TexteAffichable.Nettoyer(definition.Reference, ReferenceMax)
            ?? throw new SaisieInvalideException("reference", $"reference : une ligne visible de {ReferenceMax} caractères au plus.");
        if (definition.DonneurOrdre is { } donneur && !Enum.IsDefined(donneur))
        {
            throw new SaisieInvalideException("donneurOrdre", "Donneur d'ordre : Paroisse, Commune, Diocese ou Autre.");
        }

        Libelle = libelle;
        Base = definition.Base;
        Reference = reference;
        IntervenantCompteId = definition.Intervenant.CompteId;
        Prestataire = definition.Intervenant.Prestataire;
        ReferentCompteId = definition.Intervenant.ReferentCompteId;
        DonneurOrdre = definition.DonneurOrdre;
        PeriodeValeur = definition.Calendrier.Periode?.Valeur;
        PeriodeUnite = definition.Calendrier.Periode?.Unite;
        Mode = definition.Calendrier.Mode;
        PremiereEcheance = definition.Calendrier.PremiereEcheance;
    }
}

/// <summary>Action impossible dans l'état du plan (409 « statut_incompatible »).</summary>
internal sealed class PlanIncompatibleException(string message) : Exception(message);

/// <summary>
/// Proposition du catalogue écartée par R/D (décision du 30/10) : elle reste visible avec son motif ; elle peut encore
/// être confirmée ensuite. Ajout seul.
/// </summary>
internal sealed class PropositionEcartee : IAppartientAOrganisation
{
    private PropositionEcartee()
    {
    }

    internal PropositionEcartee(Guid id, Guid organisationId, Guid siteId, string cle, MotifEcart motif, string? commentaire, Guid auteurCompteId, DateTimeOffset le)
    {
        if (!Enum.IsDefined(motif))
        {
            throw new SaisieInvalideException("motif", "Motif : NonConcerne, SuiviAilleurs ou Autre.");
        }

        Id = id;
        OrganisationId = organisationId;
        SiteId = siteId;
        Cle = cle;
        Motif = motif;
        Commentaire = Domain.Commentaire.Exiger(commentaire, motif == MotifEcart.Autre);
        AuteurCompteId = auteurCompteId;
        EcarteeLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public string Cle { get; private set; } = string.Empty;

    public MotifEcart Motif { get; private set; }

    public string? Commentaire { get; private set; }

    public Guid AuteurCompteId { get; private set; }

    public DateTimeOffset EcarteeLe { get; private set; }
}

internal enum NatureEvenementPlan
{
    Creation = 1,
    Modification = 2,
    Suspension = 3,
    Reprise = 4,
}

/// <summary>Journal d'un plan, en ajout seul : création, modification (ancienne et nouvelle règle), suspension, reprise.</summary>
internal sealed class EvenementPlan : IAppartientAOrganisation
{
    private EvenementPlan()
    {
    }

    internal EvenementPlan(Guid id, PlanPreventif plan, NatureEvenementPlan nature, Guid? auteurCompteId, DateTimeOffset le, string? detail)
    {
        Id = id;
        OrganisationId = plan.OrganisationId;
        SiteId = plan.SiteId;
        PlanId = plan.Id;
        Nature = nature;
        AuteurCompteId = auteurCompteId;
        Le = le;
        Detail = detail;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid PlanId { get; private set; }

    public NatureEvenementPlan Nature { get; private set; }

    public Guid? AuteurCompteId { get; private set; }

    public DateTimeOffset Le { get; private set; }

    public string? Detail { get; private set; }
}

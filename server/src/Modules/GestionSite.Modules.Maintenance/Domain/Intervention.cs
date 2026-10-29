using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Motifs d'attente (DEC-03) ; « Autre » exige un commentaire.</summary>
internal enum MotifAttente
{
    Financement = 1,
    Livraison = 2,
    Prestataire = 3,
    AccesImpossible = 4,
    DecisionInterne = 5,
    Autre = 6,
}

internal enum StatutIntervention
{
    Affectee = 1,
    EnCours = 2,
    EnAttente = 3,
    Cloturee = 4,
}

/// <summary>
/// Intervention d'un dossier (US-09, DEC-03 : 1 à N par dossier) : réalisée par un technicien interne, ou par un
/// prestataire extérieur dont le suivi est assuré par un référent interne (décision du 29/10). La personne en charge
/// (intervenant ou référent) la démarre et la clôture ; R/D peuvent agir à sa place.
/// </summary>
internal sealed class Intervention : IAppartientAOrganisation
{
    internal const int TitreMax = 120;
    internal const int PrestataireMax = 120;

    private Intervention()
    {
    }

    internal Intervention(Guid id, Signalement dossier, int numero, string? titre, Guid? intervenantCompteId, string? prestataire, Guid? referentCompteId, DateTimeOffset maintenant)
    {
        var nomPrestataire = string.IsNullOrWhiteSpace(prestataire) ? null : TexteAffichable.Nettoyer(prestataire, PrestataireMax)
            ?? throw new SaisieInvalideException("prestataire", $"prestataire : une ligne visible de {PrestataireMax} caractères au plus.");
        // Exactement l'un des deux : un intervenant interne, ou un prestataire avec son référent interne.
        if (intervenantCompteId is not null == (nomPrestataire is not null || referentCompteId is not null))
        {
            throw new SaisieInvalideException("intervenantCompteId", "Un technicien interne, ou un prestataire et son référent interne.");
        }

        if (nomPrestataire is not null != referentCompteId is not null)
        {
            throw new SaisieInvalideException(nomPrestataire is null ? "prestataire" : "referentCompteId", "Un prestataire est suivi par un référent interne.");
        }

        Id = id;
        OrganisationId = dossier.OrganisationId;
        SiteId = dossier.SiteId;
        DossierId = dossier.Id;
        Numero = numero;
        Titre = TexteAffichable.Nettoyer(titre, TitreMax) ?? throw new SaisieInvalideException("titre", $"titre : une ligne visible de {TitreMax} caractères au plus.");
        IntervenantCompteId = intervenantCompteId;
        Prestataire = nomPrestataire;
        ReferentCompteId = referentCompteId;
        Statut = StatutIntervention.Affectee;
        CreeLe = maintenant;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid DossierId { get; private set; }

    /// <summary>Rang dans le dossier, à partir de 1.</summary>
    public int Numero { get; private set; }

    public string Titre { get; private set; } = string.Empty;

    public Guid? IntervenantCompteId { get; private set; }

    public string? Prestataire { get; private set; }

    public Guid? ReferentCompteId { get; private set; }

    public StatutIntervention Statut { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset? DemarreeLe { get; private set; }

    public MotifAttente? MotifAttente { get; private set; }

    public string? CommentaireAttente { get; private set; }

    /// <summary>Date de réexamen de l'attente en cours (DEC-03 : obligatoire).</summary>
    public DateOnly? ReexamenLe { get; private set; }

    /// <summary>Compte qui en répond : l'intervenant interne, ou le référent d'un prestataire.</summary>
    internal Guid EnCharge => IntervenantCompteId ?? ReferentCompteId!.Value;

    internal void Demarrer(DateTimeOffset maintenant)
    {
        if (Statut != StatutIntervention.Affectee)
        {
            throw new InterventionIncompatibleException(Statut);
        }

        Statut = StatutIntervention.EnCours;
        DemarreeLe = maintenant;
    }

    /// <summary>
    /// Attente (financement, livraison, prestataire…) : motif de la liste DEC-03, date de réexamen obligatoire, comprise
    /// entre aujourd'hui et un an ; commentaire obligatoire pour « Autre ».
    /// </summary>
    internal void MettreEnAttente(MotifAttente motif, string? commentaire, DateOnly? reexamenLe, DateTimeOffset maintenant)
    {
        if (Statut != StatutIntervention.EnCours)
        {
            throw new InterventionIncompatibleException(Statut);
        }

        if (!Enum.IsDefined(motif))
        {
            throw new SaisieInvalideException("motif", "Motif d'attente inconnu.");
        }

        // « Aujourd'hui » de l'utilisateur, où qu'il soit : de la date la plus en retard (UTC−12) à la plus avancée (UTC+14).
        var auPlusTot = DateOnly.FromDateTime(maintenant.ToOffset(TimeSpan.FromHours(-12)).DateTime);
        var auPlusTard = DateOnly.FromDateTime(maintenant.ToOffset(TimeSpan.FromHours(14)).DateTime).AddYears(1);
        if (reexamenLe is not { } reexamen || reexamen < auPlusTot || reexamen > auPlusTard)
        {
            throw new SaisieInvalideException("reexamenLe", "Date de réexamen entre aujourd'hui et un an.");
        }

        Statut = StatutIntervention.EnAttente;
        MotifAttente = motif;
        CommentaireAttente = Commentaire.Exiger(commentaire, motif == Domain.MotifAttente.Autre);
        ReexamenLe = reexamen;
    }

    internal void Reprendre()
    {
        if (Statut != StatutIntervention.EnAttente)
        {
            throw new InterventionIncompatibleException(Statut);
        }

        Statut = StatutIntervention.EnCours;
        MotifAttente = null;
        CommentaireAttente = null;
        ReexamenLe = null;
    }
}

/// <summary>Intervention dans un statut qui n'admet pas cette action (409 « statut_incompatible »).</summary>
internal sealed class InterventionIncompatibleException(StatutIntervention statut) : Exception($"Action impossible pour une intervention « {statut} ».");

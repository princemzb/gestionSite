using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

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
}

/// <summary>Intervention dans un statut qui n'admet pas cette action (409 « statut_incompatible »).</summary>
internal sealed class InterventionIncompatibleException(StatutIntervention statut) : Exception($"Action impossible pour une intervention « {statut} ».");

using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Événements notifiés dans le centre (US-10, S10-01) ; les rappels planifiés arrivent avec S10-02 et suivantes.</summary>
internal enum TypeNotification
{
    /// <summary>Danger signalé (dépôt public, déclaration interne, constat d'inspection) : R/D, P/C, personnes en charge (Q3-A).</summary>
    DangerSignale = 1,

    /// <summary>Activité bloquée sans danger : R/D du site.</summary>
    ActiviteBloquee = 2,

    /// <summary>Intervention affectée : l'intervenant, ou le référent interne du prestataire.</summary>
    Affectation = 3,

    /// <summary>Dossier clôturé par la clôture de sa dernière intervention ouverte (T-03.11) : R/D du site.</summary>
    ClotureAutomatique = 4,

    /// <summary>Constat d'inspection rattaché à un dossier ouvert (T-03.19) : R/D du site.</summary>
    ConstatRattache = 5,
}

/// <summary>Nature de l'objet visé, ouvert après contrôle des droits (« une URL connue n'accorde aucun droit »).</summary>
internal enum ObjetNotification
{
    Dossier = 1,
}

/// <summary>
/// Entrée du centre de notifications (source de vérité, CONTRATS « Push ») : une par destinataire et par événement (clé
/// d'unicité), écrite dans la même transaction que l'événement métier. Aucun texte libre : ni description, ni nom, ni
/// contact de déclarant ; le titre de l'objet est relu à l'affichage, droits vérifiés.
/// </summary>
internal sealed class Notification : IAppartientAOrganisation
{
    internal const int CleMax = 200;

    private Notification()
    {
    }

    internal Notification(Guid id, Signalement dossier, Guid compteId, TypeNotification type, string cle, DateTimeOffset le)
    {
        Id = id;
        OrganisationId = dossier.OrganisationId;
        SiteId = dossier.SiteId;
        CompteId = compteId != Guid.Empty ? compteId : throw new ArgumentException("Destinataire obligatoire.", nameof(compteId));
        Type = type;
        Objet = ObjetNotification.Dossier;
        ObjetId = dossier.Id;
        Cle = cle.Length is > 0 and <= CleMax ? cle : throw new ArgumentException("Clé d'événement de 1 à 200 caractères.", nameof(cle));
        CreeLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>Compte destinataire : lui seul lit et marque lue sa notification (filtre applicatif et RLS).</summary>
    public Guid CompteId { get; private set; }

    public TypeNotification Type { get; private set; }

    public ObjetNotification Objet { get; private set; }

    public Guid ObjetId { get; private set; }

    /// <summary>Événement d'origine (« danger:{alerte} », « affectation:{intervention} »…) : un rejeu n'en écrit pas une seconde.</summary>
    public string Cle { get; private set; } = string.Empty;

    /// <summary>Instant exact de l'événement (jamais différé, même pendant les heures calmes des canaux externes).</summary>
    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset? LueLe { get; private set; }

    /// <summary>Première lecture conservée : marquer à nouveau ne change rien.</summary>
    internal bool MarquerLue(DateTimeOffset le)
    {
        if (LueLe is not null)
        {
            return false;
        }

        LueLe = le;
        return true;
    }
}

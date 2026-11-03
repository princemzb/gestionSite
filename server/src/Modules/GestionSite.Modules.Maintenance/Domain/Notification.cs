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

    // Rappels du planificateur (S10-02, S10-03) : une notification par destinataire et par seuil (clé « rappel:… »).

    /// <summary>75 % du délai de prise en charge (DEC-05) : personne en charge, R/D si le dossier n'est pas affecté.</summary>
    PriseEnChargeProche = 6,

    /// <summary>Délai de prise en charge dépassé : escalade aux R/D, puis rappel quotidien aux R/D et à la personne en charge.</summary>
    PriseEnChargeDepassee = 7,

    /// <summary>75 % du délai de résolution (DEC-05, T-04.14) : personne en charge, R/D si le dossier n'est pas affecté.</summary>
    ResolutionProche = 8,

    /// <summary>Délai de résolution dépassé : escalade aux R/D, puis rappel quotidien aux R/D et à la personne en charge.</summary>
    ResolutionDepassee = 9,

    /// <summary>Dossier resté « À qualifier » 24 h, puis toutes les 24 h (Q8-A) : R/D du site.</summary>
    AQualifier = 10,

    /// <summary>Jour du réexamen d'une attente, à 8 h (Q9-A) : personne en charge de l'intervention.</summary>
    ReexamenAtteint = 11,

    /// <summary>Réexamen sans reprise ni report : escalade aux R/D à J+3, puis rappel hebdomadaire aux R/D et à la personne en charge.</summary>
    ReexamenDepasse = 12,

    // Suivi d'un danger (S10-04, Q4-A, Q-M2, Q-M3) : notifications reliées à l'alerte (AlerteId).

    /// <summary>Danger non pris en compte 15 min après l'alerte, puis toutes les heures, jour et nuit : tous les destinataires de l'alerte.</summary>
    DangerNonPrisEnCompte = 13,

    /// <summary>Mise en sécurité non déclarée, toutes les heures après la prise en compte : celui qui l'a prise, et les autres R/D au bout de 4 h.</summary>
    MiseEnSecuriteAFaire = 14,

    /// <summary>Mise en sécurité déclarée : le R/D qui a pris le danger en compte, les R/D du site si personne (Q-M3).</summary>
    MiseEnSecuriteDeclaree = 15,

    /// <summary>Mise en sécurité jugée insuffisante à la vérification : son déclarant (Q-M2).</summary>
    MiseEnSecuriteInsuffisante = 16,
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

    internal Notification(Guid id, Signalement dossier, Guid compteId, TypeNotification type, string cle, DateTimeOffset le, Guid? alerteId = null)
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
        AlerteId = alerteId;
    }

    /// <summary>
    /// Types d'une alerte de danger : marquée lue, elle reste épinglée tant que son danger n'est pas pris en compte (Q-M8) ;
    /// chaque nouvelle alerte est une nouvelle notification, donc non lue.
    /// </summary>
    internal static readonly TypeNotification[] TypesAlerte = [TypeNotification.DangerSignale, TypeNotification.DangerNonPrisEnCompte];

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

    /// <summary>Alerte de danger concernée (S10-04) ; null pour les autres notifications.</summary>
    public Guid? AlerteId { get; private set; }

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

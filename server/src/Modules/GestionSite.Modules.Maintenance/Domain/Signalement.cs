using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Incident ou demande (PRD 5.1).</summary>
internal enum NatureSignalement
{
    Incident = 1,
    Demande = 2,
}

/// <summary>Cycle d'un dossier (DEC-03, PROCESSUS : 7 statuts).</summary>
internal enum StatutDossier
{
    AQualifier = 1,
    Affecte = 2,
    EnCours = 3,
    EnAttente = 4,
    Cloture = 5,
    ClasseSansSuite = 6,
    RattacheDoublon = 7,
}

/// <summary>Priorité fixée par R/D à la qualification (DEC-05), indépendante du danger déclaré.</summary>
internal enum PrioriteDossier
{
    Urgente = 1,
    Haute = 2,
    Normale = 3,
    Basse = 4,
}

/// <summary>Motifs de classement sans suite (DEC-03) ; « Autre » exige un commentaire.</summary>
internal enum MotifClassement
{
    AucunProbleme = 1,
    HorsPerimetre = 2,
    DejaTraite = 3,
    SpamAbusif = 4,
    Autre = 5,
}

internal enum OrigineDossier
{
    Public = 1,
    Interne = 2,
}

/// <summary>Dossier principal qui a déjà des doublons rattachés : ni rattachement ailleurs ni classement (409 « doublons_rattaches »).</summary>
internal sealed class DoublonsRattachesException() : Exception("Ce dossier porte des doublons rattachés : traitez-le comme dossier principal.");

/// <summary>Affectation d'un dossier sans priorité ni zone (409 « non_qualifie ») : qualifier d'abord.</summary>
internal sealed class DossierNonQualifieException() : Exception("Qualifiez le dossier (priorité et zone) avant de l'affecter.");

/// <summary>Délais cibles par priorité (DEC-05), en temps calendaire : prise en charge et résolution.</summary>
internal static class Delais
{
    internal static TimeSpan PriseEnCharge(PrioriteDossier priorite) => priorite switch
    {
        PrioriteDossier.Urgente => TimeSpan.FromHours(4),
        PrioriteDossier.Haute => TimeSpan.FromDays(1),
        PrioriteDossier.Normale => TimeSpan.FromDays(3),
        _ => TimeSpan.FromDays(7),
    };

    internal static TimeSpan Resolution(PrioriteDossier priorite) => priorite switch
    {
        PrioriteDossier.Urgente => TimeSpan.FromDays(2),
        PrioriteDossier.Haute => TimeSpan.FromDays(7),
        PrioriteDossier.Normale => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(90),
    };
}

/// <summary>Dossier dans un statut qui n'admet pas cette action (409 « statut_incompatible »).</summary>
internal sealed class StatutIncompatibleException(StatutDossier statut) : Exception($"Action impossible pour un dossier « {statut} ».")
{
    internal StatutDossier Statut { get; } = statut;
}

/// <summary>
/// Dossier d'un site : la déclaration reçue (dépôt public RG-02, ou déclaration d'un compte interne) et son traitement
/// (US-09 : qualification, doublons, classement ; affectation et interventions ensuite). Déclarant public sans compte,
/// contact non vérifié, aucun suivi public. La commande identifie l'envoi : un rejeu ne crée pas de second dossier (F-01).
/// </summary>
internal sealed class Signalement : IAppartientAOrganisation
{
    internal const int CommentaireMax = 500;

    /// <summary>Statuts d'un dossier ouvert (à traiter) ; les autres sont clos (clôturé, classé, rattaché).</summary>
    internal static readonly StatutDossier[] StatutsOuverts = [StatutDossier.AQualifier, StatutDossier.Affecte, StatutDossier.EnCours, StatutDossier.EnAttente];
    internal const int DescriptionMax = 2000;
    internal const int EmplacementMax = 200;
    internal const int NomMax = 200;
    internal const int ContactMax = 200;

    private Signalement()
    {
    }

    internal Signalement(
        Guid id, Guid organisationId, Guid siteId, Guid commandeId, NatureSignalement nature, string? description, string? emplacement,
        bool danger, bool activiteBloquee, string? nomDeclarant, string? contactDeclarant, DateTimeOffset recuLe)
    {
        Id = Exiger(id, nameof(id));
        OrganisationId = Exiger(organisationId, nameof(organisationId));
        SiteId = Exiger(siteId, nameof(siteId));
        CommandeId = commandeId != Guid.Empty ? commandeId : throw new SaisieInvalideException("commandeId", "Identifiant d'envoi manquant.");
        Nature = Enum.IsDefined(nature) ? nature : throw new SaisieInvalideException("nature", "Nature attendue : Incident ou Demande.");
        Description = Texte.Exiger(description, DescriptionMax, "description", multiligne: true);
        Emplacement = Texte.Exiger(emplacement, EmplacementMax, "emplacement");
        Danger = danger;
        ActiviteBloquee = activiteBloquee;
        NomDeclarant = Texte.Exiger(nomDeclarant, NomMax, "nom");
        ContactDeclarant = Texte.Exiger(contactDeclarant, ContactMax, "contact");
        RecuLe = recuLe;
        Statut = StatutDossier.AQualifier;
        Origine = OrigineDossier.Public;
    }

    /// <summary>
    /// Déclaration d'un compte interne du site (US-09) : la zone est connue d'emblée ; l'emplacement précis est facultatif
    /// (à défaut, le libellé de la zone) ; le déclarant est le compte, joignable dans l'application.
    /// </summary>
    internal static Signalement Interne(
        Guid id, Guid organisationId, Guid siteId, Guid commandeId, NatureSignalement nature, string? description, string? emplacement,
        string libelleZone, Guid zoneId, bool danger, bool activiteBloquee, Guid compteId, string nomCompte, DateTimeOffset recuLe)
    {
        // Libellé « Bâtiment · Zone » raccourci si besoin : jamais refusé pour un champ que le déclarant n'a pas rempli.
        var precis = string.IsNullOrWhiteSpace(emplacement)
            ? (libelleZone.Length <= EmplacementMax ? libelleZone : string.Concat(libelleZone.AsSpan(0, EmplacementMax - 1), "…"))
            : emplacement;
        var signalement = new Signalement(
            id, organisationId, siteId, commandeId, nature, description, precis, danger, activiteBloquee, nomCompte, contactDeclarant: "—", recuLe)
        {
            Origine = OrigineDossier.Interne,
            DeclarantCompteId = Exiger(compteId, nameof(compteId)),
            ContactDeclarant = null,
            ZoneId = zoneId,
        };
        return signalement;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid CommandeId { get; private set; }

    public NatureSignalement Nature { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public string Emplacement { get; private set; } = string.Empty;

    public bool Danger { get; private set; }

    public bool ActiviteBloquee { get; private set; }

    public string NomDeclarant { get; private set; } = string.Empty;

    /// <summary>Déclaratif, non vérifié : jamais une identité authentifiée (GLOSSAIRE-DONNEES). Absent pour un compte interne.</summary>
    public string? ContactDeclarant { get; private set; } = string.Empty;

    public DateTimeOffset RecuLe { get; private set; }

    public OrigineDossier Origine { get; private set; }

    /// <summary>Compte interne déclarant (déclaration depuis l'application) ; null pour un dépôt public.</summary>
    public Guid? DeclarantCompteId { get; private set; }

    public StatutDossier Statut { get; private set; }

    public PrioriteDossier? Priorite { get; private set; }

    /// <summary>Zone de l'inventaire (obligatoire à la qualification, décision du 29/10) ; composant facultatif.</summary>
    public Guid? ZoneId { get; private set; }

    public Guid? ComposantId { get; private set; }

    public DateTimeOffset? QualifieLe { get; private set; }

    /// <summary>Première affectation : point de départ du délai de prise en charge (DEC-05).</summary>
    public DateTimeOffset? AffecteLe { get; private set; }

    /// <summary>Premier démarrage d'une intervention : fin de la prise en charge.</summary>
    public DateTimeOffset? PrisEnChargeLe { get; private set; }

    /// <summary>Dossier principal d'un doublon rattaché (la déclaration rattachée est conservée, DEC-03).</summary>
    public Guid? DoublonDe { get; private set; }

    public MotifClassement? MotifClassement { get; private set; }

    public string? CommentaireClassement { get; private set; }

    internal bool Ouvert => StatutsOuverts.Contains(Statut);

    /// <summary>
    /// Priorité et lieu, à la qualification ou ensuite tant que le dossier est ouvert (R/D, RG-04). Faux si rien ne change
    /// (aucune écriture, aucune trace).
    /// </summary>
    internal bool Qualifier(PrioriteDossier priorite, Guid zoneId, Guid? composantId, DateTimeOffset maintenant)
    {
        if (!Ouvert)
        {
            throw new StatutIncompatibleException(Statut);
        }

        if (!Enum.IsDefined(priorite))
        {
            throw new SaisieInvalideException("priorite", "Priorité inconnue.");
        }

        if (QualifieLe is not null && Priorite == priorite && ZoneId == zoneId && ComposantId == composantId)
        {
            return false;
        }

        Priorite = priorite;
        ZoneId = zoneId;
        ComposantId = composantId;
        QualifieLe ??= maintenant;
        return true;
    }

    /// <summary>Une intervention est affectée (R/D) : le dossier qualifié passe « Affecté » à la première (DEC-03).</summary>
    internal void Affecter(DateTimeOffset maintenant)
    {
        if (!Ouvert)
        {
            throw new StatutIncompatibleException(Statut);
        }

        if (Priorite is null || ZoneId is null)
        {
            throw new DossierNonQualifieException();
        }

        if (Statut == StatutDossier.AQualifier)
        {
            Statut = StatutDossier.Affecte;
        }

        AffecteLe ??= maintenant;
    }

    /// <summary>Une intervention démarre : le dossier passe « En cours » ; la première fixe la prise en charge.</summary>
    internal void Demarrer(DateTimeOffset maintenant)
    {
        if (Statut is not (StatutDossier.Affecte or StatutDossier.EnCours))
        {
            throw new StatutIncompatibleException(Statut);
        }

        Statut = StatutDossier.EnCours;
        PrisEnChargeLe ??= maintenant;
    }

    /// <summary>
    /// Rattachement à un dossier ouvert du même site, confirmé par R/D (DEC-03). Un dossier qui porte déjà des doublons
    /// reste principal : pas de chaîne de doublons.
    /// </summary>
    internal void RattacherA(Signalement principal, bool aDesDoublons)
    {
        if (Statut != StatutDossier.AQualifier)
        {
            throw new StatutIncompatibleException(Statut);
        }

        if (aDesDoublons)
        {
            throw new DoublonsRattachesException();
        }

        if (principal.Id == Id || principal.SiteId != SiteId || !principal.Ouvert || principal.DoublonDe is not null)
        {
            throw new SaisieInvalideException("principalId", "Dossier principal : un autre dossier ouvert du même site.");
        }

        DoublonDe = principal.Id;
        Statut = StatutDossier.RattacheDoublon;
    }

    /// <summary>Classement sans suite (R/D, DEC-03) ; un dossier qui porte des doublons ne se classe pas (ils resteraient orphelins).</summary>
    internal void ClasserSansSuite(MotifClassement motif, string? commentaire, bool aDesDoublons)
    {
        if (Statut != StatutDossier.AQualifier)
        {
            throw new StatutIncompatibleException(Statut);
        }

        if (aDesDoublons)
        {
            throw new DoublonsRattachesException();
        }

        if (!Enum.IsDefined(motif))
        {
            throw new SaisieInvalideException("motif", "Motif inconnu.");
        }

        var texte = string.IsNullOrWhiteSpace(commentaire) ? null : TexteAffichable.Nettoyer(commentaire, CommentaireMax, multiligne: true)
            ?? throw new SaisieInvalideException("commentaire", $"Commentaire visible de {CommentaireMax} caractères au plus.");
        if (motif == Domain.MotifClassement.Autre && texte is null)
        {
            throw new SaisieInvalideException("commentaire", "Commentaire obligatoire pour le motif « Autre ».");
        }

        MotifClassement = motif;
        CommentaireClassement = texte;
        Statut = StatutDossier.ClasseSansSuite;
    }

    /// <summary>Même envoi (rejeu) : même site et même contenu, sinon la commande est réutilisée à tort.</summary>
    internal bool MemeEnvoi(Signalement autre) =>
        (SiteId, Nature, Description, Emplacement, Danger, ActiviteBloquee, NomDeclarant, ContactDeclarant)
        == (autre.SiteId, autre.Nature, autre.Description, autre.Emplacement, autre.Danger, autre.ActiviteBloquee, autre.NomDeclarant, autre.ContactDeclarant);

    private static Guid Exiger(Guid id, string champ) =>
        id != Guid.Empty ? id : throw new ArgumentException($"{champ} est obligatoire.", champ);
}

/// <summary>
/// Danger signalé : alerte enregistrée avec le signalement, avant toute qualification (RG-05). Sa diffusion aux
/// responsables du site relève d'US-10.
/// </summary>
internal sealed class AlerteDanger : IAppartientAOrganisation
{
    private AlerteDanger()
    {
    }

    internal AlerteDanger(Guid id, Signalement signalement)
    {
        Id = id;
        OrganisationId = signalement.OrganisationId;
        SiteId = signalement.SiteId;
        SignalementId = signalement.Id;
        CreeLe = signalement.RecuLe;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid SignalementId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }
}

/// <summary>
/// Photo d'un signalement, réencodée sans métadonnées, stockée en privé. Clé propre au signalement enregistré :
/// un envoi concurrent perdant (même commande) n'écrase jamais les fichiers du gagnant.
/// </summary>
internal sealed class PhotoSignalement : IAppartientAOrganisation
{
    internal const int CleMax = 200;

    private PhotoSignalement()
    {
    }

    internal PhotoSignalement(Guid id, Signalement signalement, int rang, int taille)
    {
        Id = id;
        OrganisationId = signalement.OrganisationId;
        SignalementId = signalement.Id;
        Rang = rang;
        Cle = CleDe(signalement.OrganisationId, signalement.Id, rang);
        Taille = taille;
        CreeLe = signalement.RecuLe;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SignalementId { get; private set; }

    /// <summary>1 à 3, dans l'ordre d'envoi.</summary>
    public int Rang { get; private set; }

    public string Cle { get; private set; } = string.Empty;

    public int Taille { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    internal static string CleDe(Guid organisationId, Guid signalementId, int rang) =>
        $"organisations/{organisationId:D}/signalements/{signalementId:D}/photo-{rang}.jpg";
}

internal enum NatureEvenementDossier
{
    Declaration = 1,
    Qualification = 2,
    RattachementDoublon = 3,
    ClassementSansSuite = 4,
    Affectation = 5,
    Demarrage = 6,
}

/// <summary>Journal d'un dossier, en ajout seul : auteur, date, nature et détail (JSON) de chaque décision.</summary>
internal sealed class EvenementDossier : IAppartientAOrganisation
{
    private EvenementDossier()
    {
    }

    internal EvenementDossier(Guid id, Signalement dossier, NatureEvenementDossier nature, Guid? auteurCompteId, DateTimeOffset le, string? detail)
    {
        Id = id;
        OrganisationId = dossier.OrganisationId;
        SiteId = dossier.SiteId;
        DossierId = dossier.Id;
        Nature = nature;
        AuteurCompteId = auteurCompteId;
        Le = le;
        Detail = detail;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid DossierId { get; private set; }

    public NatureEvenementDossier Nature { get; private set; }

    public Guid? AuteurCompteId { get; private set; }

    public DateTimeOffset Le { get; private set; }

    public string? Detail { get; private set; }
}

using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>
/// Mesure conservatoire déclarée (US-10, S10-04 ; liste fermée du cadrage §1.1, choix de l'agent révisable) ; « Autre » exige
/// un commentaire. « Énergie coupée » : l'écran rappelle qu'une consignation électrique est réservée à une personne habilitée
/// (NF C 18-510) ; l'application ne vérifie pas l'habilitation.
/// </summary>
internal enum ActionMiseEnSecurite
{
    /// <summary>Accès condamné ou balisé.</summary>
    AccesCondamne = 1,

    /// <summary>Public tenu à distance, zone interdite.</summary>
    PublicTenuADistance = 2,

    /// <summary>Énergie coupée (électricité, gaz ou eau).</summary>
    EnergieCoupee = 3,

    Evacuation = 4,

    /// <summary>Protection ou étaiement provisoire.</summary>
    ProtectionProvisoire = 5,

    SecoursAppeles = 6,

    Autre = 7,
}

/// <summary>Fin du suivi d'un danger : levé (réparé) ou écarté (aucun danger à traiter).</summary>
internal enum FinDanger
{
    Leve = 1,
    Ecarte = 2,
}

/// <summary>Motif d'un danger levé (règle d'écran du 03/11, Q-M4) ; « Autre » exige un commentaire.</summary>
internal enum MotifLevee
{
    /// <summary>Réparation faite et dossier clôturé « Résolu » (vérifié : dossier clôturé par une intervention « Résolu »).</summary>
    ReparationDossierResolu = 1,

    /// <summary>Réparation constatée sur place.</summary>
    ReparationConstatee = 2,

    Autre = 3,
}

/// <summary>Motif d'un danger écarté (cadrage §1.1) ; « Autre » exige un commentaire.</summary>
internal enum MotifEcartement
{
    AucunDangerConstate = 1,
    DejaTraite = 2,
    Autre = 3,
}

/// <summary>
/// État affiché d'un danger, déduit de ses étapes : « Ouvert » tant qu'aucun R/D ne l'a pris en compte (même si une mise en
/// sécurité est déjà déclarée, Q-M1) ; l'ordre va du moins au plus avancé (l'état d'un dossier est celui de son alerte la
/// moins avancée).
/// </summary>
internal enum EtatDanger
{
    Ouvert = 1,
    PrisEnCompte = 2,
    MiseEnSecuriteDeclaree = 3,
    MiseEnSecuriteVerifiee = 4,
    Leve = 5,
    Ecarte = 6,
}

/// <summary>Étape impossible dans l'état actuel du danger (409, avec un code propre à la situation).</summary>
internal sealed class DangerIncompatibleException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}

/// <summary>
/// Danger signalé (RG-05) : alerte enregistrée avec le dossier, avant toute qualification, ou par un constat d'inspection
/// (T-03.20) ; puis suivi parallèle au dossier (US-10, S10-04, PROCESSUS « mise en sécurité ») : prise en compte par un R/D,
/// mise en sécurité déclarée (T, R/D, P/C, dès l'alerte ouverte, Q-M1) puis vérifiée par un R/D (« elle ne suffit pas » la
/// défait, Q-M2), enfin danger levé ou écarté par un R/D. La clôture du dossier ne lève pas le danger d'elle-même.
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

    /// <summary>Danger relevé par un constat d'inspection (T-03.20) : une alerte par constat, même rattaché à un dossier existant.</summary>
    internal AlerteDanger(Guid id, Signalement dossier, Guid constatId, DateTimeOffset le)
        : this(id, dossier)
    {
        ConstatId = constatId;
        CreeLe = le;
    }

    /// <summary>Constat d'inspection à l'origine de l'alerte ; null pour le danger déclaré avec le dossier.</summary>
    public Guid? ConstatId { get; private set; }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid SignalementId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    /// <summary>Prise en compte par un R/D : arrête les nouvelles alertes (Q4-A).</summary>
    public DateTimeOffset? PrisEnCompteLe { get; private set; }

    public Guid? PrisEnCompteParCompteId { get; private set; }

    /// <summary>
    /// Début du cycle des rappels horaires « mise en sécurité à faire » (Q4-A) : la prise en compte, puis le refus d'une mise
    /// en sécurité jugée insuffisante (Q-M2), qui relance le cycle.
    /// </summary>
    public DateTimeOffset? RappelsSecuriteDepuis { get; private set; }

    /// <summary>Mise en sécurité déclarée en cours (null : aucune, ou défaite par une vérification négative).</summary>
    public ActionMiseEnSecurite? SecuriteAction { get; private set; }

    public string? SecuriteCommentaire { get; private set; }

    public DateTimeOffset? SecuriteDeclareeLe { get; private set; }

    public Guid? SecuriteDeclareeParCompteId { get; private set; }

    public DateTimeOffset? SecuriteVerifieeLe { get; private set; }

    public Guid? SecuriteVerifieeParCompteId { get; private set; }

    public FinDanger? Fin { get; private set; }

    /// <summary>Motif de la fin : nom d'un <see cref="MotifLevee"/> ou d'un <see cref="MotifEcartement"/>.</summary>
    public string? FinMotif { get; private set; }

    public string? FinCommentaire { get; private set; }

    public DateTimeOffset? FinLe { get; private set; }

    public Guid? FinParCompteId { get; private set; }

    internal bool EnCours => Fin is null;

    internal EtatDanger Etat => Fin switch
    {
        FinDanger.Leve => EtatDanger.Leve,
        FinDanger.Ecarte => EtatDanger.Ecarte,
        _ when PrisEnCompteLe is null => EtatDanger.Ouvert,
        _ when SecuriteVerifieeLe is not null => EtatDanger.MiseEnSecuriteVerifiee,
        _ when SecuriteDeclareeLe is not null => EtatDanger.MiseEnSecuriteDeclaree,
        _ => EtatDanger.PrisEnCompte,
    };

    /// <summary>Prise en compte par un R/D ; une seule retenue (la seconde est refusée).</summary>
    internal void PrendreEnCompte(Guid compteId, DateTimeOffset le)
    {
        ExigerEnCours();
        if (PrisEnCompteLe is not null)
        {
            throw new DangerIncompatibleException("deja_pris_en_compte", "Ce danger est déjà pris en compte.");
        }

        PrisEnCompteLe = le;
        PrisEnCompteParCompteId = compteId;
        RappelsSecuriteDepuis = le;
    }

    /// <summary>Mise en sécurité déclarée réalisée (T, R/D, P/C), dès l'alerte ouverte (Q-M1) ; une seule à la fois.</summary>
    internal void DeclarerMiseEnSecurite(ActionMiseEnSecurite action, string? commentaire, Guid compteId, DateTimeOffset le)
    {
        ExigerEnCours();
        if (!Enum.IsDefined(action))
        {
            throw new SaisieInvalideException("action", "Action de mise en sécurité inconnue.");
        }

        if (SecuriteDeclareeLe is not null)
        {
            throw new DangerIncompatibleException("securite_deja_declaree", "Une mise en sécurité est déjà déclarée pour ce danger.");
        }

        SecuriteCommentaire = Commentaire.Exiger(commentaire, action == ActionMiseEnSecurite.Autre);
        SecuriteAction = action;
        SecuriteDeclareeLe = le;
        SecuriteDeclareeParCompteId = compteId;
    }

    /// <summary>
    /// Vérification par un R/D, après la prise en compte : suffisante, le danger est maîtrisé (« mis en sécurité, à réparer ») ;
    /// sinon (motif obligatoire) la déclaration est défaite, le danger redevient « Pris en compte » et les rappels horaires
    /// repartent (Q-M2). Rend le déclarant de la mise en sécurité vérifiée.
    /// </summary>
    internal (Guid Declarant, ActionMiseEnSecurite Action, string? Motif) VerifierMiseEnSecurite(bool suffisante, string? motif, Guid compteId, DateTimeOffset le)
    {
        ExigerEnCours();
        ExigerPrisEnCompte();
        if (SecuriteDeclareeLe is null)
        {
            throw new DangerIncompatibleException("securite_non_declaree", "Aucune mise en sécurité à vérifier pour ce danger.");
        }

        if (SecuriteVerifieeLe is not null)
        {
            throw new DangerIncompatibleException("securite_deja_verifiee", "La mise en sécurité de ce danger est déjà vérifiée.");
        }

        var declarant = SecuriteDeclareeParCompteId!.Value;
        var action = SecuriteAction!.Value;
        if (suffisante)
        {
            SecuriteVerifieeLe = le;
            SecuriteVerifieeParCompteId = compteId;
            return (declarant, action, null);
        }

        var raison = TexteAffichable.Nettoyer(motif, Signalement.CommentaireMax, multiligne: true)
            ?? throw new SaisieInvalideException("motif", $"Motif obligatoire : texte visible de {Signalement.CommentaireMax} caractères au plus.");
        SecuriteAction = null;
        SecuriteCommentaire = null;
        SecuriteDeclareeLe = null;
        SecuriteDeclareeParCompteId = null;
        RappelsSecuriteDepuis = le;
        return (declarant, action, raison);
    }

    /// <summary>
    /// Danger levé par un R/D, après la prise en compte (réparé, avec ou sans mise en sécurité préalable) ; « réparation faite
    /// et dossier clôturé Résolu » suppose un dossier clôturé par une intervention « Résolu ».
    /// </summary>
    internal void Lever(MotifLevee motif, string? commentaire, bool dossierResolu, Guid compteId, DateTimeOffset le)
    {
        ExigerEnCours();
        ExigerPrisEnCompte();
        if (!Enum.IsDefined(motif))
        {
            throw new SaisieInvalideException("motif", "Motif inconnu.");
        }

        if (motif == MotifLevee.ReparationDossierResolu && !dossierResolu)
        {
            throw new DangerIncompatibleException("dossier_non_resolu", "Le dossier n'est pas clôturé « Résolu » : choisissez un autre motif.");
        }

        Terminer(FinDanger.Leve, motif.ToString(), Commentaire.Exiger(commentaire, motif == MotifLevee.Autre), compteId, le);
    }

    /// <summary>Danger écarté par un R/D, sans mise en sécurité déclarée (sinon : le lever) ; motif obligatoire.</summary>
    internal void Ecarter(MotifEcartement motif, string? commentaire, Guid compteId, DateTimeOffset le)
    {
        ExigerEnCours();
        if (!Enum.IsDefined(motif))
        {
            throw new SaisieInvalideException("motif", "Motif inconnu.");
        }

        if (SecuriteDeclareeLe is not null)
        {
            throw new DangerIncompatibleException("securite_declaree", "Une mise en sécurité est déclarée : levez le danger plutôt que de l'écarter.");
        }

        Terminer(FinDanger.Ecarte, motif.ToString(), Commentaire.Exiger(commentaire, motif == MotifEcartement.Autre), compteId, le);
    }

    private void Terminer(FinDanger fin, string motif, string? commentaire, Guid compteId, DateTimeOffset le)
    {
        Fin = fin;
        FinMotif = motif;
        FinCommentaire = commentaire;
        FinLe = le;
        FinParCompteId = compteId;
    }

    private void ExigerEnCours()
    {
        if (Fin is not null)
        {
            throw new DangerIncompatibleException("danger_clos", "Ce danger est déjà levé ou écarté.");
        }
    }

    private void ExigerPrisEnCompte()
    {
        if (PrisEnCompteLe is null)
        {
            throw new DangerIncompatibleException("non_pris_en_compte", "Prenez d'abord le danger en compte.");
        }
    }
}

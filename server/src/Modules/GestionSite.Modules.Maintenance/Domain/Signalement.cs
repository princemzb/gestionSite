using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Incident ou demande (PRD 5.1).</summary>
internal enum NatureSignalement
{
    Incident = 1,
    Demande = 2,
}

/// <summary>
/// Signalement reçu sur un site (dépôt public, RG-02) : déclarant sans compte, contact non vérifié, aucun suivi
/// public. La commande identifie l'envoi : un rejeu ne crée pas de second signalement (F-01). Son traitement
/// (qualification, affectation) relève d'US-09.
/// </summary>
internal sealed class Signalement : IAppartientAOrganisation
{
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

    /// <summary>Déclaratif, non vérifié : jamais une identité authentifiée (GLOSSAIRE-DONNEES).</summary>
    public string ContactDeclarant { get; private set; } = string.Empty;

    public DateTimeOffset RecuLe { get; private set; }

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

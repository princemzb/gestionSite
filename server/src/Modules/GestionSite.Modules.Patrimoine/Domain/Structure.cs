using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

internal static class Nom
{
    internal const int Max = 120;

    internal static string Exiger(string? nom, string champ = "nom") =>
        TexteAffichable.Nettoyer(nom, Max) ?? throw new SaisieInvalideException(champ, $"{champ} : une ligne visible de {Max} caractères au plus.");
}

/// <summary>Bâtiment d'un site (REGLES « Inventaire ») ; les extérieurs se décrivent comme un bâtiment.</summary>
internal sealed class Batiment : IAppartientAOrganisation
{
    private Batiment()
    {
    }

    internal Batiment(Guid id, Guid organisationId, Guid siteId, string? nom, DateTimeOffset creeLe)
    {
        Id = id;
        OrganisationId = organisationId;
        SiteId = siteId;
        Nom = Domain.Nom.Exiger(nom);
        CreeLe = creeLe;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public DateTimeOffset CreeLe { get; private set; }

    /// <summary>Classement déclaré (US-12) ; null tant qu'il n'est pas renseigné.</summary>
    public bool? Erp { get; private set; }

    public TypeErp? TypeEtablissement { get; private set; }

    public string? TypesSecondaires { get; private set; }

    public int? Categorie { get; private set; }

    public int? EffectifDeclare { get; private set; }

    public bool LocauxSommeil { get; private set; }

    public bool MonumentHistorique { get; private set; }

    public ReponseConnue? PermisAvant1997 { get; private set; }

    public DateOnly? DerniereVisiteCommission { get; private set; }

    internal ClassementErp? Classement => Erp is { } erp && PermisAvant1997 is { } permis
        ? new ClassementErp(erp, TypeEtablissement, ClassementErp.Separer(TypesSecondaires), Categorie, EffectifDeclare, LocauxSommeil, MonumentHistorique,
            permis, DerniereVisiteCommission)
        : null;

    internal void Renommer(string? nom) => Nom = Domain.Nom.Exiger(nom);

    /// <summary>Classement déjà contrôlé (ClassementErp.Valider).</summary>
    internal void Classer(ClassementErp classement)
    {
        Erp = classement.Erp;
        TypeEtablissement = classement.Type;
        TypesSecondaires = ClassementErp.Joindre(classement.TypesSecondaires);
        Categorie = classement.Categorie;
        EffectifDeclare = classement.Effectif;
        LocauxSommeil = classement.LocauxSommeil;
        MonumentHistorique = classement.MonumentHistorique;
        PermisAvant1997 = classement.PermisAvant1997;
        DerniereVisiteCommission = classement.DerniereVisiteCommission;
    }
}

/// <summary>Niveau (étage) d'un bâtiment ; l'ordre range les niveaux de bas en haut (sous-sol négatif).</summary>
internal sealed class Niveau : IAppartientAOrganisation
{
    private Niveau()
    {
    }

    internal Niveau(Guid id, Batiment batiment, string? nom, int ordre)
    {
        Id = id;
        OrganisationId = batiment.OrganisationId;
        SiteId = batiment.SiteId;
        BatimentId = batiment.Id;
        Nom = Domain.Nom.Exiger(nom);
        Ordre = ordre is >= -10 and <= 100 ? ordre : throw new SaisieInvalideException("ordre", "Ordre du niveau entre -10 et 100.");
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid BatimentId { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public int Ordre { get; private set; }

    internal void Renommer(string? nom) => Nom = Domain.Nom.Exiger(nom);
}

/// <summary>
/// Pièce ou zone : unité de localisation et d'agrégation du score (DEC-01). La zone « Parties communes » de chaque
/// bâtiment reçoit les éléments communs à plusieurs pièces ; elle n'a pas de niveau et ne se renomme pas.
/// </summary>
internal sealed class Zone : IAppartientAOrganisation
{
    internal const string NomPartiesCommunes = "Parties communes";

    private Zone()
    {
    }

    internal Zone(Guid id, Batiment batiment, Niveau? niveau, string? nom)
    {
        if (niveau is not null && niveau.BatimentId != batiment.Id)
        {
            throw new SaisieInvalideException("niveauId", "Ce niveau n'appartient pas au bâtiment.");
        }

        Id = id;
        OrganisationId = batiment.OrganisationId;
        SiteId = batiment.SiteId;
        BatimentId = batiment.Id;
        NiveauId = niveau?.Id;
        Nom = Domain.Nom.Exiger(nom);
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid BatimentId { get; private set; }

    public Guid? NiveauId { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public bool PartiesCommunes { get; private set; }

    internal static Zone PartiesCommunesDe(Guid id, Batiment batiment) =>
        new(id, batiment, niveau: null, NomPartiesCommunes) { PartiesCommunes = true };

    internal void Renommer(string? nom)
    {
        if (PartiesCommunes)
        {
            throw new SaisieInvalideException("nom", "La zone « Parties communes » ne se renomme pas.");
        }

        Nom = Domain.Nom.Exiger(nom);
    }
}

internal enum CibleInventaire
{
    Batiment = 1,
    Niveau = 2,
    Zone = 3,
    Composant = 4,

    /// <summary>Données du site pour le préventif (US-12 : salariés présents).</summary>
    Site = 5,
}

internal enum NatureEvenementInventaire
{
    Creation = 1,
    Modification = 2,
    Retrait = 3,

    /// <summary>Répartition d'un lot reprise d'une évaluation (US-11).</summary>
    Evaluation = 4,
}

/// <summary>Journal de l'inventaire, en ajout seul (REGLES « Inventaire ») : valeurs avant et après en JSON.</summary>
internal sealed class EvenementInventaire : IAppartientAOrganisation
{
    private EvenementInventaire()
    {
    }

    internal EvenementInventaire(
        Guid id, Guid organisationId, Guid siteId, CibleInventaire cible, Guid cibleId, NatureEvenementInventaire nature,
        Guid? auteurCompteId, DateTimeOffset le, string? avant, string? apres)
    {
        Id = id;
        OrganisationId = organisationId;
        SiteId = siteId;
        Cible = cible;
        CibleId = cibleId;
        Nature = nature;
        AuteurCompteId = auteurCompteId;
        Le = le;
        Avant = avant;
        Apres = apres;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public CibleInventaire Cible { get; private set; }

    public Guid CibleId { get; private set; }

    public NatureEvenementInventaire Nature { get; private set; }

    public Guid? AuteurCompteId { get; private set; }

    public DateTimeOffset Le { get; private set; }

    public string? Avant { get; private set; }

    public string? Apres { get; private set; }
}

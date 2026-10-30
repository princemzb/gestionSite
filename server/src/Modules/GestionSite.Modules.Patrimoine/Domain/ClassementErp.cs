using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>
/// Types d'établissement recevant du public installés dans un bâtiment (arrêté du 25 juin 1980, GN 1) : V pour un lieu
/// de culte, L pour une salle paroissiale, R pour un enseignement, N pour un restaurant, S pour une bibliothèque…
/// </summary>
internal enum TypeErp
{
    J = 1,
    L = 2,
    M = 3,
    N = 4,
    O = 5,
    P = 6,
    R = 7,
    S = 8,
    T = 9,
    U = 10,
    V = 11,
    W = 12,
    X = 13,
    Y = 14,
}

/// <summary>Réponse à une question de fait qui peut rester inconnue (permis de construire avant le 1er juillet 1997).</summary>
internal enum ReponseConnue
{
    Oui = 1,
    Non = 2,
    Inconnu = 3,
}

/// <summary>
/// Classement d'un bâtiment (US-12, S12-01), déclaré par R/D d'après l'arrêté ou le procès-verbal de la commission :
/// jamais calculé par l'application, qui n'affiche jamais « conforme ». Il ajuste seulement les plans proposés.
/// Sans objet hors ERP : type, types secondaires, catégorie, effectif, locaux à sommeil et visite de la commission.
/// </summary>
internal sealed record ClassementErp(
    bool Erp, TypeErp? Type, IReadOnlyList<TypeErp> TypesSecondaires, int? Categorie, int? Effectif, bool LocauxSommeil,
    bool MonumentHistorique, ReponseConnue PermisAvant1997, DateOnly? DerniereVisiteCommission)
{
    internal const int EffectifMax = 100_000;
    internal const int TypesSecondairesMax = 5;
    internal static readonly DateOnly VisiteMin = new(1950, 1, 1);

    /// <summary>
    /// Classement contrôlé : un ERP a une catégorie (1 à 5) et un type principal (V à défaut) ; ses types secondaires sont
    /// distincts du principal ; la visite de la commission n'est pas dans le futur, jugée au fuseau le plus avancé (UTC+14).
    /// </summary>
    internal static ClassementErp Valider(
        bool? erp, TypeErp? type, IReadOnlyList<TypeErp>? secondaires, int? categorie, int? effectif, bool? locauxSommeil, bool? monument,
        ReponseConnue? permis, DateOnly? visite, DateTimeOffset maintenant)
    {
        var estErp = erp ?? throw new SaisieInvalideException("erp", "Indiquer si le bâtiment est un ERP.");
        var monumentHistorique = monument ?? throw new SaisieInvalideException("monumentHistorique", "Indiquer si le bâtiment est classé ou inscrit.");
        var permisAvant1997 = permis is { } p && Enum.IsDefined(p)
            ? p
            : throw new SaisieInvalideException("permisAvant1997", "Permis de construire avant le 1er juillet 1997 : Oui, Non ou Inconnu.");
        secondaires ??= [];
        if (!estErp)
        {
            if (type is not null || secondaires.Count > 0 || categorie is not null || effectif is not null || locauxSommeil == true || visite is not null)
            {
                throw new SaisieInvalideException("erp", "Type, catégorie, effectif, locaux à sommeil et commission : sans objet hors ERP.");
            }

            return new ClassementErp(false, null, [], null, null, false, monumentHistorique, permisAvant1997, null);
        }

        var principal = type ?? TypeErp.V;
        if (!Enum.IsDefined(principal))
        {
            throw new SaisieInvalideException("type", "Type d'ERP inconnu.");
        }

        if (secondaires.Count > TypesSecondairesMax || secondaires.Any(t => !Enum.IsDefined(t) || t == principal) || secondaires.Distinct().Count() != secondaires.Count)
        {
            throw new SaisieInvalideException("typesSecondaires", $"Types secondaires : {TypesSecondairesMax} au plus, distincts du type principal.");
        }

        if (categorie is not (>= 1 and <= 5))
        {
            throw new SaisieInvalideException("categorie", "Catégorie déclarée de 1 à 5.");
        }

        if (effectif is < 0 or > EffectifMax)
        {
            throw new SaisieInvalideException("effectif", $"Effectif déclaré de 0 à {EffectifMax}.");
        }

        if (visite is { } jour && (jour < VisiteMin || jour > DateOnly.FromDateTime(maintenant.ToOffset(TimeSpan.FromHours(14)).DateTime)))
        {
            throw new SaisieInvalideException("derniereVisiteCommission", "Date de la dernière visite de la commission : passée.");
        }

        return new ClassementErp(
            true, principal, [.. secondaires.Order()], categorie, effectif, locauxSommeil ?? false, monumentHistorique, permisAvant1997, visite);
    }

    /// <summary>Types secondaires enregistrés en une colonne (« L,N »), dans l'ordre de l'énumération.</summary>
    internal static string? Joindre(IReadOnlyList<TypeErp> types) => types.Count == 0 ? null : string.Join(',', types);

    internal static IReadOnlyList<TypeErp> Separer(string? types) =>
        string.IsNullOrEmpty(types) ? [] : [.. types.Split(',').Select(Enum.Parse<TypeErp>)];

    /// <summary>Valeurs relevées pour le journal (RG-31 : avant et après, champ par champ).</summary>
    internal Dictionary<string, object?> Releve() => new()
    {
        ["erp"] = Erp,
        ["type"] = Type?.ToString(),
        ["typesSecondaires"] = Joindre(TypesSecondaires),
        ["categorie"] = Categorie,
        ["effectif"] = Effectif,
        ["locauxSommeil"] = LocauxSommeil,
        ["monumentHistorique"] = MonumentHistorique,
        ["permisAvant1997"] = PermisAvant1997.ToString(),
        ["derniereVisiteCommission"] = DerniereVisiteCommission?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Données du site utiles au préventif (US-12) : salariés présents, ce qui ajoute les obligations du Code du travail
/// (électricité annuelle, portes automatiques…). Une ligne par site, créée à la première déclaration.
/// </summary>
internal sealed class EtablissementSite : IAppartientAOrganisation
{
    private EtablissementSite()
    {
    }

    internal EtablissementSite(Guid siteId, Guid organisationId, bool salariesPresents, DateTimeOffset le)
    {
        SiteId = siteId;
        OrganisationId = organisationId;
        SalariesPresents = salariesPresents;
        ModifieLe = le;
    }

    public Guid SiteId { get; private set; }

    public Guid OrganisationId { get; private set; }

    public bool SalariesPresents { get; private set; }

    public DateTimeOffset ModifieLe { get; private set; }

    internal void Declarer(bool salariesPresents, DateTimeOffset le)
    {
        SalariesPresents = salariesPresents;
        ModifieLe = le;
    }
}

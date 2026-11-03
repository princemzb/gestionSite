using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Site (église, bâtiment ou ensemble) d'une organisation ; les habilitations sont accordées par site.</summary>
internal sealed class Site : IAppartientAOrganisation
{
    internal const int NomMaxLength = NomAffichable.MaxLength;

    internal const int NumeroUrgenceMaxLength = 30;

    internal const int ConsigneDangerMaxLength = 500;

    /// <summary>Consigne de danger par défaut (cadrage US-10 §1.1, règle d'écran Q-M6 du 03/11), tant que le site n'en a pas d'autre.</summary>
    internal const string ConsigneDangerParDefaut =
        "Éloignez les personnes de la zone, n'intervenez pas vous-même sur l'installation, appelez le 18 ou le 112 en cas de feu, " +
        "de fumée, d'odeur de gaz ou de blessé.";

    private Site()
    {
    }

    internal Site(Guid id, Guid organisationId, string nom, DateTimeOffset creeLe)
    {
        Identifiants.ExigerNonVide(id, nameof(id));
        Identifiants.ExigerNonVide(organisationId, nameof(organisationId));
        Id = id;
        OrganisationId = organisationId;
        Nom = NomAffichable.Valider(nom, nameof(nom));
        CreeLe = Horodatage.Valider(creeLe, nameof(creeLe));
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public DateTimeOffset CreeLe { get; private set; }

    /// <summary>Numéro de téléphone (chiffres, espaces, + . - et parenthèses) ; vide : aucune consigne affichée.</summary>
    internal void DefinirNumeroUrgence(string? numero)
    {
        var saisi = numero?.Trim();
        if (string.IsNullOrEmpty(saisi))
        {
            NumeroUrgence = null;
            return;
        }

        if (saisi.Length > NumeroUrgenceMaxLength || saisi.Count(char.IsAsciiDigit) < 2
            || !saisi.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '.' or '-' or '(' or ')'))
        {
            throw new ArgumentException("Numéro de téléphone attendu (chiffres, espaces, + . - et parenthèses).", nameof(numero));
        }

        NumeroUrgence = saisi;
    }

    /// <summary>Numéro affiché au déclarant public qui signale un danger (décision du 28/10) ; absent : aucune consigne.</summary>
    public string? NumeroUrgence { get; private set; }

    /// <summary>Consigne de danger propre au site (US-10, Q-M6) ; null : consigne par défaut.</summary>
    public string? ConsigneDanger { get; private set; }

    /// <summary>Consigne affichée à côté du numéro d'urgence : celle du site, sinon celle par défaut.</summary>
    internal string ConsigneDangerAffichee => ConsigneDanger ?? ConsigneDangerParDefaut;

    /// <summary>Consigne propre au site (500 caractères visibles au plus, plusieurs lignes) ; vide ou égale au défaut : défaut.</summary>
    internal void DefinirConsigneDanger(string? consigne)
    {
        if (string.IsNullOrWhiteSpace(consigne))
        {
            ConsigneDanger = null;
            return;
        }

        var texte = TexteAffichable.Nettoyer(consigne, ConsigneDangerMaxLength, multiligne: true)
            ?? throw new ArgumentException($"Consigne : texte visible de {ConsigneDangerMaxLength} caractères au plus.", nameof(consigne));
        ConsigneDanger = texte == ConsigneDangerParDefaut ? null : texte;
    }
}

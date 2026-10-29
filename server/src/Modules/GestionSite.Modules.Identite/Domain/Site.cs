using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Site (église, bâtiment ou ensemble) d'une organisation ; les habilitations sont accordées par site.</summary>
internal sealed class Site : IAppartientAOrganisation
{
    internal const int NomMaxLength = NomAffichable.MaxLength;

    internal const int NumeroUrgenceMaxLength = 30;

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
}

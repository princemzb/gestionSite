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

    /// <summary>Numéro affiché au déclarant public qui signale un danger (décision du 28/10) ; absent : aucune consigne.</summary>
    public string? NumeroUrgence { get; private set; }
}

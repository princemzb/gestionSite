namespace GestionSite.Modules.Identite.Domain;

/// <summary>Donnée rattachée à une organisation : filtrée par organisation et protégée par la RLS.</summary>
internal interface IAppartientAOrganisation
{
    Guid OrganisationId { get; }
}

namespace GestionSite.SharedKernel.Persistence;

/// <summary>Donnée rattachée à une organisation : filtrée par organisation et protégée par la RLS.</summary>
public interface IAppartientAOrganisation
{
    Guid OrganisationId { get; }
}

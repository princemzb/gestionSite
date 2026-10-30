using GestionSite.Modules.Identite.Contracts;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Droit nominatif tel qu'enregistré dans le journal des habilitations (instantané).</summary>
internal sealed record DroitJournalise(string Droit, Guid SiteId);

/// <summary>
/// Droit accordé nommément à un membre sur un site, en plus de ses rôles (US-21, décision du porteur du 30/10) :
/// seul l'administrateur l'attribue, avec un motif, à un membre ayant un rôle sur ce site ; liste blanche
/// (<c>MatriceDroits.Nominatifs</c>). Sans rôle sur le site, il n'ouvre rien. Le retrait supprime la ligne ; l'historique
/// est dans le journal des habilitations.
/// </summary>
internal sealed class DroitNominatif : IAppartientAOrganisation
{
    private DroitNominatif()
    {
    }

    internal DroitNominatif(Guid id, Membre membre, Droit droit, Site site, Guid? accordeParCompteId, DateTimeOffset accordeLe)
    {
        ArgumentNullException.ThrowIfNull(membre);
        ArgumentNullException.ThrowIfNull(site);
        if (site.OrganisationId != membre.OrganisationId)
        {
            throw new ArgumentException("Le site appartient à une autre organisation.", nameof(site));
        }

        Identifiants.ExigerNonVide(id, nameof(id));
        Id = id;
        OrganisationId = membre.OrganisationId;
        MembreId = membre.Id;
        SiteId = site.Id;
        Droit = droit;
        AccordeParCompteId = accordeParCompteId;
        AccordeLe = Horodatage.Valider(accordeLe, nameof(accordeLe));
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid MembreId { get; private set; }

    public Guid SiteId { get; private set; }

    public Droit Droit { get; private set; }

    public Guid? AccordeParCompteId { get; private set; }

    public DateTimeOffset AccordeLe { get; private set; }

    internal static IReadOnlyCollection<DroitJournalise> Instantane(IEnumerable<DroitNominatif> droits) =>
        [.. droits.OrderBy(d => d.Droit).ThenBy(d => d.SiteId).Select(d => new DroitJournalise(d.Droit.ToString(), d.SiteId))];
}

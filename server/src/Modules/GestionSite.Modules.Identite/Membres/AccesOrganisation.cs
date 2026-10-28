using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>
/// Entrée dans une organisation de la route : l'appelant doit y avoir une adhésion active (lue par la
/// politique RLS « par compte »), sinon 404 (jamais 403, pas d'énumération entre organisations, CONTRATS).
/// Ensuite seulement l'organisation devient courante (filtres EF et RLS d'organisation).
/// </summary>
internal sealed class AccesOrganisation(IdentiteDbContext context, CompteCourant compte, OrganisationCourante organisation)
{
    internal async Task<Membre> EntrerAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        var compteId = compte.Id ?? throw RefusException.Introuvable();
        var appelant = await context.Membres.IgnoreQueryFilters().Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.OrganisationId == organisationId && m.CompteId == compteId && m.RevoqueLe == null, cancellationToken)
            .ConfigureAwait(false) ?? throw RefusException.Introuvable();
        organisation.Definir(organisationId);
        return appelant;
    }

    internal async Task<Membre> EntrerCommeAdministrateurAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        var appelant = await EntrerAsync(organisationId, cancellationToken).ConfigureAwait(false);
        return appelant.EstAdministrateur ? appelant : throw RefusException.Interdit();
    }
}

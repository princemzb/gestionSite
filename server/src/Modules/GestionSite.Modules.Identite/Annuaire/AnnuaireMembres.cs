using GestionSite.Modules.Identite.Autorisation;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Annuaire;

// Membres révoqués compris : l'auteur d'une opération passée reste nommé dans l'historique.
internal sealed class AnnuaireMembres(IdentiteDbContext context) : IAnnuaireMembres
{
    public async Task<IReadOnlyDictionary<Guid, string>> NomsAsync(IReadOnlyCollection<Guid> compteIds, CancellationToken cancellationToken)
    {
        if (compteIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await (
                from m in context.Membres.AsNoTracking()
                join c in context.Users.AsNoTracking() on m.CompteId equals c.Id
                where compteIds.Contains(m.CompteId)
                select new { c.Id, c.NomAffiche })
            .Distinct().ToDictionaryAsync(x => x.Id, x => x.NomAffiche, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MembreSite>> MembresAyantDroitAsync(
        Guid siteId, Droit droit, IReadOnlyCollection<Guid>? compteIds, CancellationToken cancellationToken)
    {
        var roles = MatriceDroits.RolesPour(droit).ToList();
        var requete = context.Membres.AsNoTracking().Include(m => m.Roles)
            .Where(m => m.RevoqueLe == null && m.AccepteLe != null && m.Roles.Any(r => r.SiteId == siteId && roles.Contains(r.Role)));
        if (compteIds is not null)
        {
            requete = requete.Where(m => compteIds.Contains(m.CompteId));
        }

        var membres = await requete.ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = membres.Select(m => m.CompteId).ToList();
        var noms = await context.Users.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.NomAffiche, cancellationToken).ConfigureAwait(false);
        return [.. membres
            .Select(m => new MembreSite(
                m.CompteId, noms.GetValueOrDefault(m.CompteId) ?? string.Empty,
                [.. m.Roles.Where(r => r.SiteId == siteId).Select(r => r.Role.ToString()).Order(StringComparer.Ordinal)]))
            .OrderBy(m => m.Nom, StringComparer.CurrentCulture)];
    }
}

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
}

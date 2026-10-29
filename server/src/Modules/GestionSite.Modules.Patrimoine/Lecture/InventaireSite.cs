using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.Modules.Patrimoine.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Lecture;

internal sealed class InventaireSite(PatrimoineDbContext context) : IInventaireSite
{
    public async Task<LieuInventaire?> LieuAsync(Guid siteId, Guid zoneId, Guid? composantId, bool composantActif, CancellationToken cancellationToken)
    {
        var zone = await (
                from z in context.Zones.AsNoTracking()
                join b in context.Batiments.AsNoTracking() on z.BatimentId equals b.Id
                where z.Id == zoneId && z.SiteId == siteId
                select new { z.Id, z.Nom, Batiment = b.Nom })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (zone is null)
        {
            return null;
        }

        if (composantId is not { } id)
        {
            return new LieuInventaire(zone.Id, zone.Nom, zone.Batiment, null, null, null);
        }

        // Nouveau rattachement : composant actif de cette zone seulement (un composant retiré ne reçoit plus de dossier).
        // Relecture : le composant rattaché reste nommé même retiré ou déplacé depuis ; disparu, la zone reste affichée.
        var composant = await context.Composants.AsNoTracking()
            .Where(c => c.Id == id && c.SiteId == siteId && (!composantActif || (c.ZoneId == zoneId && c.RetireLe == null)))
            .Select(c => new { c.Id, c.Nom, c.CorpsEtat }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (composant is null)
        {
            return composantActif ? null : new LieuInventaire(zone.Id, zone.Nom, zone.Batiment, null, null, null);
        }

        return new LieuInventaire(zone.Id, zone.Nom, zone.Batiment, composant.Id, composant.Nom, composant.CorpsEtat.ToString());
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ZonesAsync(Guid siteId, IReadOnlyCollection<Guid> zoneIds, CancellationToken cancellationToken)
    {
        if (zoneIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await (
                from z in context.Zones.AsNoTracking()
                join b in context.Batiments.AsNoTracking() on z.BatimentId equals b.Id
                where z.SiteId == siteId && zoneIds.Contains(z.Id)
                select new { z.Id, Libelle = b.Nom + " · " + z.Nom })
            .ToDictionaryAsync(x => x.Id, x => x.Libelle, cancellationToken).ConfigureAwait(false);
    }
}

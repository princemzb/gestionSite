using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
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

        return new LieuInventaire(
            zone.Id, zone.Nom, zone.Batiment, composant.Id, composant.Nom, composant.CorpsEtat.ToString(), CorpsEtats.PhotoApresIntervention(composant.CorpsEtat));
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

    public async Task<EtablissementInventaire> EtablissementAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var salaries = await context.Etablissements.AsNoTracking().Where(e => e.SiteId == siteId).Select(e => (bool?)e.SalariesPresents)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        // Tout bâtiment, même sans zone « Parties communes » (créée avec lui) : son classement vaut pour ses composants.
        var batiments = await context.Batiments.AsNoTracking().Where(b => b.SiteId == siteId).OrderBy(b => b.Nom)
            .Select(b => new
            {
                Batiment = b,
                Communes = context.Zones.Where(z => z.BatimentId == b.Id && z.PartiesCommunes).Select(z => (Guid?)z.Id).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new EtablissementInventaire(salaries, [.. batiments.Select(b => new BatimentInventaire(
            b.Batiment.Id, b.Batiment.Nom, Classements.Lire(b.Batiment.Classement), b.Communes))]);
    }

    public async Task<IReadOnlyList<ComposantInventaire>> ComposantsAsync(Guid siteId, IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        var composants = context.Composants.AsNoTracking().Where(c => c.SiteId == siteId);
        composants = ids is null ? composants.Where(c => c.RetireLe == null && c.SoumisAVerification) : composants.Where(c => ids.Contains(c.Id));
        var lus = await (
                from c in composants
                join z in context.Zones.AsNoTracking() on c.ZoneId equals z.Id
                join b in context.Batiments.AsNoTracking() on z.BatimentId equals b.Id
                orderby b.Nom, z.Nom, c.Nom, c.Id
                select new { c.Id, c.Nom, c.ZoneId, Zone = b.Nom + " · " + z.Nom, BatimentId = b.Id, c.CorpsEtat, c.SoumisAVerification, c.Quantite, c.RetireLe })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. lus.Select(c => new ComposantInventaire(
            c.Id, c.Nom, c.ZoneId, c.Zone, c.BatimentId, c.CorpsEtat.ToString(), c.SoumisAVerification, c.Quantite is not null, c.RetireLe is not null))];
    }
}

/// <summary>Classement déclaré lu dans la forme du contrat (types et réponses en noms exacts), pour l'API et les autres modules.</summary>
internal static class Classements
{
    internal static ClassementBatiment? Lire(ClassementErp? c) => c is null
        ? null
        : new ClassementBatiment(
            c.Erp, c.Type?.ToString(), [.. c.TypesSecondaires.Select(t => t.ToString())], c.Categorie, c.Effectif, c.LocauxSommeil, c.MonumentHistorique,
            c.PermisAvant1997.ToString(), c.DerniereVisiteCommission);
}

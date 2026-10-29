using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Score;

internal sealed record CouvertureLue(int Evalues, int Actifs);

internal sealed record ZoneNotee(
    Guid ZoneId, string Nom, Guid BatimentId, string Batiment, string? Niveau, bool PartiesCommunes, decimal? Note, string? Libelle,
    CouvertureLue Couverture, int Anciennes, int AEvaluer, int Critiques, DateTimeOffset? PlusAncienne);

internal sealed record PointLu(string Mois, decimal? Note, string? Libelle);

internal sealed record ScoreLu(
    decimal? Note, string? Libelle, CouvertureLue Couverture, decimal PartAncienne, int Critiques, IReadOnlyList<ZoneNotee> Zones,
    IReadOnlyList<PointLu> Historique);

/// <summary>
/// État du site (US-11, S11-03) : note, couverture, fraîcheur et points critiques, du site et de chaque zone, et courbe
/// mensuelle sur 12 mois, recalculés à la lecture depuis les évaluations datées (aucun instantané stocké). Notes affichées
/// à une décimale, libellé lu sur la valeur arrondie ; zones les moins bien notées d'abord, non évaluées ensuite.
/// </summary>
internal sealed class ServiceScore(PatrimoineDbContext context, IAutorisations autorisations, TimeProvider horloge)
{
    internal async Task<ScoreLu> LireAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var zones = await (
                from z in context.Zones.AsNoTracking()
                join b in context.Batiments.AsNoTracking() on z.BatimentId equals b.Id
                join n in context.Niveaux.AsNoTracking() on z.NiveauId equals n.Id into niveaux
                from n in niveaux.DefaultIfEmpty()
                where z.SiteId == siteId
                select new { z.Id, z.Nom, z.BatimentId, Batiment = b.Nom, Niveau = n == null ? null : n.Nom, z.PartiesCommunes })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var composants = await context.Composants.AsNoTracking().Where(c => c.SiteId == siteId)
            .Select(c => new ComposantSuivi(c.Id, c.ZoneId, c.CreeLe, c.RetireLe))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var evaluations = await context.Evaluations.AsNoTracking().Where(e => e.SiteId == siteId)
            .Select(e => new { e.Id, e.ComposantId, e.EvalueLe, e.Etat, e.Bon, e.Satisfaisant, e.Degrade, e.Mauvais, e.Critique })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var notes = evaluations.Select(e =>
        {
            var note = e.Bon is { } bon
                ? Echelle.Note(new Repartition(bon, e.Satisfaisant ?? 0, e.Degrade ?? 0, e.Mauvais ?? 0, e.Critique ?? 0))
                : (int)e.Etat!.Value;
            return new NoteDatee(e.Id, e.ComposantId, e.EvalueLe, note, e.Etat == EtatComposant.Critique || e.Critique > 0);
        }).ToList();

        var maintenant = horloge.GetUtcNow();
        var identifiants = zones.Select(z => z.Id).ToList();
        var etat = CalculScore.Calculer(identifiants, composants, notes, maintenant);
        var parZone = etat.Zones.ToDictionary(z => z.ZoneId);
        var zonesNotees = zones.Select(z =>
            {
                var e = parZone[z.Id];
                return new ZoneNotee(
                    z.Id, z.Nom, z.BatimentId, z.Batiment, z.Niveau, z.PartiesCommunes, Affichee(e.Note), Libelle(e.Note),
                    new CouvertureLue(e.Evalues, e.Actifs), e.Anciennes, e.Actifs - e.Evalues + e.Anciennes, e.Critiques, e.PlusAncienne);
            })
            .OrderBy(z => z.Note is null).ThenBy(z => parZone[z.ZoneId].Note).ThenBy(z => z.Batiment).ThenBy(z => z.Nom).ToList();
        return new ScoreLu(
            Affichee(etat.Note), Libelle(etat.Note), new CouvertureLue(etat.Evalues, etat.Actifs), Math.Round(etat.PartAncienne, 4), etat.Critiques,
            zonesNotees,
            [.. CalculScore.Historique(identifiants, composants, notes, maintenant)
                .Select(p => new PointLu(p.Mois.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Affichee(p.Note), Libelle(p.Note)))]);
    }

    private static decimal? Affichee(decimal? note) => note is { } n ? Echelle.Arrondie(n) : null;

    private static string? Libelle(decimal? note) => note is { } n ? Echelle.Libelle(n).ToString() : null;
}

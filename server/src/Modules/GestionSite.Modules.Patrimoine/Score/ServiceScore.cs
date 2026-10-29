using System.Globalization;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Score;

internal sealed record CouvertureLue(int Evalues, int Actifs);

internal sealed record PointLu(string Mois, decimal? Note, string? Libelle);

internal sealed record ZoneNotee(
    Guid ZoneId, string Nom, Guid BatimentId, string Batiment, string? Niveau, bool PartiesCommunes, decimal? Note, string? Libelle,
    CouvertureLue Couverture, int Anciennes, int AEvaluer, int Critiques, DateTimeOffset? PlusAncienne, IReadOnlyList<PointLu> Historique);

internal sealed record ScoreLu(
    decimal? Note, string? Libelle, CouvertureLue Couverture, decimal PartAncienne, int Critiques, IReadOnlyList<ZoneNotee> Zones,
    IReadOnlyList<PointLu> Historique);

/// <summary>
/// État du site (US-11, S11-03) : note, couverture, fraîcheur et points critiques, du site et de chaque zone, et courbes
/// mensuelles sur 12 mois, recalculés à la lecture depuis les évaluations datées (aucun instantané stocké). Notes affichées
/// à une décimale, libellé lu sur la valeur arrondie ; zones les moins bien notées d'abord, non notées ensuite.
/// </summary>
internal sealed class ServiceScore(PatrimoineDbContext context, IAutorisations autorisations, TimeProvider horloge)
{
    internal async Task<ScoreLu> LireAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var zones = await (
                from z in context.Zones.AsNoTracking()
                join b in context.Batiments.AsNoTracking() on z.BatimentId equals b.Id
                join n in context.Niveaux.AsNoTracking() on z.NiveauId equals n.Id into niveaux
                from n in niveaux.DefaultIfEmpty()
                where z.SiteId == siteId
                orderby b.Nom, z.Nom, z.Id
                select new { z.Id, z.Nom, z.BatimentId, Batiment = b.Nom, Niveau = n == null ? null : n.Nom, z.PartiesCommunes })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var composants = await context.Composants.AsNoTracking().Where(c => c.SiteId == siteId)
            .Select(c => new ComposantSuivi(c.Id, c.ZoneId, c.CreeLe, c.RetireLe))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Évaluations utiles seulement : celles de la fenêtre de 12 mois, et la dernière de chaque composant avant elle.
        var debut = CalculScore.DebutHistorique(maintenant);
        var evaluations = await context.Evaluations.AsNoTracking()
            .Where(e => e.SiteId == siteId && (e.EvalueLe >= debut
                || !context.Evaluations.Any(suivante => suivante.ComposantId == e.ComposantId && suivante.EvalueLe < debut && suivante.EvalueLe > e.EvalueLe)))
            .Select(e => new { e.Id, e.ComposantId, e.EvalueLe, e.Etat, e.Bon, e.Satisfaisant, e.Degrade, e.Mauvais, e.Critique })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var notes = evaluations.Select(e =>
        {
            var repartition = e.Bon is { } bon ? new Repartition(bon, e.Satisfaisant ?? 0, e.Degrade ?? 0, e.Mauvais ?? 0, e.Critique ?? 0) : null;
            return new NoteDatee(e.Id, e.ComposantId, e.EvalueLe, Echelle.Note(e.Etat, repartition), Echelle.Critique(e.Etat, repartition));
        });

        var historique = new CalculScore([.. zones.Select(z => z.Id)], composants, notes).Historique(maintenant);
        var etat = historique[^1].Etat;
        var parZone = etat.Zones.ToDictionary(z => z.ZoneId);
        var historiqueParZone = historique
            .SelectMany(p => p.Etat.Zones.Select(z => (z.ZoneId, Point: Point(p.Mois, z.Note))))
            .ToLookup(p => p.ZoneId, p => p.Point);
        var zonesNotees = zones.Select(z =>
            {
                var e = parZone[z.Id];
                return new ZoneNotee(
                    z.Id, z.Nom, z.BatimentId, z.Batiment, z.Niveau, z.PartiesCommunes, Affichee(e.Note), Libelle(e.Note),
                    new CouvertureLue(e.Evalues, e.Actifs), e.Anciennes, e.Actifs - e.Evalues + e.Anciennes, e.Critiques, e.PlusAncienne,
                    [.. historiqueParZone[z.Id]]);
            })
            .OrderBy(z => parZone[z.ZoneId].Note is null).ThenBy(z => parZone[z.ZoneId].Note) // tri stable : bâtiment puis nom à égalité
            .ToList();
        return new ScoreLu(
            Affichee(etat.Note), Libelle(etat.Note), new CouvertureLue(etat.Evalues, etat.Actifs), Math.Round(etat.PartAncienne.Approche(), 4),
            etat.Critiques, zonesNotees, [.. historique.Select(p => Point(p.Mois, p.Etat.Note))]);
    }

    private static PointLu Point(DateOnly mois, Rationnel? note) =>
        new(mois.ToString("yyyy-MM", CultureInfo.InvariantCulture), Affichee(note), Libelle(note));

    private static decimal? Affichee(Rationnel? note) => note is { } n ? Echelle.Arrondie(n) : null;

    private static string? Libelle(Rationnel? note) => note is { } n ? Echelle.Libelle(n).ToString() : null;
}

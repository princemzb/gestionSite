using System.Globalization;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Preventif;

/// <summary>
/// Non-conformités d'un contrôle vers les dossiers (US-12, S12-05, RG-09, PROCESSUS « Inspection ») : chaque point de
/// grille en défaut et chaque observation d'un rapport ouvre un dossier « À qualifier » (origine Inspection, zone et
/// composant fixés, commande dérivée du contrôle), sauf si un dossier ouvert existe pour le même composant (ou la même
/// zone sans composant) et le même critère : le constat y est alors rattaché et tracé (« ConstatInspection »). Un danger
/// enregistre toujours une alerte, rattaché ou non (T-03.20). Écrit dans la même transaction que le contrôle.
/// </summary>
internal static class Constats
{
    private sealed record Releve(int Rang, string Libelle, string Cle, Guid ZoneId, bool Danger);

    /// <summary>
    /// Occurrences dont les réserves sont levées : au moins un constat, et tous leurs dossiers clos (clôturés, classés sans
    /// suite ou rattachés à un doublon), selon le cycle d'US-09. Calculé à la lecture : une réouverture les remet en réserve.
    /// </summary>
    internal static async Task<HashSet<Guid>> LeveesAsync(MaintenanceDbContext context, IReadOnlyCollection<Guid> occurrenceIds, CancellationToken cancellationToken)
    {
        if (occurrenceIds.Count == 0)
        {
            return [];
        }

        var liens = await (
                from c in context.Constats.AsNoTracking()
                join s in context.Signalements.AsNoTracking() on c.DossierId equals s.Id
                where occurrenceIds.Contains(c.OccurrenceId)
                select new { c.OccurrenceId, Ouvert = Signalement.StatutsOuverts.Contains(s.Statut) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. liens.GroupBy(l => l.OccurrenceId).Where(g => !g.Any(l => l.Ouvert)).Select(g => g.Key)];
    }

    internal static async Task OuvrirAsync(
        MaintenanceDbContext context, IInventaireSite inventaire, IAnnuaireMembres annuaire, PlanPreventif plan, ControlePreventif controle, Guid auteur,
        DateTimeOffset le, CancellationToken cancellationToken)
    {
        var releves = controle.PointsLus
            .Select((p, i) => (Point: p, Rang: i + 1))
            .Where(p => p.Point.Conforme == false)
            .Select(p => new Releve(p.Rang, p.Point.Texte, CleConstat.Point(controle.CorpsEtat, p.Point.Texte), controle.ZoneId, p.Point.Danger))
            .Concat(controle.ObservationsLues.Select((o, i) => new Releve(
                ConstatControle.RangObservations + i + 1, o.Libelle, CleConstat.Observation(o.Libelle), o.ZoneId, o.Danger)))
            .ToList();
        if (releves.Count == 0)
        {
            return;
        }

        var zones = await inventaire.ZonesAsync(controle.SiteId, [.. releves.Select(r => r.ZoneId).Distinct()], cancellationToken).ConfigureAwait(false);
        var nom = (await annuaire.NomsAsync([auteur], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(auteur) is { Length: > 0 } n ? n : "Compte interne";
        var cles = releves.Select(r => r.Cle).Distinct().ToList();
        var composant = controle.ComposantId;
        var ouverts = await context.Signalements
            .Where(s => s.SiteId == controle.SiteId && s.CleConstat != null && cles.Contains(s.CleConstat) && s.ComposantId == composant
                && Signalement.StatutsOuverts.Contains(s.Statut))
            .OrderBy(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        // Même critère : sur le composant, ou sur la zone quand le contrôle ne vise pas un composant.
        var parCritere = new Dictionary<(string Cle, Guid Zone), Signalement>();
        foreach (var dossier in ouverts)
        {
            parCritere.TryAdd((dossier.CleConstat!, composant is null ? dossier.ZoneId ?? Guid.Empty : Guid.Empty), dossier);
        }

        foreach (var releve in releves)
        {
            var critere = (releve.Cle, composant is null ? releve.ZoneId : Guid.Empty);
            var rattache = parCritere.TryGetValue(critere, out var dossier);
            if (!rattache)
            {
                var description = string.Create(CultureInfo.InvariantCulture,
                    $"{plan.Libelle} : {releve.Libelle}\nConstat du contrôle réalisé le {controle.RealiseLe:dd/MM/yyyy}.");
                dossier = Signalement.Constat(
                    Guid.CreateVersion7(le), controle.OrganisationId, controle.SiteId, Commandes.Derivee(controle.CommandeId, $"constat:{releve.Rang}"),
                    description, zones.GetValueOrDefault(releve.ZoneId, "Zone"), releve.ZoneId, composant, releve.Danger, auteur, nom, releve.Cle, controle.Id,
                    controle.OccurrenceId, le);
                context.Signalements.Add(dossier);
                parCritere[critere] = dossier;
            }

            var constat = new ConstatControle(Guid.CreateVersion7(le), controle, releve.Rang, releve.Libelle, releve.Cle, dossier!, rattache, releve.Danger, le);
            context.Constats.Add(constat);
            context.Tracer(dossier!, rattache ? NatureEvenementDossier.ConstatInspection : NatureEvenementDossier.Declaration, auteur, le, new
            {
                controleId = controle.Id,
                occurrenceId = controle.OccurrenceId,
                planId = plan.Id,
                plan = plan.Libelle,
                constat = releve.Libelle,
                danger = releve.Danger,
            });
            if (releve.Danger)
            {
                context.AlertesDanger.Add(new AlerteDanger(Guid.CreateVersion7(le), dossier!, constat.Id, le));
            }
        }
    }
}

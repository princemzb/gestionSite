using System.Globalization;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Preventif;

/// <summary>Libellés utiles à l'ouverture des dossiers, lus avant le verrou de l'échéance (zones, nom du déclarant).</summary>
internal sealed record LibellesConstats(IReadOnlyDictionary<Guid, string> Zones, string Declarant);

/// <summary>
/// Non-conformités d'un contrôle vers les dossiers (US-12, S12-05, RG-09, PROCESSUS « Inspection ») : chaque point de
/// grille en défaut et chaque observation d'un rapport ouvre un dossier « À qualifier » (origine Inspection, zone et
/// composant fixés, commande dérivée du contrôle), sauf si un dossier ouvert existe pour le même composant (ou la même
/// zone sans composant) et le même critère : le constat y est alors rattaché et tracé (« ConstatInspection »), et le
/// dossier marqué en danger si le constat l'est. Un danger enregistre toujours une alerte, rattaché ou non (T-03.20).
/// Écrit dans la même transaction que le contrôle.
/// </summary>
internal static class Constats
{
    private sealed record Releve(int Rang, string Libelle, string Cle, Guid ZoneId, Guid? ComposantId, bool Danger);

    /// <summary>Zones des constats et nom du déclarant, lus hors transaction (autres modules).</summary>
    internal static async Task<LibellesConstats> LibellesAsync(
        IInventaireSite inventaire, IAnnuaireMembres annuaire, ControlePreventif controle, Guid auteur, CancellationToken cancellationToken)
    {
        var zones = await inventaire.ZonesAsync(
            controle.SiteId, [.. controle.ObservationsLues.Select(o => o.ZoneId).Append(controle.ZoneId).Distinct()], cancellationToken).ConfigureAwait(false);
        var nom = (await annuaire.NomsAsync([auteur], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(auteur) is { Length: > 0 } n ? n : "Compte interne";
        return new LibellesConstats(zones, nom);
    }

    /// <summary>
    /// Occurrences dont les réserves sont levées : au moins un constat, et tous leurs dossiers clos (clôturés, classés sans
    /// suite) ; un dossier rattaché à un doublon suit son dossier principal. Calculé à la lecture : une réouverture les
    /// remet en réserve.
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
                join p in context.Signalements.AsNoTracking() on s.DoublonDe equals p.Id into principaux
                from p in principaux.DefaultIfEmpty()
                where occurrenceIds.Contains(c.OccurrenceId)
                select new { c.OccurrenceId, Statut = p == null ? s.Statut : p.Statut })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. liens.GroupBy(l => l.OccurrenceId).Where(g => !g.Any(l => Signalement.StatutsOuverts.Contains(l.Statut))).Select(g => g.Key)];
    }

    internal static async Task OuvrirAsync(
        MaintenanceDbContext context, PlanPreventif plan, ControlePreventif controle, LibellesConstats libelles, Guid auteur, DateTimeOffset le,
        CancellationToken cancellationToken)
    {
        // Observation hors de la zone du composant visé : dossier de zone, sans composant (le composant n'y est pas).
        var releves = controle.PointsLus
            .Select((p, i) => (Point: p, Rang: i + 1))
            .Where(p => p.Point.Conforme == false)
            .Select(p => new Releve(
                p.Rang, p.Point.Texte, CleConstat.Point(controle.CorpsEtat, p.Point.Texte), controle.ZoneId, controle.ComposantId, p.Point.Danger))
            .Concat(controle.ObservationsLues.Select((o, i) => new Releve(
                ConstatControle.RangObservations + i + 1, o.Libelle, CleConstat.Observation(o.Libelle), o.ZoneId,
                o.ZoneId == controle.ZoneId ? controle.ComposantId : null, o.Danger)))
            .ToList();
        if (releves.Count == 0)
        {
            return;
        }

        // Critères verrouillés (ordre fixe, durée de la transaction) : deux contrôles simultanés, même de plans différents,
        // ne créent pas deux dossiers pour le même écart ; dossiers candidats verrouillés puis relus ouverts.
        var cles = releves.Select(r => r.Cle).Distinct().Order(StringComparer.Ordinal).ToList();
        foreach (var cle in cles)
        {
            await context.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({$"constat:{controle.SiteId:N}:{cle}"}, 0))", cancellationToken).ConfigureAwait(false);
        }

        var candidats = await context.Signalements.AsNoTracking()
            .Where(s => s.SiteId == controle.SiteId && s.CleConstat != null && cles.Contains(s.CleConstat) && Signalement.StatutsOuverts.Contains(s.Statut))
            .Select(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        await context.VerrouillerAsync([.. candidats], cancellationToken).ConfigureAwait(false);
        var ouverts = await context.Signalements
            .Where(s => candidats.Contains(s.Id) && Signalement.StatutsOuverts.Contains(s.Statut))
            .OrderBy(s => s.RecuLe).ThenBy(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        // Même critère : sur le composant, ou sur la zone quand le constat ne vise pas un composant.
        var parCritere = new Dictionary<(string Cle, Guid? Composant, Guid Zone), Signalement>();
        foreach (var dossier in ouverts)
        {
            parCritere.TryAdd(Critere(dossier.CleConstat!, dossier.ComposantId, dossier.ZoneId ?? Guid.Empty), dossier);
        }

        foreach (var releve in releves)
        {
            var critere = Critere(releve.Cle, releve.ComposantId, releve.ZoneId);
            var rattache = parCritere.TryGetValue(critere, out var dossier);
            if (!rattache)
            {
                var description = string.Create(CultureInfo.InvariantCulture,
                    $"{plan.Libelle} : {releve.Libelle}\nConstat du contrôle réalisé le {controle.RealiseLe:dd/MM/yyyy}.");
                dossier = Signalement.Constat(
                    Guid.CreateVersion7(le), controle.OrganisationId, controle.SiteId, Commandes.Derivee(controle.CommandeId, $"constat:{releve.Rang}"),
                    description, libelles.Zones.GetValueOrDefault(releve.ZoneId, "Zone"), releve.ZoneId, releve.ComposantId, releve.Danger, auteur,
                    libelles.Declarant, releve.Cle, controle.Id, controle.OccurrenceId, le);
                context.Signalements.Add(dossier);
                parCritere[critere] = dossier;
            }
            else if (releve.Danger)
            {
                dossier!.SignalerDanger(); // le danger relevé se voit sur le dossier (liste et fiche)
            }

            var constat = new ConstatControle(Guid.CreateVersion7(le), controle, releve.Rang, releve.Libelle, releve.Cle, dossier!, rattache, releve.Danger, le);
            context.Constats.Add(constat);
            // Un même écart relevé deux fois par ce contrôle ne marque pas « nouveau constat » le dossier qu'il vient d'ouvrir.
            if (!rattache || dossier!.ControleId != controle.Id)
            {
                context.Tracer(dossier!, rattache ? NatureEvenementDossier.ConstatInspection : NatureEvenementDossier.Declaration, auteur, le, new
                {
                    controleId = controle.Id,
                    occurrenceId = controle.OccurrenceId,
                    planId = plan.Id,
                    plan = plan.Libelle,
                    constat = releve.Libelle,
                    danger = releve.Danger,
                });
            }

            if (releve.Danger)
            {
                context.AlertesDanger.Add(new AlerteDanger(Guid.CreateVersion7(le), dossier!, constat.Id, le));
            }
        }
    }

    private static (string, Guid?, Guid) Critere(string cle, Guid? composant, Guid zone) => (cle, composant, composant is null ? zone : Guid.Empty);
}

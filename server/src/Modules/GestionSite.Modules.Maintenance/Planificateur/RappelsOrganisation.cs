using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Notifications;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Planificateur;

/// <summary>Bilan du passage dans une organisation : notifications écrites et prochain seuil à venir.</summary>
internal sealed record BilanOrganisation(int Notifications, DateTimeOffset? Prochain);

/// <summary>
/// Passage du planificateur dans <b>une</b> organisation, celle de la portée (définie avant tout accès à la base) : toutes
/// les lectures et écritures passent par la RLS de cette organisation, comme une requête ordinaire, sans compte (aucun
/// auteur : personne n'est exclu des destinataires). Un verrou transactionnel par organisation écarte un second passage
/// simultané (alarme et déclencheur de secours) ; les notifications sont écrites avec « ON CONFLICT DO NOTHING » sur la
/// clé d'unicité (organisation, destinataire, clé) : un seuil déjà notifié n'écrit rien, même rejoué.
/// </summary>
internal sealed class RappelsOrganisation(MaintenanceDbContext context, IAnnuaireMembres annuaire)
{
    /// <summary>Nouveau passage quand l'organisation était déjà en cours de traitement (réponse de l'autre passage peut-être perdue).</summary>
    internal static readonly TimeSpan PassageConcurrent = TimeSpan.FromMinutes(1);

    internal async Task<BilanOrganisation> TraiterAsync(DateTimeOffset maintenant, CancellationToken cancellationToken)
    {
        var organisation = context.OrganisationRequise;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var cleVerrou = $"planificateur:{organisation:N}";
        var verrou = await context.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({cleVerrou}, 0)) AS \"Value\"")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!verrou[0])
        {
            // Un autre passage traite cette organisation en ce moment : revenir sous peu (sa réponse peut se perdre).
            return new BilanOrganisation(0, maintenant + PassageConcurrent);
        }

        var dossiers = await context.Signalements.AsNoTracking().Where(s => Signalement.StatutsOuverts.Contains(s.Statut))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var actives = (await context.Interventions.AsNoTracking()
                .Where(i => i.Statut != StatutIntervention.Cloturee
                    && context.Signalements.Any(s => s.Id == i.DossierId && Signalement.StatutsOuverts.Contains(s.Statut)))
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToLookup(i => i.DossierId);

        var echeances = dossiers.Select(dossier => Rappels.Calculer(dossier, [.. actives[dossier.Id]], maintenant)).ToList();

        // Dangers en cours (S10-04), y compris sur un dossier clos : la clôture du dossier ne lève pas le danger.
        var alertes = await context.AlertesDanger.AsNoTracking().Where(a => a.Fin == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        var parId = dossiers.ToDictionary(d => d.Id);
        var clos = alertes.Select(a => a.SignalementId).Where(id => !parId.ContainsKey(id)).Distinct().ToList();
        if (clos.Count > 0)
        {
            foreach (var dossier in await context.Signalements.AsNoTracking().Where(s => clos.Contains(s.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                parId[dossier.Id] = dossier;
            }
        }

        echeances.AddRange(alertes.Where(a => parId.ContainsKey(a.SignalementId))
            .Select(alerte => Rappels.Danger(alerte, parId[alerte.SignalementId], [.. actives[alerte.SignalementId]], maintenant)));

        // Prochain seuil à venir : le plus proche de tous (Min ignore les « aucun ») ; null s'il n'y en a aucun.
        var prochain = echeances.Min(e => e.Prochain);
        var dus = echeances.SelectMany(e => e.Dus).ToList();
        var lignes = new List<Ligne>();
        foreach (var parSite in dus.GroupBy(r => r.Dossier.SiteId))
        {
            // Destinataires lus au moment de l'envoi : membres actuels du site seulement (révoqués et sans rôle exclus).
            var site = await DestinatairesSite.LireAsync(annuaire, parSite.Key, cancellationToken).ConfigureAwait(false);
            foreach (var rappel in parSite)
            {
                lignes.AddRange(Destinataires(rappel.Cible, site).Select(compte => new Ligne(parSite.Key, compte, rappel.Type, rappel.Dossier.Id, rappel.Cle, rappel.AlerteId)));
            }
        }

        var ecrites = lignes.Count == 0 ? 0 : await EcrireAsync(organisation, lignes, maintenant, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BilanOrganisation(ecrites, prochain);
    }

    /// <summary>
    /// R/D si demandés, et personnes en charge encore membres du site ; une personne en charge qui ne l'est plus est
    /// remplacée par les R/D (l'intervention n'a plus de répondant : ils doivent le savoir).
    /// </summary>
    internal static IEnumerable<Guid> Destinataires(CibleRappel cible, DestinatairesSite site)
    {
        if (cible.Information)
        {
            // Alerte de danger (Q3-A) : R/D, P/C, et personnes en charge encore membres du site.
            return site.AlerteDanger(cible.EnCharge).Distinct();
        }

        var enCharge = (cible.EnChargeResponsable ? site.ResponsablesDelegues().Intersect(cible.EnCharge) : site.Membres(cible.EnCharge)).ToList();
        var encadrement = cible.Encadrement || enCharge.Count == 0;
        return (encadrement ? site.ResponsablesDelegues() : []).Concat(enCharge).Distinct();
    }

    // Une instruction par destinataire : « ON CONFLICT » soumet la ligne écrite à la politique de lecture du centre (le
    // destinataire seul) ; le compte de la transaction est donc celui du destinataire le temps de son instruction, puis
    // effacé. Un seuil déjà notifié (même clé pour ce compte) n'écrit rien.
    private async Task<int> EcrireAsync(
        Guid organisation, List<Ligne> lignes, DateTimeOffset maintenant,
        CancellationToken cancellationToken)
    {
        var objet = nameof(ObjetNotification.Dossier);
        var ecrites = 0;
        foreach (var parCompte in lignes.GroupBy(l => l.Compte))
        {
            var compte = parCompte.Key.ToString("D");
            var ids = parCompte.Select(_ => Guid.CreateVersion7()).ToArray();
            var sites = parCompte.Select(l => l.Site).ToArray();
            var types = parCompte.Select(l => l.Type.ToString()).ToArray();
            var objets = parCompte.Select(l => l.Dossier).ToArray();
            var cles = parCompte.Select(l => l.Cle).ToArray();
            var alertes = parCompte.Select(l => l.Alerte).ToArray();
            await context.Database.ExecuteSqlAsync(
                $"SELECT set_config({CompteCourant.VariableSession}, {compte}, true)", cancellationToken).ConfigureAwait(false);
            ecrites += await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO maintenance.notifications (id, organisation_id, site_id, compte_id, type, objet, objet_id, cle, cree_le, alerte_id)
                SELECT u.id, {organisation}, u.site_id, {parCompte.Key}, u.type, {objet}, u.objet_id, u.cle, {maintenant}, u.alerte_id
                FROM unnest({ids}, {sites}, {types}, {objets}, {cles}, {alertes}) AS u(id, site_id, type, objet_id, cle, alerte_id)
                ON CONFLICT (organisation_id, compte_id, cle) DO NOTHING
                """, cancellationToken).ConfigureAwait(false);
        }

        await context.Database.ExecuteSqlAsync($"SELECT set_config({CompteCourant.VariableSession}, '', true)", cancellationToken).ConfigureAwait(false);
        return ecrites;
    }

    /// <summary>Notification à écrire : site, destinataire, type, dossier, clé du seuil et alerte de danger éventuelle.</summary>
    private sealed record Ligne(Guid Site, Guid Compte, TypeNotification Type, Guid Dossier, string Cle, Guid? Alerte);
}

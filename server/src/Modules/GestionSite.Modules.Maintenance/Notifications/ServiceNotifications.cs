using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Notifications;

/// <summary>Objet visé : son titre est relu à l'affichage (null si l'objet n'est plus lisible).</summary>
internal sealed record ObjetNotificationLu(string Type, Guid Id, string? Titre);

/// <summary>
/// Notification du centre ; <c>Epinglee</c> : alerte de danger dont le danger n'est encore ni pris en compte, ni levé ou écarté
/// (Q-M8 : elle reste en vue même lue) ; <c>AlerteId</c> : danger concerné (S10-04), null sinon.
/// </summary>
internal sealed record NotificationLue(
    Guid Id, string Type, Guid SiteId, ObjetNotificationLu Objet, DateTimeOffset CreeLe, DateTimeOffset? LueLe, bool Epinglee = false, Guid? AlerteId = null);

/// <summary>
/// Page du centre, du plus récent au plus ancien, nombre total de notifications non lues (pastille de la cloche) et alertes
/// épinglées (Q-M8 : la plus récente alerte de chaque danger non pris en compte, lue ou non, quelle que soit la page).
/// </summary>
internal sealed record PageNotifications(IReadOnlyList<NotificationLue> Notifications, int NonLues, IReadOnlyList<NotificationLue>? Epinglees = null);

/// <summary>
/// Centre de notifications de l'appelant dans une organisation (US-10, S10-01) : il ne lit et ne marque que les siennes,
/// et seulement sur les sites où il a encore un rôle au moment de la lecture (NFR-01) ; hors de l'organisation, 404.
/// </summary>
internal sealed class ServiceNotifications(MaintenanceDbContext context, IAutorisations autorisations, CompteCourant compte, TimeProvider horloge)
{
    internal const int LimiteParDefaut = 50;
    internal const int LimiteMax = 100;
    private const int LimiteAlertes = 1000;

    internal async Task<PageNotifications> ListerAsync(
        Guid organisationId, bool nonLuesSeulement, Guid? avant, int? limite, CancellationToken cancellationToken)
    {
        if (limite is < 1 or > LimiteMax)
        {
            throw new SaisieInvalideException("limite", $"Limite de 1 à {LimiteMax}.");
        }

        var miennes = await MiennesAsync(organisationId, cancellationToken).ConfigureAwait(false);
        var toutes = miennes;
        var nonLues = await miennes.CountAsync(n => n.LueLe == null, cancellationToken).ConfigureAwait(false);
        if (nonLuesSeulement)
        {
            miennes = miennes.Where(n => n.LueLe == null);
        }

        if (avant is { } curseur)
        {
            miennes = miennes.Where(n => n.Id.CompareTo(curseur) < 0);
        }

        var page = await miennes.OrderByDescending(n => n.Id).Take(limite ?? LimiteParDefaut).ToListAsync(cancellationToken).ConfigureAwait(false);
        // Alertes de danger épinglées (Q-M8) : la plus récente de chaque danger encore ni pris en compte, ni levé ou écarté.
        var nonPrisEnCompte = context.AlertesDanger.Where(a => a.PrisEnCompteLe == null && a.Fin == null).Select(a => (Guid?)a.Id);
        var alertesMiennes = toutes.Where(n => n.AlerteId != null && Notification.TypesAlerte.Contains(n.Type) && nonPrisEnCompte.Contains(n.AlerteId));
        // Peu de lignes (une alerte par heure et par danger en cours) : la plus récente par danger est retenue en mémoire.
        var epinglees = (await alertesMiennes.OrderByDescending(n => n.Id).Take(LimiteAlertes).ToListAsync(cancellationToken).ConfigureAwait(false))
            .DistinctBy(n => n.AlerteId).Take(LimiteMax).ToList();
        var ouvertes = epinglees.Select(n => n.AlerteId).ToHashSet();
        var aEpingler = page.Where(n => n.AlerteId is not null && Notification.TypesAlerte.Contains(n.Type)).Select(n => n.AlerteId).Except(ouvertes).ToList();
        if (aEpingler.Count > 0)
        {
            // Alertes plus anciennes du même danger sur la page : épinglées elles aussi tant qu'il n'est pas pris en compte.
            ouvertes.UnionWith(await nonPrisEnCompte.Where(a => aEpingler.Contains(a)).ToListAsync(cancellationToken).ConfigureAwait(false));
        }

        var ids = page.Concat(epinglees).Where(n => n.Objet == ObjetNotification.Dossier).Select(n => n.ObjetId).Distinct().ToList();
        // Dossiers des sites encore autorisés seulement (les notifications l'ont été) : titre de la fiche, comme la liste des dossiers.
        // Description seule (ni nom ni contact du déclarant), réduite au titre.
        var titres = ids.Count == 0
            ? []
            : (await context.Signalements.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Description })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(s => s.Id, s => ServiceDossiers.Titre(s.Description));
        NotificationLue Lire(Notification n) => new(
            n.Id, n.Type.ToString(), n.SiteId, new ObjetNotificationLu(n.Objet.ToString(), n.ObjetId, titres.GetValueOrDefault(n.ObjetId)), n.CreeLe,
            n.LueLe, Notification.TypesAlerte.Contains(n.Type) && ouvertes.Contains(n.AlerteId), n.AlerteId);
        return new PageNotifications([.. page.Select(Lire)], nonLues, [.. epinglees.Select(Lire)]);
    }

    /// <summary>Marque lue une notification de l'appelant (déjà lue : rien ne change) ; sinon introuvable (404).</summary>
    internal async Task MarquerLueAsync(Guid organisationId, Guid id, CancellationToken cancellationToken)
    {
        var notification = await (await MiennesAsync(organisationId, cancellationToken).ConfigureAwait(false)).AsTracking()
            .SingleOrDefaultAsync(n => n.Id == id, cancellationToken).ConfigureAwait(false) ?? throw new NotificationIntrouvableException();
        if (notification.MarquerLue(horloge.GetUtcNow()))
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Marque lues toutes les notifications visibles de l'appelant, jusqu'à <paramref name="jusqua"/> compris s'il est donné
    /// (dernière reçue à l'écran) : une alerte arrivée entre-temps reste non lue.
    /// </summary>
    internal async Task<int> MarquerToutLuAsync(Guid organisationId, Guid? jusqua, CancellationToken cancellationToken)
    {
        var miennes = (await MiennesAsync(organisationId, cancellationToken).ConfigureAwait(false)).Where(n => n.LueLe == null);
        if (jusqua is { } borne)
        {
            miennes = miennes.Where(n => n.Id.CompareTo(borne) <= 0);
        }

        var maintenant = horloge.GetUtcNow();
        return await miennes.ExecuteUpdateAsync(n => n.SetProperty(x => x.LueLe, maintenant), cancellationToken).ConfigureAwait(false);
    }

    // Notifications de l'appelant sur ses sites actuels ; l'organisation devient courante (filtres et RLS) si l'appelant en est membre actif.
    private async Task<IQueryable<Notification>> MiennesAsync(Guid organisationId, CancellationToken cancellationToken)
    {
        var sites = (await autorisations.SitesAutorisesAsync(organisationId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false)).ToList();
        if (sites.Count == 0
            && await autorisations.VerifierAsync(organisationId, Droit.GererComptes, siteId: null, cancellationToken).ConfigureAwait(false)
                == DecisionAutorisation.Introuvable)
        {
            throw new AccesRefuseException(siteVisible: false); // pas membre actif de cette organisation : 404
        }

        var moi = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        return context.Notifications.AsNoTracking().Where(n => n.CompteId == moi && sites.Contains(n.SiteId));
    }
}

/// <summary>Notification absente, d'un autre compte ou d'un site où l'appelant n'a plus de rôle (404).</summary>
internal sealed class NotificationIntrouvableException() : Exception("Notification introuvable.");

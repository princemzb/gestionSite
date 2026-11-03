using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;

namespace GestionSite.Modules.Maintenance.Notifications;

/// <summary>
/// Membres internes actifs d'un site et leurs rôles, lus au moment de l'émission (avant la transaction de l'événement,
/// comme les autres lectures d'un autre module) : un membre révoqué ou sans rôle sur le site ne reçoit rien.
/// </summary>
internal sealed class DestinatairesSite
{
    private static readonly string[] Encadrement = ["Responsable", "Delegue"];
    private static readonly string[] Information = ["Pasteur", "Coordination"];
    private readonly IReadOnlyList<MembreSite> _membres;

    internal DestinatairesSite(IReadOnlyList<MembreSite> membres) => _membres = membres;

    /// <summary>Aucun destinataire : rien à notifier, aucune lecture faite.</summary>
    internal static DestinatairesSite Aucun { get; } = new([]);

    /// <summary>Membres du site lus seulement s'il y a quelque chose à notifier.</summary>
    internal static async Task<DestinatairesSite> LireSiAsync(bool aNotifier, IAnnuaireMembres annuaire, Guid siteId, CancellationToken cancellationToken) =>
        aNotifier ? await LireAsync(annuaire, siteId, cancellationToken).ConfigureAwait(false) : Aucun;

    internal static async Task<DestinatairesSite> LireAsync(IAnnuaireMembres annuaire, Guid siteId, CancellationToken cancellationToken) =>
        new(await annuaire.MembresAyantDroitAsync(siteId, Droit.ConsulterSite, compteIds: null, cancellationToken).ConfigureAwait(false));

    /// <summary>Responsable et délégués du site (pour agir).</summary>
    internal IEnumerable<Guid> ResponsablesDelegues() =>
        _membres.Where(m => m.Roles.Any(Encadrement.Contains)).Select(m => m.CompteId);

    /// <summary>
    /// Alerte de danger (Q3-A, décision du 02/11) : R/D du site pour agir, pasteurs et coordinateurs pour information, et
    /// les personnes en charge d'une intervention ouverte du dossier, si elles sont encore membres du site.
    /// </summary>
    internal IEnumerable<Guid> AlerteDanger(IReadOnlyCollection<Guid> enCharge) =>
        _membres.Where(m => m.Roles.Any(r => Encadrement.Contains(r) || Information.Contains(r)) || enCharge.Contains(m.CompteId))
            .Select(m => m.CompteId);
}

/// <summary>
/// Émission dans le centre (S10-01, ADR-06 précisée) : les notifications sont ajoutées au contexte de l'événement métier
/// et enregistrées dans sa transaction ; elles n'existent que si l'événement est enregistré. L'auteur d'une action n'est
/// jamais notifié de sa propre action ; un destinataire reçoit une seule notification par événement (clé d'unicité).
/// </summary>
internal static class EmissionNotifications
{
    internal static void Emettre(
        this MaintenanceDbContext context, TypeNotification type, Signalement dossier, string cle, IEnumerable<Guid> destinataires, Guid? auteur,
        DateTimeOffset le)
    {
        foreach (var compte in destinataires.Where(c => c != auteur).Distinct())
        {
            // Identifiant pris à l'écriture, juste avant l'enregistrement (et non à l'instant métier, lu en début de requête,
            // avant photos ou verrous) : l'ordre des identifiants suit l'arrivée dans le centre, base des curseurs « avant »
            // et « jusqua ». CreeLe garde l'instant exact de l'événement.
            context.Notifications.Add(new Notification(Guid.CreateVersion7(), dossier, compte, type, cle, le));
        }
    }

    /// <summary>Destinataires à lire pour une déclaration : seulement s'il y a danger ou activité bloquée.</summary>
    internal static bool ANotifier(Signalement dossier) => dossier.Danger || dossier.ActiviteBloquee;

    /// <summary>
    /// Nouveau dossier déclaré (dépôt public, déclaration interne) : alerte de danger enregistrée (RG-05) et notifiée (Q3-A),
    /// sinon activité bloquée notifiée aux R/D. Aucune intervention n'existe encore : seuls R/D et P/C reçoivent l'alerte.
    /// </summary>
    internal static void Declarer(
        this MaintenanceDbContext context, Signalement dossier, DestinatairesSite destinataires, Guid? auteur, DateTimeOffset le)
    {
        if (dossier.Danger)
        {
            var alerte = new AlerteDanger(Guid.CreateVersion7(le), dossier);
            context.AlertesDanger.Add(alerte);
            context.Emettre(TypeNotification.DangerSignale, dossier, CleDanger(alerte), destinataires.AlerteDanger([]), auteur, le);
        }
        else if (dossier.ActiviteBloquee)
        {
            context.Emettre(
                TypeNotification.ActiviteBloquee, dossier, $"activite-bloquee:{dossier.Id:N}", destinataires.ResponsablesDelegues(), auteur, le);
        }
    }

    internal static string CleDanger(AlerteDanger alerte) => $"danger:{alerte.Id:N}";
}

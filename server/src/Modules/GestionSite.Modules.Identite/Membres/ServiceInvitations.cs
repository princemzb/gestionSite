using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>Détail d'une invitation en attente pour son titulaire : rôles proposés (nom du site) et administrateur qui invite.</summary>
internal sealed record InvitationDetaillee(Membre Membre, string? InvitePar, IReadOnlyList<(string Role, Guid? SiteId, string? Site)> Roles);

/// <summary>Le titulaire accepte ou refuse ses invitations (CONTRATS : /me/invitations).</summary>
internal sealed class ServiceInvitations(
    IdentiteDbContext context, CompteCourant compte, OrganisationCourante organisation, TimeProvider horloge,
    IServiceScopeFactory portees)
{
    /// <summary>
    /// Rôles proposés et auteur de l'invitation (décision du 03/11, Q8), lus organisation par organisation dans une portée
    /// dédiée (organisation courante fixée, RLS comprise) : les politiques « par compte » n'ouvrent ni les rôles ni les sites
    /// d'une invitation non acceptée. Seules les invitations du titulaire, déjà lues par lui, sont détaillées.
    /// </summary>
    internal async Task<IReadOnlyList<InvitationDetaillee>> DetaillerAsync(IReadOnlyList<Membre> invitations, CancellationToken cancellationToken)
    {
        var details = new List<InvitationDetaillee>(invitations.Count);
        foreach (var invitation in invitations)
        {
            await using var portee = portees.CreateAsyncScope();
            portee.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(invitation.OrganisationId);
            var lecture = portee.ServiceProvider.GetRequiredService<IdentiteDbContext>();
            var roles = await lecture.RolesAttribues.AsNoTracking().Where(r => r.MembreId == invitation.Id)
                .Select(r => new { r.Role, r.SiteId }).ToListAsync(cancellationToken).ConfigureAwait(false);
            var siteIds = roles.Where(r => r.SiteId is not null).Select(r => r.SiteId!.Value).ToList();
            var sites = await lecture.Sites.AsNoTracking().Where(s => siteIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Nom, cancellationToken).ConfigureAwait(false);
            var auteurId = await lecture.JournalHabilitations.AsNoTracking()
                .Where(e => e.MembreId == invitation.Id && (e.Nature == NatureHabilitation.Creation || e.Nature == NatureHabilitation.Ouverture))
                .OrderBy(e => e.Id).Select(e => e.AuteurCompteId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var auteur = auteurId is { } id
                ? await context.Users.AsNoTracking().Where(c => c.Id == id).Select(c => c.NomAffiche).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;
            details.Add(new InvitationDetaillee(invitation, auteur, [.. roles
                .OrderBy(r => r.SiteId is null ? string.Empty : sites.GetValueOrDefault(r.SiteId.Value), StringComparer.CurrentCulture)
                .ThenBy(r => r.Role)
                .Select(r => (r.Role.ToString(), r.SiteId, r.SiteId is { } site ? sites.GetValueOrDefault(site) : null))]));
        }

        return details;
    }

    internal Task AccepterAsync(Guid membreId, CancellationToken cancellationToken) =>
        TraiterAsync(membreId, NatureHabilitation.Acceptation, (m, le) => m.Accepter(le), cancellationToken);

    internal Task RefuserAsync(Guid membreId, CancellationToken cancellationToken) =>
        TraiterAsync(membreId, NatureHabilitation.Refus, (m, le) => m.Revoquer(le), cancellationToken);

    private async Task TraiterAsync(Guid membreId, NatureHabilitation nature, Action<Membre, DateTimeOffset> action, CancellationToken cancellationToken)
    {
        var compteId = compte.Id ?? throw RefusException.Introuvable();
        // Lecture par la politique « par compte » : seulement une invitation de l'appelant lui-même.
        var invitation = await context.Membres.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == membreId && m.CompteId == compteId && m.RevoqueLe == null && m.AccepteLe == null, cancellationToken)
            .ConfigureAwait(false) ?? throw RefusException.Introuvable();

        // Écriture sous l'organisation de l'invitation (isolation par organisation, RLS comprise).
        organisation.Definir(invitation.OrganisationId);
        var membre = await context.Membres.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.Id == membreId && m.RevoqueLe == null && m.AccepteLe == null, cancellationToken)
            .ConfigureAwait(false) ?? throw RefusException.Introuvable(); // traitée entre-temps
        var avant = EvenementHabilitation.Instantane(membre);
        var maintenant = horloge.GetUtcNow();
        action(membre, maintenant);
        context.JournalHabilitations.Add(EvenementHabilitation.Pour(membre, nature, compteId, maintenant, motif: null, avant));
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw RefusException.Conflit("invitation_modifiee", "L'invitation a été modifiée entre-temps. Recharger.");
        }
    }
}

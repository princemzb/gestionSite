using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>Le titulaire accepte ou refuse ses invitations (CONTRATS : /me/invitations).</summary>
internal sealed class ServiceInvitations(
    IdentiteDbContext context, CompteCourant compte, OrganisationCourante organisation, ServiceMembres membres, TimeProvider horloge)
{
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
        action(membre, horloge.GetUtcNow());
        membres.Journaliser(membre, nature, motif: null, avant, apres: nature == NatureHabilitation.Acceptation);
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

using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>
/// Commande d'exploitation réservée à la plateforme (ADR-10) : ouvre une organisation et son premier
/// administrateur. Pas d'endpoint HTTP : seule une personne ayant accès à l'hébergement peut la lancer.
/// </summary>
internal static class OuvertureOrganisation
{
    internal static async Task<(Guid OrganisationId, ActivationEmise? Activation)> OuvrirAsync(
        IServiceProvider services, string nom, string emailAdministrateur, string nomAdministrateur, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var organisationId = Guid.NewGuid();
        scope.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(organisationId);
        var context = scope.ServiceProvider.GetRequiredService<IdentiteDbContext>();
        var membres = scope.ServiceProvider.GetRequiredService<ServiceMembres>();
        var maintenant = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        context.Organisations.Add(new Organisation(organisationId, nom, maintenant));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var (compte, cree) = await membres.TrouverOuCreerCompteAsync(emailAdministrateur, nomAdministrateur).ConfigureAwait(false);
        if (!cree && compte.PasswordHash is null)
        {
            throw new InvalidOperationException("Ce compte n'est pas encore activé : l'activer avant de l'ajouter à une autre organisation.");
        }

        var administrateur = new Membre(Guid.NewGuid(), organisationId, compte.Id, maintenant);
        // Invitation comme ailleurs : compte créé, l'activation l'accepte ; compte existant, le titulaire
        // l'accepte dans /me (ADR-10, complément du 28/10).
        administrateur.Accorder(Role.Administrateur, site: null);
        context.Membres.Add(administrateur);
        context.JournalHabilitations.Add(EvenementHabilitation.Pour(
            administrateur, NatureHabilitation.Ouverture, auteurCompteId: null, maintenant, "Ouverture de l'organisation par la plateforme", avant: null));
        var activation = cree ? await membres.EmettreActivationAsync(compte.Id, organisationId, cancellationToken).ConfigureAwait(false) : null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (organisationId, activation);
    }
}

using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>
/// Conditions d'un lien de réinitialisation créé par un administrateur (décision du porteur du 03/11) : le compte a été créé
/// par cette organisation (lien d'activation qu'elle a émis) et n'est lié à aucune autre, invitation en attente comprise.
/// </summary>
internal static class EligibiliteReinitialisation
{
    /// <summary>
    /// Organisation qui a créé le compte : celle de son premier lien d'activation (données hors organisation ; seule
    /// l'organisation créatrice en émet ensuite). Null pour un compte jamais invité par lien.
    /// </summary>
    internal static Task<Guid?> CreeParAsync(IdentiteDbContext context, Guid compteId, CancellationToken cancellationToken) =>
        context.Activations.Where(a => a.CompteId == compteId).OrderBy(a => a.CreeLe)
            .Select(a => (Guid?)a.OrganisationId).FirstOrDefaultAsync(cancellationToken);

    internal static async Task<bool> EstEligibleAsync(IdentiteDbContext context, Guid compteId, Guid organisationId, CancellationToken cancellationToken)
    {
        if (await CreeParAsync(context, compteId, cancellationToken).ConfigureAwait(false) != organisationId)
        {
            return false;
        }

        // Adhésions d'autres organisations, invisibles sous la RLS : la fonction ne rend qu'un booléen (migration).
        var lie = await context.Database
            .SqlQuery<bool>($"SELECT identite.compte_lie_a_une_autre_organisation({compteId}, {organisationId}) AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        return !lie;
    }
}

/// <summary>
/// POST /auth/reset-password (US-31) : le lien à usage unique permet de choisir un nouveau mot de passe ; tous les appareils
/// du compte sont déconnectés (décision du 03/11). Conditions revérifiées à l'usage : le compte a pu rejoindre une autre
/// organisation, ou quitter celle qui a émis le lien, depuis sa création.
/// </summary>
internal sealed class ServiceReinitialisation(
    IdentiteDbContext context, UserManager<Compte> comptes, OrganisationCourante organisation, TimeProvider horloge)
{
    /// <summary>Rend l'adresse du compte : le titulaire peut ainsi être connecté aussitôt.</summary>
    internal async Task<string> ReinitialiserAsync(string jeton, string motDePasse, CancellationToken cancellationToken)
    {
        var empreinte = SecretsAleatoires.Empreinte(jeton);
        var maintenant = horloge.GetUtcNow();
        var lien = empreinte is null
            ? null
            : await context.Reinitialisations.SingleOrDefaultAsync(r => r.Empreinte == empreinte, cancellationToken).ConfigureAwait(false);
        var compte = lien is null ? null : await comptes.FindByIdAsync(lien.CompteId.ToString()).ConfigureAwait(false);
        if (lien is null || compte?.PasswordHash is null || !lien.EstUtilisable(maintenant))
        {
            throw LienInvalide();
        }

        foreach (var validateur in comptes.PasswordValidators)
        {
            var verdict = await validateur.ValidateAsync(comptes, compte, motDePasse ?? string.Empty).ConfigureAwait(false);
            if (!verdict.Succeeded)
            {
                throw RefusException.Invalide("mot_de_passe_invalide", "Le mot de passe doit contenir au moins 12 caractères.");
            }
        }

        // Journal et adhésion lus sous l'organisation qui a émis le lien (isolation par organisation, RLS comprise).
        organisation.Definir(lien.OrganisationId);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var membre = await context.Membres.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.CompteId == compte.Id && m.RevoqueLe == null && m.AccepteLe != null, cancellationToken).ConfigureAwait(false);
        // Devenu administrateur depuis la création du lien : le lien ne vaut plus (décision du 03/11).
        if (membre is null || membre.EstAdministrateur || !await EligibiliteReinitialisation.EstEligibleAsync(context, compte.Id, lien.OrganisationId, cancellationToken).ConfigureAwait(false))
        {
            throw LienInvalide();
        }

        lien.Utiliser(maintenant);
        compte.PasswordHash = comptes.PasswordHasher.HashPassword(compte, motDePasse!);
        compte.SecurityStamp = Guid.NewGuid().ToString();
        // Le titulaire qui a oublié son mot de passe s'est souvent verrouillé : le lien le libère.
        compte.AccessFailedCount = 0;
        compte.LockoutEnd = null;
        // Déconnexion de tous les appareils : chaque jeton de renouvellement encore valide est révoqué.
        await context.JetonsRenouvellement.Where(j => j.CompteId == compte.Id && j.RevoqueLe == null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.RevoqueLe, maintenant), cancellationToken).ConfigureAwait(false);
        context.JournalHabilitations.Add(EvenementHabilitation.Pour(
            membre, NatureHabilitation.MotDePasseReinitialise, compte.Id, maintenant, motif: null, avant: null));
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException conflit) when (conflit.Entries.Any(e => e.Entity is Reinitialisation))
        {
            throw LienInvalide(); // lien utilisé au même moment par une autre requête
        }
        catch (DbUpdateConcurrencyException)
        {
            // Compte modifié au même moment (essai de connexion, par exemple) : rien n'est écrit, le lien reste valable.
            throw RefusException.Conflit("compte_modifie", "Le compte a été modifié au même moment. Réessayer.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return compte.Email!;
    }

    private static RefusException LienInvalide() =>
        new(410, "lien_invalide", "Lien de réinitialisation expiré ou déjà utilisé. Demander un nouveau lien à l'administrateur.");
}

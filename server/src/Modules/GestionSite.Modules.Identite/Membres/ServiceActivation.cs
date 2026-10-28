using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Membres;

/// <summary>POST /auth/activate : le lien à usage unique permet de choisir son mot de passe (ADR-10).</summary>
internal sealed class ServiceActivation(IdentiteDbContext context, UserManager<Compte> comptes, TimeProvider horloge)
{
    internal async Task ActiverAsync(string jeton, string motDePasse, CancellationToken cancellationToken)
    {
        var empreinte = SecretsAleatoires.Empreinte(jeton);
        var maintenant = horloge.GetUtcNow();
        var activation = empreinte is null
            ? null
            : await context.Activations.SingleOrDefaultAsync(a => a.Empreinte == empreinte, cancellationToken).ConfigureAwait(false);
        var compte = activation is null ? null : await comptes.FindByIdAsync(activation.CompteId.ToString()).ConfigureAwait(false);
        if (activation is null || compte is null || !activation.EstUtilisable(maintenant) || compte.PasswordHash is not null)
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

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        activation.Utiliser(maintenant);
        compte.PasswordHash = comptes.PasswordHasher.HashPassword(compte, motDePasse!);
        compte.EmailConfirmed = true; // le lien a été transmis à cette adresse par l'administrateur
        compte.SecurityStamp = Guid.NewGuid().ToString();
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw LienInvalide(); // lien utilisé au même moment par une autre requête
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static RefusException LienInvalide() =>
        new(410, "lien_invalide", "Lien d'activation expiré ou déjà utilisé. Demander un nouveau lien à l'administrateur.");
}

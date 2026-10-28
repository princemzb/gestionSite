using System.Security.Claims;
using System.Security.Cryptography;
using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GestionSite.Modules.Identite.Authentification;

internal enum EchecAuthentification
{
    IdentifiantsInvalides,
    CompteVerrouille,
    JetonInvalide,
}

internal sealed record CoupleJetons(string JetonAcces, DateTimeOffset AccesExpireLe, string JetonRenouvellement, DateTimeOffset RenouvellementExpireLe);

/// <summary>Résultat sans exception : succès (jetons) ou motif d'échec à traduire en réponse HTTP.</summary>
internal sealed record ResultatAuthentification(CoupleJetons? Jetons, EchecAuthentification? Echec)
{
    internal static ResultatAuthentification Reussi(CoupleJetons jetons) => new(jetons, null);

    internal static ResultatAuthentification Refuse(EchecAuthentification echec) => new(null, echec);
}

/// <summary>Connexion, rotation et révocation des jetons (CONTRATS, US-06).</summary>
internal sealed class ServiceAuthentification(
    UserManager<Compte> comptes,
    IdentiteDbContext context,
    IOptions<AuthentificationOptions> options,
    TimeProvider horloge)
{
    // Empreinte de référence : un e-mail inconnu coûte le même calcul qu'un mot de passe faux.
    private static readonly Lazy<string> EmpreinteFactice = new(() => new PasswordHasher<Compte>().HashPassword(new Compte(), Guid.NewGuid().ToString()));

    internal async Task<ResultatAuthentification> ConnecterAsync(string email, string motDePasse, CancellationToken cancellationToken)
    {
        var compte = string.IsNullOrWhiteSpace(email) ? null : await comptes.FindByEmailAsync(email).ConfigureAwait(false);
        if (compte is null)
        {
            comptes.PasswordHasher.VerifyHashedPassword(new Compte(), EmpreinteFactice.Value, motDePasse ?? string.Empty);
            return ResultatAuthentification.Refuse(EchecAuthentification.IdentifiantsInvalides);
        }

        if (await comptes.IsLockedOutAsync(compte).ConfigureAwait(false))
        {
            return ResultatAuthentification.Refuse(EchecAuthentification.CompteVerrouille);
        }

        if (!await comptes.CheckPasswordAsync(compte, motDePasse ?? string.Empty).ConfigureAwait(false))
        {
            await comptes.AccessFailedAsync(compte).ConfigureAwait(false);
            return await comptes.IsLockedOutAsync(compte).ConfigureAwait(false)
                ? ResultatAuthentification.Refuse(EchecAuthentification.CompteVerrouille)
                : ResultatAuthentification.Refuse(EchecAuthentification.IdentifiantsInvalides);
        }

        await comptes.ResetAccessFailedCountAsync(compte).ConfigureAwait(false);
        var jetons = Emettre(compte.Id, famille: Guid.NewGuid());
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ResultatAuthentification.Reussi(jetons);
    }

    internal async Task<ResultatAuthentification> RenouvelerAsync(string jeton, CancellationToken cancellationToken)
    {
        var actuel = await TrouverAsync(jeton, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        if (actuel is null)
        {
            return ResultatAuthentification.Refuse(EchecAuthentification.JetonInvalide);
        }

        if (actuel.RemplacePar is not null)
        {
            // Réutilisation d'un jeton déjà remplacé : vol probable, toute la lignée est révoquée.
            await RevoquerFamilleAsync(actuel.Famille, maintenant, cancellationToken).ConfigureAwait(false);
            return ResultatAuthentification.Refuse(EchecAuthentification.JetonInvalide);
        }

        var compte = await comptes.FindByIdAsync(actuel.CompteId.ToString()).ConfigureAwait(false);
        if (!actuel.EstUtilisable(maintenant) || compte is null || await comptes.IsLockedOutAsync(compte).ConfigureAwait(false))
        {
            return ResultatAuthentification.Refuse(EchecAuthentification.JetonInvalide);
        }

        var jetons = Emettre(actuel.CompteId, actuel.Famille, out var successeur);
        actuel.Remplacer(successeur, maintenant);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultatAuthentification.Refuse(EchecAuthentification.JetonInvalide); // rotation concurrente
        }

        return ResultatAuthentification.Reussi(jetons);
    }

    /// <summary>Déconnexion de l'appareil : la lignée du jeton présenté, s'il appartient au compte.</summary>
    internal async Task DeconnecterAsync(Guid compteId, string jeton, CancellationToken cancellationToken)
    {
        var actuel = await TrouverAsync(jeton, cancellationToken).ConfigureAwait(false);
        if (actuel is not null && actuel.CompteId == compteId)
        {
            await RevoquerFamilleAsync(actuel.Famille, horloge.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
    }

    private CoupleJetons Emettre(Guid compteId, Guid famille) => Emettre(compteId, famille, out _);

    private CoupleJetons Emettre(Guid compteId, Guid famille, out Guid identifiant)
    {
        var reglages = options.Value;
        var maintenant = horloge.GetUtcNow();
        var accesExpireLe = maintenant + reglages.DureeJetonAcces;
        var acces = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = reglages.Emetteur,
            Audience = reglages.Audience,
            IssuedAt = maintenant.UtcDateTime,
            NotBefore = maintenant.UtcDateTime,
            Expires = accesExpireLe.UtcDateTime,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, compteId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(reglages.Cle()), SecurityAlgorithms.HmacSha256),
        });

        var secret = RandomNumberGenerator.GetBytes(32);
        var renouvellement = Base64UrlEncoder.Encode(secret);
        identifiant = Guid.NewGuid();
        var expireLe = maintenant + reglages.DureeJetonRenouvellement;
        context.JetonsRenouvellement.Add(new JetonRenouvellement(identifiant, compteId, famille, SHA256.HashData(secret), maintenant, expireLe));
        return new CoupleJetons(acces, accesExpireLe, renouvellement, expireLe);
    }

    private async Task<JetonRenouvellement?> TrouverAsync(string jeton, CancellationToken cancellationToken)
    {
        byte[] secret;
        try
        {
            secret = Base64UrlEncoder.DecodeBytes(jeton ?? string.Empty);
        }
        catch (FormatException)
        {
            return null;
        }

        if (secret.Length != 32)
        {
            return null;
        }

        var empreinte = SHA256.HashData(secret);
        return await context.JetonsRenouvellement.SingleOrDefaultAsync(j => j.Empreinte == empreinte, cancellationToken).ConfigureAwait(false);
    }

    private Task<int> RevoquerFamilleAsync(Guid famille, DateTimeOffset le, CancellationToken cancellationToken) =>
        context.JetonsRenouvellement
            .Where(j => j.Famille == famille && j.RevoqueLe == null)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.RevoqueLe, le), cancellationToken);
}

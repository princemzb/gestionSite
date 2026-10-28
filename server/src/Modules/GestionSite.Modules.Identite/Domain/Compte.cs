using Microsoft.AspNetCore.Identity;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Compte de connexion (ASP.NET Core Identity, ADR-10) : e-mail, empreinte du mot de passe, verrouillage.
/// Rattaché aux organisations par ses adhésions (Membre.CompteId). Sans mot de passe tant que le lien
/// d'activation n'a pas servi.
/// </summary>
internal sealed class Compte : IdentityUser<Guid>, IDonneeGlobale
{
    internal const int NomMaxLength = NomAffichable.MaxLength;

    public string NomAffiche { get; private set; } = string.Empty;

    internal static Compte Nouveau(string email, string nom) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        NomAffiche = NomAffichable.Valider(nom, nameof(nom)),
    };
}

using Microsoft.AspNetCore.Identity;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Compte de connexion (ASP.NET Core Identity, ADR-10) : e-mail, empreinte du mot de passe, verrouillage.
/// Rattaché aux organisations par ses adhésions (Membre.CompteId).
/// </summary>
internal sealed class Compte : IdentityUser<Guid>, IDonneeGlobale;

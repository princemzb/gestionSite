using GestionSite.Modules.Identite.Domain;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Authentification;

internal sealed record DemandeConnexion(string Email, string MotDePasse);

internal sealed record DemandeJeton(string JetonRenouvellement);

internal sealed record ReponseJetons(string JetonAcces, DateTimeOffset AccesExpireLe, string JetonRenouvellement, DateTimeOffset RenouvellementExpireLe);

internal sealed record ReponseMoi(
    Guid CompteId, string? Email, IReadOnlyList<OrganisationAutorisee> Organisations, IReadOnlyList<InvitationEnAttente> Invitations);

internal sealed record InvitationEnAttente(Guid MembreId, Guid OrganisationId, string Organisation);

internal sealed record OrganisationAutorisee(Guid Id, string Nom, IReadOnlyList<RoleAutorise> Roles, IReadOnlyList<SiteAutorise> Sites);

internal sealed record RoleAutorise(string Role, Guid? SiteId);

internal sealed record SiteAutorise(Guid Id, string Nom);

/// <summary>Opérations /auth et /me (CONTRATS, US-06). Erreurs : ProblemDetails avec un code stable.</summary>
internal static class AuthentificationEndpoints
{
    internal static IEndpointRouteBuilder MapAuthentification(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth");
        auth.MapPost("/login", ConnecterAsync).AllowAnonymous();
        auth.MapPost("/refresh", RenouvelerAsync).AllowAnonymous();
        auth.MapPost("/logout", DeconnecterAsync).AllowAnonymous(); // le jeton de renouvellement fait preuve
        endpoints.MapGet("/me", MoiAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> ConnecterAsync(DemandeConnexion demande, ServiceAuthentification service, CancellationToken cancellationToken) =>
        Repondre(await service.ConnecterAsync(demande.Email, demande.MotDePasse, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RenouvelerAsync(DemandeJeton demande, ServiceAuthentification service, CancellationToken cancellationToken) =>
        Repondre(await service.RenouvelerAsync(demande.JetonRenouvellement, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DeconnecterAsync(DemandeJeton demande, ServiceAuthentification service, CancellationToken cancellationToken)
    {
        await service.DeconnecterAsync(demande.JetonRenouvellement, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// Adhésions actives du compte, toutes organisations confondues : lecture permise par les politiques
    /// RLS « par compte » (aucune organisation courante), filtres d'organisation volontairement ignorés.
    /// </summary>
    private static async Task<IResult> MoiAsync(IdentiteDbContext context, CompteCourant compte, CancellationToken cancellationToken)
    {
        var compteId = compte.Id!.Value;
        var email = await context.Users.Where(c => c.Id == compteId).Select(c => c.Email).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var membres = await context.Membres.IgnoreQueryFilters().Include(m => m.Roles)
            .Where(m => m.CompteId == compteId && m.RevoqueLe == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var organisationIds = membres.Select(m => m.OrganisationId).ToList();
        var organisations = await context.Organisations.IgnoreQueryFilters()
            .Where(o => organisationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken).ConfigureAwait(false);
        var sites = await context.Sites.IgnoreQueryFilters()
            .Where(s => organisationIds.Contains(s.OrganisationId)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var invitations = membres
            .Where(m => m.AccepteLe is null && organisations.ContainsKey(m.OrganisationId))
            .Select(m => new InvitationEnAttente(m.Id, m.OrganisationId, organisations[m.OrganisationId].Nom))
            .ToList();
        var autorisees = membres
            .Where(m => m.EstActif && organisations.ContainsKey(m.OrganisationId))
            .Select(m => new OrganisationAutorisee(
                m.OrganisationId,
                organisations[m.OrganisationId].Nom,
                [.. m.Roles.Select(r => new RoleAutorise(r.Role.ToString(), r.SiteId))],
                [.. sites.Where(s => s.OrganisationId == m.OrganisationId && EstAutorise(m, s)).OrderBy(s => s.Nom).Select(s => new SiteAutorise(s.Id, s.Nom))]))
            .OrderBy(o => o.Nom)
            .ToList();
        return Results.Ok(new ReponseMoi(compteId, email, autorisees, invitations));
    }

    // Même règle que la politique RLS des sites : tous pour l'administrateur, sinon ceux où un rôle est accordé.
    private static bool EstAutorise(Membre membre, Site site) =>
        membre.Roles.Any(r => r.Role == Role.Administrateur || r.SiteId == site.Id);

    private static IResult Repondre(ResultatAuthentification resultat) => resultat switch
    {
        { Jetons: { } j } => Results.Ok(new ReponseJetons(j.JetonAcces, j.AccesExpireLe, j.JetonRenouvellement, j.RenouvellementExpireLe)),
        { Echec: EchecAuthentification.CompteVerrouille } => Probleme(StatusCodes.Status423Locked, "compte_verrouille",
            "Compte temporairement verrouillé après plusieurs échecs. Réessayer plus tard."),
        { Echec: EchecAuthentification.JetonInvalide } => Probleme(StatusCodes.Status401Unauthorized, "jeton_invalide",
            "Session expirée ou révoquée. Se reconnecter."),
        _ => Probleme(StatusCodes.Status401Unauthorized, "identifiants_invalides", "E-mail ou mot de passe incorrect."),
    };

    private static IResult Probleme(int statut, string code, string message) =>
        Results.Problem(statusCode: statut, title: message, extensions: new Dictionary<string, object?> { ["code"] = code });
}

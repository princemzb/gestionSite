namespace GestionSite.Web.Session;

// Réponses de l'API lues par l'application web (docs/dev/CONTRATS.md).
internal sealed record JetonsApi(string JetonAcces, DateTimeOffset AccesExpireLe, string JetonRenouvellement, DateTimeOffset RenouvellementExpireLe);

internal sealed record MoiApi(Guid CompteId, string? Email, IReadOnlyList<OrganisationApi> Organisations, IReadOnlyList<InvitationApi>? Invitations);

internal sealed record InvitationApi(Guid MembreId, Guid OrganisationId, string Organisation);

internal sealed record OrganisationApi(Guid Id, string Nom, IReadOnlyList<RoleApi> Roles, IReadOnlyList<SiteApi> Sites);

internal sealed record RoleApi(string Role, Guid? SiteId);

internal sealed record SiteApi(Guid Id, string Nom);

internal sealed record MembreApi(Guid MembreId, Guid CompteId, string Nom, string? Email, bool InvitationEnAttente, int Version, IReadOnlyList<RoleApi> Roles);

internal sealed record ActivationApi(string Jeton, DateTimeOffset ExpireLe);

internal sealed record MembreCreeApi(Guid MembreId, Guid CompteId, int Version, ActivationApi? Activation);

internal sealed record ProblemeApi(string? Code, string? Title);

internal sealed record ActivationReussieApi(string Email);

internal sealed record LienPublicApi(Guid SiteId, string Nom, string Code, string? NumeroUrgence);

internal sealed record SitePublicApi(string Site, string Organisation);

internal sealed record AccuseApi(string Site, bool Danger, string? NumeroUrgence);

internal sealed record DepotRecuApi(
    Guid Id, DateTimeOffset RecuLe, string Nature, bool Danger, bool ActiviteBloquee, string Emplacement, string Description,
    string NomDeclarant, string ContactDeclarant, IReadOnlyList<int> Photos);

using GestionSite.Modules.Identite.Authentification;
using GestionSite.Modules.Identite.Autorisation;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Identite.Membres;
using GestionSite.Modules.Identite.Persistence;
using GestionSite.Modules.Identite.SitesPublics;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GestionSite.Modules.Identite;

/// <summary>
/// Point d'entrée du module Identite : comptes, adhésions aux organisations, rôles et sites autorisés.
/// Les autres modules n'y accèdent que par ses opérations publiques (ARCHITECTURE.md).
/// </summary>
public static class IdentiteModule
{
    public static IServiceCollection AddIdentiteModule(this IServiceCollection services) =>
        services.AddIdentitePersistence().AddAuthentificationIdentite()
            .AddScoped<Autorisations>().AddScoped<IAutorisations>(p => p.GetRequiredService<Autorisations>())
            .AddScoped<ISitesPublics, RechercheSitesPublics>().AddScoped<ServiceLiensPublics>()
            .AddScoped<AccesOrganisation>().AddScoped<ServiceMembres>().AddScoped<ServiceActivation>().AddScoped<ServiceInvitations>();

    public static IEndpointRouteBuilder MapIdentiteEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapAuthentification().MapOrganisations();

    /// <summary>
    /// Commande d'exploitation « ouvrir-organisation » : crée l'organisation et son administrateur ; rend le
    /// lien d'activation à transmettre (null si le compte avait déjà un mot de passe).
    /// </summary>
    public static async Task<OrganisationOuverte> OuvrirOrganisationAsync(
        IServiceProvider services, string nom, string emailAdministrateur, string nomAdministrateur, CancellationToken cancellationToken,
        string? premierSite = null)
    {
        var (id, activation) = await OuvertureOrganisation.OuvrirAsync(
            services, nom, emailAdministrateur, nomAdministrateur, premierSite, cancellationToken).ConfigureAwait(false);
        return new OrganisationOuverte(id, activation?.Jeton, activation?.ExpireLe);
    }
}

/// <summary>Résultat de la commande d'ouverture d'organisation.</summary>
public sealed record OrganisationOuverte(Guid OrganisationId, string? JetonActivation, DateTimeOffset? ActivationExpireLe);

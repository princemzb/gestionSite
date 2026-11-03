using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GestionSite.Modules.Maintenance.Planificateur;

/// <summary>
/// Bilan d'un passage, sans aucune donnée d'organisation : nombres, et instant du prochain seuil à venir (l'alarme du
/// Durable Object y est réglée) ; null s'il n'y a rien à venir.
/// </summary>
internal sealed record BilanPassage(int Organisations, int Notifications, int Echecs, DateTimeOffset? ProchainPassage);

/// <summary>
/// Passage du planificateur (US-10, S10-02), appelé par le Worker (alarme du Durable Object ou déclencheur quotidien de
/// secours). Il n'a ni organisation ni compte : la fonction <c>maintenance.organisations_a_planifier()</c>
/// (<c>SECURITY DEFINER</c>) ne lui donne que les identifiants des organisations ayant un dossier ouvert, puis chacune est
/// traitée dans sa propre portée, sous sa RLS, exactement comme une requête de cette organisation. Une organisation en
/// échec n'empêche pas les autres ; elle sera reprise au passage suivant (au plus tard dans une heure).
/// </summary>
internal sealed partial class ServicePlanificateur(IServiceScopeFactory portees, ILogger<ServicePlanificateur> journal)
{
    /// <summary>Nouvel essai d'une organisation en échec (erreur passagère de la base) : borne le coût d'un réveil répété.</summary>
    internal static readonly TimeSpan Reprise = TimeSpan.FromHours(1);

    internal async Task<BilanPassage> PasserAsync(DateTimeOffset maintenant, CancellationToken cancellationToken)
    {
        var instant = maintenant.ToUniversalTime();
        var organisations = await OrganisationsAsync(cancellationToken).ConfigureAwait(false);
        var notifications = 0;
        var echecs = 0;
        DateTimeOffset? prochain = null;
        foreach (var organisation in organisations)
        {
            BilanOrganisation bilan;
            try
            {
                await using var portee = portees.CreateAsyncScope();
                portee.ServiceProvider.GetRequiredService<OrganisationCourante>().Definir(organisation);
                bilan = await portee.ServiceProvider.GetRequiredService<RappelsOrganisation>().TraiterAsync(instant, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception erreur) when (erreur is not OperationCanceledException)
            {
                // Identifiant technique seulement (aucune donnée de dossier ni de personne dans le journal).
                EchecOrganisation(journal, erreur, organisation);
                echecs++;
                bilan = new BilanOrganisation(0, instant + Reprise);
            }

            notifications += bilan.Notifications;
            if (bilan.Prochain is { } p && (prochain is null || p < prochain))
            {
                prochain = p;
            }
        }

        return new BilanPassage(organisations.Count, notifications, echecs, prochain);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Planificateur : échec du passage de l'organisation {Organisation}")]
    private static partial void EchecOrganisation(ILogger journal, Exception erreur, Guid organisation);

    // Seule lecture hors organisation : des identifiants, par la fonction SECURITY DEFINER (jamais de contournement global).
    private async Task<List<Guid>> OrganisationsAsync(CancellationToken cancellationToken)
    {
        await using var portee = portees.CreateAsyncScope();
        var context = portee.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        return await context.Database
            .SqlQuery<Guid>($"SELECT o AS \"Value\" FROM maintenance.organisations_a_planifier() AS o")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

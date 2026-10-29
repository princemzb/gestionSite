using System.Text.Json;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.Dossiers;

/// <summary>Écritures d'un dossier communes aux services : version lue, journal en ajout seul, conflits.</summary>
internal static class DossiersPersistance
{
    private const string NumeroIntervention = "ix_interventions_dossier_id_organisation_id_numero";

    /// <summary>
    /// Dossier verrouillé (transaction ouverte par l'appelant) puis version comparée explicitement : une version dépassée
    /// est refusée même quand l'opération ne modifie pas le dossier lui-même (seconde affectation, autre démarrage).
    /// </summary>
    internal static async Task<Signalement> DossierAVersionAsync(
        this MaintenanceDbContext context, Guid siteId, Guid id, uint? version, CancellationToken cancellationToken)
    {
        var attendue = version ?? throw new SaisieInvalideException("version", "Version du dossier lue obligatoire.");
        await context.VerrouillerAsync([id], cancellationToken).ConfigureAwait(false);
        var dossier = await context.Signalements.SingleOrDefaultAsync(s => s.Id == id && s.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new DossierIntrouvableException();
        if (context.Entry(dossier).Property<uint>("xmin").CurrentValue != attendue)
        {
            throw new DossierModifieException();
        }

        return dossier;
    }

    /// <summary>Verrous de ligne dans un ordre fixe (pas d'interblocage entre deux opérations croisées).</summary>
    internal static async Task VerrouillerAsync(this MaintenanceDbContext context, Guid[] ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids.Distinct().Order())
        {
            await context.Database.ExecuteSqlAsync($"SELECT 1 FROM maintenance.signalements WHERE id = {id} FOR UPDATE", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal static void Tracer(
        this MaintenanceDbContext context, Signalement dossier, NatureEvenementDossier nature, Guid? auteurCompteId, DateTimeOffset le, object? detail) =>
        context.Journal.Add(new EvenementDossier(
            Guid.CreateVersion7(le), dossier, nature, auteurCompteId, le, detail is null ? null : JsonSerializer.Serialize(detail)));

    internal static async Task EnregistrerAsync(this MaintenanceDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException conflit) when (conflit is DbUpdateConcurrencyException
            || conflit.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NumeroIntervention })
        {
            // Version dépassée, ou deux affectations simultanées (même numéro) : relire puis refaire.
            throw new DossierModifieException();
        }
    }
}

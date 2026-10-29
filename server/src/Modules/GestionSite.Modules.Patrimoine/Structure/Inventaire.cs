using System.Text.Json;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Patrimoine.Structure;

/// <summary>Règles communes de l'inventaire : journal en ajout seul, enregistrement et conflits.</summary>
internal static class Inventaire
{
    internal static void Tracer(
        this PatrimoineDbContext context, Guid? auteurCompteId, DateTimeOffset le, Guid siteId, CibleInventaire cible, Guid cibleId,
        NatureEvenementInventaire nature, object? avant, object? apres) =>
        context.Journal.Add(new EvenementInventaire(
            Guid.CreateVersion7(le), context.OrganisationRequise, siteId, cible, cibleId, nature, auteurCompteId, le,
            avant is null ? null : JsonSerializer.Serialize(avant), apres is null ? null : JsonSerializer.Serialize(apres)));

    internal static async Task EnregistrerAsync(this PatrimoineDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ModifieEntreTempsException();
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
            && ContraintesNom.Contient(violation.ConstraintName))
        {
            throw new NomExistantException(); // même nom à la casse près, enregistré en parallèle
        }
    }
}

using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Patrimoine.Structure;

/// <summary>Règles communes de l'inventaire : accès au site, journal en ajout seul, enregistrement et conflits.</summary>
internal static class Inventaire
{
    // Avant toute lecture : une décision favorable fixe l'organisation courante (filtres et RLS).
    internal static async Task ExigerAsync(this IAutorisations autorisations, Guid organisationId, Guid siteId, Droit droit, CancellationToken cancellationToken)
    {
        var decision = await autorisations.VerifierAsync(organisationId, droit, siteId, cancellationToken).ConfigureAwait(false);
        if (decision != DecisionAutorisation.Autorise)
        {
            throw new AccesInventaireRefuseException(decision);
        }
    }

    internal static void Tracer(
        this PatrimoineDbContext context, Guid? auteurCompteId, DateTimeOffset le, Guid siteId, CibleInventaire cible, Guid cibleId,
        NatureEvenementInventaire nature, object? avant, object? apres) =>
        context.Journal.Add(new EvenementInventaire(
            Guid.CreateVersion7(le), context.OrganisationCourante, siteId, cible, cibleId, nature, auteurCompteId, le,
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

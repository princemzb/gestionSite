using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.SharedKernel.Persistence;

/// <summary>
/// Contexte d'un module (ADR-10) : chaque donnée rattachée à une organisation n'est lue que dans l'organisation courante
/// (filtre de requête) et n'est écrite que pour elle (garde d'enregistrement) ; la RLS forcée en base reste la seconde
/// barrière. Une donnée non rattachée à une organisation n'est jamais écrite par un module.
/// </summary>
public abstract class ContexteOrganisation(DbContextOptions options, OrganisationCourante organisationCourante) : DbContext(options)
{
    /// <summary>Organisation de la requête, fixée par l'autorisation avant toute écriture.</summary>
    public Guid OrganisationRequise => organisationCourante.Id ?? throw new InvalidOperationException("Organisation courante non définie.");

    // Lu à chaque requête par les filtres (paramètre EF) : sans organisation, aucune ligne.
    protected Guid? OrganisationId => organisationCourante.Id;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ExigerOrganisationCourante();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ExigerOrganisationCourante();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>À appeler en fin d'OnModelCreating : filtre « organisation courante » sur chaque entité rattachée.</summary>
    protected void FiltrerParOrganisation(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        foreach (var type in modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(IAppartientAOrganisation).IsAssignableFrom(t.ClrType)).ToList())
        {
            // e => (Guid?)e.OrganisationId == this.OrganisationId ; EF remplace « this » par le contexte de la requête.
            var donnee = Expression.Parameter(type.ClrType, "e");
            var filtre = Expression.Equal(
                Expression.Convert(Expression.Property(donnee, nameof(IAppartientAOrganisation.OrganisationId)), typeof(Guid?)),
                Expression.Property(Expression.Constant(this), nameof(OrganisationId)));
            modelBuilder.Entity(type.ClrType).HasQueryFilter(Expression.Lambda(filtre, donnee));
        }
    }

    // Double barrière : la RLS refuserait aussi ces écritures, mais l'erreur applicative est plus claire.
    private void ExigerOrganisationCourante()
    {
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        foreach (var entree in ChangeTracker.Entries())
        {
            if (entree.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entree.Entity is not IAppartientAOrganisation donnee)
            {
                throw new InvalidOperationException($"{entree.Metadata.ClrType.Name} n'est rattachée à aucune organisation.");
            }

            if (donnee.OrganisationId != OrganisationId)
            {
                throw new InvalidOperationException("Écriture refusée : la donnée appartient à une autre organisation que celle de la requête.");
            }
        }
    }
}

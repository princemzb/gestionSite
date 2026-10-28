using GestionSite.Modules.Maintenance.Domain;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Persistence;

/// <summary>
/// Données du module Maintenance, schéma « maintenance ». Mêmes barrières qu'Identité (ADR-10) : filtre sur
/// l'organisation courante, refus d'écrire pour une autre, RLS forcée en base (migration).
/// </summary>
internal sealed class MaintenanceDbContext(DbContextOptions<MaintenanceDbContext> options, OrganisationCourante organisationCourante)
    : DbContext(options)
{
    internal const string Schema = "maintenance";
    internal const string MigrationsTable = "__ef_migrations";

    public DbSet<Signalement> Signalements => Set<Signalement>();

    public DbSet<AlerteDanger> AlertesDanger => Set<AlerteDanger>();

    // Lu à chaque requête par les filtres (paramètre EF) : sans organisation, aucune ligne.
    private Guid? OrganisationId => organisationCourante.Id;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Signalement>(signalement =>
        {
            signalement.HasKey(s => s.Id);
            signalement.Property(s => s.Id).ValueGeneratedNever();
            signalement.Property(s => s.Nature).HasConversion<string>().HasMaxLength(16);
            signalement.Property(s => s.Description).HasMaxLength(Signalement.DescriptionMax);
            signalement.Property(s => s.Emplacement).HasMaxLength(Signalement.EmplacementMax);
            signalement.Property(s => s.NomDeclarant).HasMaxLength(Signalement.NomMax);
            signalement.Property(s => s.ContactDeclarant).HasMaxLength(Signalement.ContactMax);
            signalement.HasAlternateKey(s => new { s.Id, s.OrganisationId });
            // Un envoi = un signalement, même rejoué ou envoyé deux fois en parallèle (F-01).
            signalement.HasIndex(s => new { s.OrganisationId, s.CommandeId }).IsUnique();
            signalement.HasIndex(s => new { s.OrganisationId, s.SiteId, s.RecuLe });
        });

        modelBuilder.Entity<AlerteDanger>(alerte =>
        {
            alerte.ToTable("alertes_danger");
            alerte.HasKey(a => a.Id);
            alerte.Property(a => a.Id).ValueGeneratedNever();
            alerte.HasIndex(a => new { a.SignalementId, a.OrganisationId }).IsUnique();
            alerte.HasOne<Signalement>().WithMany()
                .HasForeignKey(a => new { a.SignalementId, a.OrganisationId })
                .HasPrincipalKey(s => new { s.Id, s.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Signalement>().HasQueryFilter(s => s.OrganisationId == OrganisationId);
        modelBuilder.Entity<AlerteDanger>().HasQueryFilter(a => a.OrganisationId == OrganisationId);
    }

    // Double barrière : la RLS refuserait aussi ces écritures, mais l'erreur applicative est plus claire.
    private void ExigerOrganisationCourante()
    {
        // Toute écriture dans une transaction : l'organisation y est posée à l'ouverture (intercepteurs du noyau).
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

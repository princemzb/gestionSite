using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Persistence;

/// <summary>
/// Données du module Patrimoine, schéma « patrimoine ». Mêmes barrières que les autres modules (ADR-10) : filtre sur
/// l'organisation courante, refus d'écrire pour une autre, RLS forcée en base (migration).
/// </summary>
internal sealed class PatrimoineDbContext(DbContextOptions<PatrimoineDbContext> options, OrganisationCourante organisationCourante)
    : DbContext(options)
{
    internal const string Schema = "patrimoine";
    internal const string MigrationsTable = "__ef_migrations";

    public DbSet<Batiment> Batiments => Set<Batiment>();

    public DbSet<Niveau> Niveaux => Set<Niveau>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<EvenementInventaire> Journal => Set<EvenementInventaire>();

    private Guid? OrganisationId => organisationCourante.Id;

    /// <summary>Organisation de la requête, fixée par l'autorisation avant toute écriture.</summary>
    internal Guid OrganisationCourante => organisationCourante.Id ?? throw new InvalidOperationException("Organisation courante non définie.");

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
        modelBuilder.Entity<Batiment>(batiment =>
        {
            batiment.ToTable("batiments");
            batiment.HasKey(b => b.Id);
            batiment.Property(b => b.Id).ValueGeneratedNever();
            batiment.Property(b => b.Nom).HasMaxLength(Nom.Max);
            batiment.HasAlternateKey(b => new { b.Id, b.OrganisationId });
            batiment.HasIndex(b => new { b.OrganisationId, b.SiteId, b.Nom }).IsUnique();
        });

        modelBuilder.Entity<Niveau>(niveau =>
        {
            niveau.ToTable("niveaux");
            niveau.HasKey(n => n.Id);
            niveau.Property(n => n.Id).ValueGeneratedNever();
            niveau.Property(n => n.Nom).HasMaxLength(Nom.Max);
            niveau.HasAlternateKey(n => new { n.Id, n.OrganisationId });
            niveau.HasIndex(n => new { n.BatimentId, n.OrganisationId, n.Nom }).IsUnique();
            niveau.HasOne<Batiment>().WithMany()
                .HasForeignKey(n => new { n.BatimentId, n.OrganisationId })
                .HasPrincipalKey(b => new { b.Id, b.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Zone>(zone =>
        {
            zone.ToTable("zones");
            zone.HasKey(z => z.Id);
            zone.Property(z => z.Id).ValueGeneratedNever();
            zone.Property(z => z.Nom).HasMaxLength(Nom.Max);
            zone.HasAlternateKey(z => new { z.Id, z.OrganisationId });
            zone.HasIndex(z => new { z.BatimentId, z.OrganisationId, z.Nom }).IsUnique();
            // Une seule zone « Parties communes » par bâtiment.
            zone.HasIndex(z => new { z.BatimentId, z.OrganisationId }).IsUnique().HasFilter("parties_communes")
                .HasDatabaseName("ix_zones_parties_communes_par_batiment");
            zone.HasOne<Batiment>().WithMany()
                .HasForeignKey(z => new { z.BatimentId, z.OrganisationId })
                .HasPrincipalKey(b => new { b.Id, b.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
            zone.HasOne<Niveau>().WithMany()
                .HasForeignKey(z => new { z.NiveauId, z.OrganisationId })
                .HasPrincipalKey(n => new { n.Id, n.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EvenementInventaire>(evenement =>
        {
            evenement.ToTable("journal_inventaire");
            evenement.HasKey(e => e.Id);
            evenement.Property(e => e.Id).ValueGeneratedNever();
            evenement.Property(e => e.Cible).HasConversion<string>().HasMaxLength(16);
            evenement.Property(e => e.Nature).HasConversion<string>().HasMaxLength(16);
            evenement.Property(e => e.Avant).HasColumnType("jsonb");
            evenement.Property(e => e.Apres).HasColumnType("jsonb");
            evenement.HasIndex(e => new { e.OrganisationId, e.CibleId, e.Id });
        });

        modelBuilder.Entity<Batiment>().HasQueryFilter(b => b.OrganisationId == OrganisationId);
        modelBuilder.Entity<Niveau>().HasQueryFilter(n => n.OrganisationId == OrganisationId);
        modelBuilder.Entity<Zone>().HasQueryFilter(z => z.OrganisationId == OrganisationId);
        modelBuilder.Entity<EvenementInventaire>().HasQueryFilter(e => e.OrganisationId == OrganisationId);
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

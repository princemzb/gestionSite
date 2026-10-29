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
    : ContexteOrganisation(options, organisationCourante)
{
    internal const string Schema = "maintenance";
    internal const string MigrationsTable = "__ef_migrations";

    public DbSet<Signalement> Signalements => Set<Signalement>();

    public DbSet<AlerteDanger> AlertesDanger => Set<AlerteDanger>();

    public DbSet<PhotoSignalement> Photos => Set<PhotoSignalement>();

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
            // Liste par site, du plus récent au plus ancien (identifiants ordonnés dans le temps), curseur « avant ».
            signalement.HasIndex(s => new { s.OrganisationId, s.SiteId, s.Id });
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

        modelBuilder.Entity<PhotoSignalement>(photo =>
        {
            photo.ToTable("photos_signalement", table => table.HasCheckConstraint("ck_photos_signalement_rang", "rang BETWEEN 1 AND 3"));
            photo.HasKey(p => p.Id);
            photo.Property(p => p.Id).ValueGeneratedNever();
            photo.Property(p => p.Cle).HasMaxLength(PhotoSignalement.CleMax);
            photo.HasIndex(p => new { p.SignalementId, p.OrganisationId, p.Rang }).IsUnique();
            photo.HasOne<Signalement>().WithMany()
                .HasForeignKey(p => new { p.SignalementId, p.OrganisationId })
                .HasPrincipalKey(s => new { s.Id, s.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        FiltrerParOrganisation(modelBuilder);
    }
}

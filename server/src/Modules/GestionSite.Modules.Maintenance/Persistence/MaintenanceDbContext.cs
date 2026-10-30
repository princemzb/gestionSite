using GestionSite.Modules.Maintenance.Domain;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Persistence;

/// <summary>
/// Données du module Maintenance, schéma « maintenance ». Mêmes barrières qu'Identité (ADR-10) : filtre sur
/// l'organisation courante, refus d'écrire pour une autre, RLS forcée en base (migration).
/// </summary>
internal sealed partial class MaintenanceDbContext(DbContextOptions<MaintenanceDbContext> options, OrganisationCourante organisationCourante)
    : ContexteOrganisation(options, organisationCourante)
{
    internal const string Schema = "maintenance";
    internal const string MigrationsTable = "__ef_migrations";

    public DbSet<Signalement> Signalements => Set<Signalement>();

    public DbSet<AlerteDanger> AlertesDanger => Set<AlerteDanger>();

    public DbSet<PhotoSignalement> Photos => Set<PhotoSignalement>();

    public DbSet<EvenementDossier> Journal => Set<EvenementDossier>();

    public DbSet<Intervention> Interventions => Set<Intervention>();

    public DbSet<PhotoIntervention> PhotosIntervention => Set<PhotoIntervention>();

    public DbSet<PlanPreventif> Plans => Set<PlanPreventif>();

    public DbSet<PropositionEcartee> PropositionsEcartees => Set<PropositionEcartee>();

    public DbSet<EvenementPlan> JournalPreventif => Set<EvenementPlan>();

    protected override void ConfigurerModele(ModelBuilder modelBuilder)
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
            // Dossier (US-09) : statut, priorité, lieu dans l'inventaire, doublon, classement ; version (xmin) pour les écritures concurrentes.
            signalement.Property(s => s.Origine).HasConversion<string>().HasMaxLength(16).HasDefaultValue(OrigineDossier.Public).HasSentinel((OrigineDossier)0);
            signalement.Property(s => s.Statut).HasConversion<string>().HasMaxLength(24).HasDefaultValue(StatutDossier.AQualifier).HasSentinel((StatutDossier)0);
            signalement.Property(s => s.Priorite).HasConversion<string>().HasMaxLength(16);
            signalement.Property(s => s.MotifClassement).HasConversion<string>().HasMaxLength(24);
            signalement.Property(s => s.CommentaireClassement).HasMaxLength(Signalement.CommentaireMax);
            signalement.Property<uint>("xmin").IsRowVersion();
            signalement.HasIndex(s => new { s.OrganisationId, s.SiteId, s.Statut });
            signalement.HasOne<Signalement>().WithMany()
                .HasForeignKey(s => new { s.DoublonDe, s.OrganisationId })
                .HasPrincipalKey(s => new { s.Id, s.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<Intervention>(intervention =>
        {
            intervention.ToTable("interventions");
            intervention.HasKey(i => i.Id);
            intervention.Property(i => i.Id).ValueGeneratedNever();
            intervention.Property(i => i.Titre).HasMaxLength(Intervention.TitreMax);
            intervention.Property(i => i.Prestataire).HasMaxLength(Intervention.PrestataireMax);
            intervention.Property(i => i.Statut).HasConversion<string>().HasMaxLength(16);
            intervention.Property(i => i.MotifAttente).HasConversion<string>().HasMaxLength(24);
            intervention.Property(i => i.CommentaireAttente).HasMaxLength(Signalement.CommentaireMax);
            intervention.Property(i => i.Actions).HasMaxLength(Intervention.ActionsMax);
            intervention.Property(i => i.EtatFinal).HasConversion<string>().HasMaxLength(24);
            intervention.Property<uint>("xmin").IsRowVersion();
            intervention.HasIndex(i => new { i.DossierId, i.OrganisationId, i.Numero }).IsUnique();
            intervention.HasIndex(i => new { i.OrganisationId, i.SiteId, i.Statut });
            intervention.HasOne<Signalement>().WithMany()
                .HasForeignKey(i => new { i.DossierId, i.OrganisationId })
                .HasPrincipalKey(s => new { s.Id, s.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhotoIntervention>(photo =>
        {
            photo.ToTable("photos_intervention", table => table.HasCheckConstraint("ck_photos_intervention_rang", "rang BETWEEN 1 AND 3"));
            photo.HasKey(p => p.Id);
            photo.Property(p => p.Id).ValueGeneratedNever();
            photo.Property(p => p.Cle).HasMaxLength(PhotoIntervention.CleMax);
            photo.HasIndex(p => new { p.InterventionId, p.OrganisationId, p.Rang }).IsUnique();
            photo.HasOne<Intervention>().WithMany()
                .HasForeignKey(p => new { p.InterventionId, p.OrganisationId })
                .HasPrincipalKey(i => new { i.Id, i.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EvenementDossier>(evenement =>
        {
            evenement.ToTable("journal_dossiers");
            evenement.HasKey(e => e.Id);
            evenement.Property(e => e.Id).ValueGeneratedNever();
            evenement.Property(e => e.Nature).HasConversion<string>().HasMaxLength(32);
            evenement.Property(e => e.Detail).HasColumnType("jsonb");
            evenement.HasIndex(e => new { e.OrganisationId, e.DossierId, e.Id });
            evenement.HasOne<Signalement>().WithMany()
                .HasForeignKey(e => new { e.DossierId, e.OrganisationId })
                .HasPrincipalKey(s => new { s.Id, s.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigurerPreventif(modelBuilder);

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
    }
}

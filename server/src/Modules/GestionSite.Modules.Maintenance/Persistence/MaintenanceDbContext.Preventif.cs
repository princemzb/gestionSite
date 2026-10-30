using GestionSite.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Persistence;

/// <summary>Modèle du préventif (US-12) : plans, propositions écartées et journal des plans.</summary>
internal sealed partial class MaintenanceDbContext
{
    public DbSet<OccurrencePreventive> Occurrences => Set<OccurrencePreventive>();

    public DbSet<ControlePreventif> Controles => Set<ControlePreventif>();

    public DbSet<PhotoControle> PhotosControles => Set<PhotoControle>();

    private static void ConfigurerPreventif(ModelBuilder modelBuilder)
    {
        ConfigurerExecution(modelBuilder);
        modelBuilder.Entity<PlanPreventif>(plan =>
        {
            plan.ToTable("plans_preventifs", table =>
            {
                table.HasCheckConstraint(
                    "ck_plans_preventifs_periode",
                    "(periode_valeur IS NULL) = (periode_unite IS NULL) AND (periode_valeur IS NULL OR periode_valeur BETWEEN 1 AND 120)");
                table.HasCheckConstraint("ck_plans_preventifs_fixe", "mode = 'Glissant' OR periode_valeur IS NOT NULL");
                table.HasCheckConstraint("ck_plans_preventifs_suspension", "suspendu = (motif_suspension IS NOT NULL)");
            });
            plan.HasKey(p => p.Id);
            plan.Property(p => p.Id).ValueGeneratedNever();
            plan.Property(p => p.PropositionCle).HasMaxLength(PlanPreventif.CleMax);
            plan.Property(p => p.Cible).HasConversion<string>().HasMaxLength(16);
            plan.Property(p => p.CorpsEtat).HasMaxLength(32);
            plan.Property(p => p.Nature).HasConversion<string>().HasMaxLength(24);
            plan.Property(p => p.Libelle).HasMaxLength(PlanPreventif.LibelleMax);
            plan.Property(p => p.Base).HasConversion<string>().HasMaxLength(16);
            plan.Property(p => p.Reference).HasMaxLength(PlanPreventif.ReferenceMax);
            plan.Property(p => p.TypeIntervenant).HasConversion<string>().HasMaxLength(24);
            plan.Property(p => p.Prestataire).HasMaxLength(Intervention.PrestataireMax);
            plan.Property(p => p.DonneurOrdre).HasConversion<string>().HasMaxLength(16);
            plan.Property(p => p.PeriodeUnite).HasConversion<string>().HasMaxLength(16);
            plan.Property(p => p.Mode).HasConversion<string>().HasMaxLength(16);
            plan.Property(p => p.MotifSuspension).HasMaxLength(Signalement.CommentaireMax);
            plan.Property<uint>("xmin").IsRowVersion();
            plan.HasAlternateKey(p => new { p.Id, p.OrganisationId }).HasName("ak_plans_preventifs_id_organisation_id");
            plan.HasIndex(p => new { p.OrganisationId, p.CommandeId }).IsUnique().HasDatabaseName(PlanPreventif.ContrainteCommande);
            // Une proposition confirmée ne donne qu'un plan, même confirmée deux fois en parallèle.
            plan.HasIndex(p => new { p.OrganisationId, p.SiteId, p.PropositionCle }).IsUnique().HasFilter("proposition_cle IS NOT NULL")
                .HasDatabaseName(PlanPreventif.ContrainteProposition);
            plan.HasIndex(p => new { p.OrganisationId, p.SiteId, p.ComposantId });
        });

        modelBuilder.Entity<PropositionEcartee>(ecart =>
        {
            ecart.ToTable("propositions_ecartees");
            ecart.HasKey(e => e.Id);
            ecart.Property(e => e.Id).ValueGeneratedNever();
            ecart.Property(e => e.Cle).HasMaxLength(PlanPreventif.CleMax);
            ecart.Property(e => e.Motif).HasConversion<string>().HasMaxLength(16);
            ecart.Property(e => e.Commentaire).HasMaxLength(Signalement.CommentaireMax);
            ecart.HasIndex(e => new { e.OrganisationId, e.SiteId, e.Cle }).IsUnique();
        });

        modelBuilder.Entity<EvenementPlan>(evenement =>
        {
            evenement.ToTable("journal_preventif");
            evenement.HasKey(e => e.Id);
            evenement.Property(e => e.Id).ValueGeneratedNever();
            evenement.Property(e => e.Nature).HasConversion<string>().HasMaxLength(24);
            evenement.Property(e => e.Detail).HasColumnType("jsonb");
            evenement.HasIndex(e => new { e.OrganisationId, e.PlanId, e.Id });
            evenement.HasOne<PlanPreventif>().WithMany()
                .HasForeignKey(e => new { e.PlanId, e.OrganisationId })
                .HasPrincipalKey(p => new { p.Id, p.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // Exécution (S12-03) : occurrences matérialisées, contrôles réalisés et leurs photos.
    private static void ConfigurerExecution(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OccurrencePreventive>(occurrence =>
        {
            occurrence.ToTable("occurrences_preventives", table =>
            {
                table.HasCheckConstraint(
                    "ck_occurrences_preventives_statut",
                    "(statut = 'Affectee' AND (intervenant_compte_id IS NOT NULL OR prestataire IS NOT NULL)) " +
                    "OR (statut = 'Sautee' AND sautee_le IS NOT NULL AND motif_saut IS NOT NULL) " +
                    "OR (statut IN ('Realisee', 'RealiseeAvecReserves') AND realisee_le IS NOT NULL AND controle_id IS NOT NULL)");
                table.HasCheckConstraint("ck_occurrences_preventives_prestataire", "(prestataire IS NULL) = (referent_compte_id IS NULL)");
            });
            occurrence.HasKey(o => o.Id);
            occurrence.Property(o => o.Id).ValueGeneratedNever();
            occurrence.Property(o => o.Statut).HasConversion<string>().HasMaxLength(24);
            occurrence.Property(o => o.Prestataire).HasMaxLength(Intervention.PrestataireMax);
            occurrence.Property(o => o.MotifSaut).HasConversion<string>().HasMaxLength(16);
            occurrence.Property(o => o.CommentaireSaut).HasMaxLength(Signalement.CommentaireMax);
            occurrence.Property<uint>("xmin").IsRowVersion();
            occurrence.HasAlternateKey(o => new { o.Id, o.OrganisationId });
            // Une occurrence par plan et échéance prévue : un rejeu ou deux saisies simultanées n'en créent jamais deux.
            occurrence.HasIndex(o => new { o.OrganisationId, o.PlanId, o.Echeance }).IsUnique().HasDatabaseName(OccurrencePreventive.ContrainteEcheance);
            occurrence.HasIndex(o => new { o.OrganisationId, o.SiteId, o.Statut });
            occurrence.HasOne<PlanPreventif>().WithMany()
                .HasForeignKey(o => new { o.PlanId, o.OrganisationId })
                .HasPrincipalKey(p => new { p.Id, p.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ControlePreventif>(controle =>
        {
            controle.ToTable("controles_preventifs", table =>
            {
                table.HasCheckConstraint("ck_controles_preventifs_intervenant", "(intervenant_compte_id IS NULL) <> (prestataire IS NULL)");
                // Rapport PDF (S12-04) : complet ou absent, 10 Mo au plus.
                table.HasCheckConstraint(
                    "ck_controles_preventifs_rapport",
                    "(rapport_cle IS NULL) = (rapport_taille IS NULL) AND (rapport_cle IS NULL) = (rapport_empreinte IS NULL) " +
                    $"AND (rapport_taille IS NULL OR rapport_taille BETWEEN 1 AND {SharedKernel.Photos.FormulaireAvecPhotos.DocumentMax})");
            });
            controle.Property(c => c.Observations).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
            controle.Property(c => c.RapportCle).HasMaxLength(PhotoControle.CleMax);
            controle.Property(c => c.RapportEmpreinte).HasMaxLength(64);
            controle.HasKey(c => c.Id);
            controle.Property(c => c.Id).ValueGeneratedNever();
            controle.Property(c => c.Nature).HasConversion<string>().HasMaxLength(24);
            controle.Property(c => c.CorpsEtat).HasMaxLength(32);
            controle.Property(c => c.Prestataire).HasMaxLength(Intervention.PrestataireMax);
            controle.Property(c => c.NumeroAgrement).HasMaxLength(ControlePreventif.AgrementMax);
            controle.Property(c => c.Resultat).HasConversion<string>().HasMaxLength(24);
            controle.Property(c => c.CompteRendu).HasMaxLength(ControlePreventif.CompteRenduMax);
            controle.Property(c => c.Points).HasColumnType("jsonb");
            controle.Property(c => c.Mesures).HasColumnType("jsonb");
            controle.Property(c => c.Note).HasPrecision(3, 1);
            controle.Property(c => c.LibelleNote).HasMaxLength(16);
            controle.HasAlternateKey(c => new { c.Id, c.OrganisationId });
            controle.HasIndex(c => new { c.OrganisationId, c.CommandeId }).IsUnique().HasDatabaseName(ControlePreventif.ContrainteCommande);
            controle.HasIndex(c => new { c.OrganisationId, c.OccurrenceId }).IsUnique();
            controle.HasIndex(c => new { c.OrganisationId, c.SiteId, c.RealiseLe });
            controle.HasOne<OccurrencePreventive>().WithMany()
                .HasForeignKey(c => new { c.OccurrenceId, c.OrganisationId })
                .HasPrincipalKey(o => new { o.Id, o.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PhotoControle>(photo =>
        {
            photo.ToTable("photos_controles", table => table.HasCheckConstraint("ck_photos_controles_rang", "rang BETWEEN 1 AND 3"));
            photo.HasKey(p => p.Id);
            photo.Property(p => p.Id).ValueGeneratedNever();
            photo.Property(p => p.Cle).HasMaxLength(PhotoControle.CleMax);
            photo.HasIndex(p => new { p.ControleId, p.OrganisationId, p.Rang }).IsUnique();
            photo.HasOne<ControlePreventif>().WithMany()
                .HasForeignKey(p => new { p.ControleId, p.OrganisationId })
                .HasPrincipalKey(c => new { c.Id, c.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

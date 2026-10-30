using GestionSite.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Persistence;

/// <summary>Modèle du préventif (US-12) : plans, propositions écartées et journal des plans.</summary>
internal sealed partial class MaintenanceDbContext
{
    private static void ConfigurerPreventif(ModelBuilder modelBuilder)
    {
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
            plan.HasAlternateKey(p => new { p.Id, p.OrganisationId });
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
}

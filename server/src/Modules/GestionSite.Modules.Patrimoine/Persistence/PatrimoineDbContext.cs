using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Persistence;

/// <summary>
/// Données du module Patrimoine, schéma « patrimoine ». Barrières communes (ContexteOrganisation, ADR-10) et RLS forcée
/// en base (migrations).
/// </summary>
internal sealed class PatrimoineDbContext(DbContextOptions<PatrimoineDbContext> options, OrganisationCourante organisationCourante)
    : ContexteOrganisation(options, organisationCourante)
{
    internal const string Schema = "patrimoine";
    internal const string MigrationsTable = "__ef_migrations";

    public DbSet<Batiment> Batiments => Set<Batiment>();

    public DbSet<Niveau> Niveaux => Set<Niveau>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Composant> Composants => Set<Composant>();

    public DbSet<EvenementInventaire> Journal => Set<EvenementInventaire>();

    protected override void ConfigurerModele(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Batiment>(batiment =>
        {
            batiment.ToTable("batiments");
            batiment.HasKey(b => b.Id);
            batiment.Property(b => b.Id).ValueGeneratedNever();
            batiment.Property(b => b.Nom).HasMaxLength(Nom.Max);
            batiment.HasAlternateKey(b => new { b.Id, b.OrganisationId });
            batiment.HasIndex(b => new { b.OrganisationId, b.SiteId, b.Nom }).IsUnique().HasDatabaseName(ContraintesNom.Batiment);
            // Renommages concurrents détectés (xmin) : le journal garde la vraie valeur « avant ».
            batiment.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<Niveau>(niveau =>
        {
            niveau.HasKey(n => n.Id);
            niveau.Property(n => n.Id).ValueGeneratedNever();
            niveau.Property(n => n.Nom).HasMaxLength(Nom.Max);
            niveau.HasAlternateKey(n => new { n.Id, n.OrganisationId });
            niveau.HasIndex(n => new { n.BatimentId, n.OrganisationId, n.Nom }).IsUnique().HasDatabaseName(ContraintesNom.Niveau);
            niveau.ToTable("niveaux", table => table.HasCheckConstraint("ck_niveaux_ordre", "ordre BETWEEN -10 AND 100"));
            niveau.Property<uint>("xmin").IsRowVersion();
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
            // Même nom permis sur deux niveaux (« Sanitaires » au rez-de-chaussée et à l'étage), pas sur le même.
            zone.HasIndex(z => new { z.BatimentId, z.OrganisationId, z.NiveauId, z.Nom }).IsUnique().AreNullsDistinct(false)
                .HasDatabaseName(ContraintesNom.Zone);
            zone.Property<uint>("xmin").IsRowVersion();
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

        modelBuilder.Entity<Composant>(composant =>
        {
            composant.ToTable("composants", table =>
            {
                // Mêmes règles que le domaine, tenues aussi en base : lot complet ou absent, répartition complète égale à la quantité.
                table.HasCheckConstraint("ck_composants_lot", $"(unite IS NULL) = (quantite IS NULL) AND (quantite IS NULL OR quantite BETWEEN 1 AND {Composant.QuantiteMax})");
                table.HasCheckConstraint(
                    "ck_composants_repartition",
                    "(quantite_bon IS NULL AND quantite_satisfaisant IS NULL AND quantite_degrade IS NULL AND quantite_mauvais IS NULL AND quantite_critique IS NULL) " +
                    "OR (quantite IS NOT NULL AND quantite_bon >= 0 AND quantite_satisfaisant >= 0 AND quantite_degrade >= 0 AND quantite_mauvais >= 0 " +
                    "AND quantite_critique >= 0 AND quantite_bon + quantite_satisfaisant + quantite_degrade + quantite_mauvais + quantite_critique = quantite)");
                table.HasCheckConstraint(
                    "ck_composants_mise_en_service",
                    "(mois_mise_en_service IS NULL OR (annee_mise_en_service IS NOT NULL AND mois_mise_en_service BETWEEN 1 AND 12))");
                table.HasCheckConstraint("ck_composants_retrait", "(retire_le IS NULL) = (motif_retrait IS NULL)");
            });
            composant.HasKey(c => c.Id);
            composant.Property(c => c.Id).ValueGeneratedNever();
            composant.Property(c => c.Nom).HasMaxLength(Nom.Max);
            composant.Property(c => c.CorpsEtat).HasConversion<string>().HasMaxLength(32);
            composant.Property(c => c.Unite).HasConversion<string>().HasMaxLength(16);
            composant.Property(c => c.Emplacement).HasMaxLength(Composant.EmplacementMax);
            composant.Property(c => c.Marque).HasMaxLength(Composant.TexteCourtMax);
            composant.Property(c => c.Modele).HasMaxLength(Composant.TexteCourtMax);
            composant.Property(c => c.NumeroSerie).HasMaxLength(Composant.TexteCourtMax);
            composant.Property(c => c.Remarques).HasMaxLength(Composant.RemarquesMax);
            composant.Property(c => c.MotifRetrait).HasMaxLength(Composant.MotifMax);
            composant.Property<uint>("xmin").IsRowVersion();
            composant.HasIndex(c => new { c.OrganisationId, c.SiteId, c.ZoneId });
            composant.HasOne<Zone>().WithMany()
                .HasForeignKey(c => new { c.ZoneId, c.OrganisationId })
                .HasPrincipalKey(z => new { z.Id, z.OrganisationId })
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
    }
}

/// <summary>Index d'unicité des noms : seule leur violation se traduit en 409 « nom_existant ».</summary>
internal static class ContraintesNom
{
    internal const string Batiment = "ix_batiments_nom_par_site";
    internal const string Niveau = "ix_niveaux_nom_par_batiment";
    internal const string Zone = "ix_zones_nom_par_niveau";

    internal static bool Contient(string? contrainte) => contrainte is Batiment or Niveau or Zone;
}

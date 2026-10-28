using GestionSite.Modules.Identite.Domain;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Données du module Identité, dans le schéma PostgreSQL « identite ». Noms de tables, colonnes
/// et contraintes en snake_case par convention (IdentitePersistence).
/// Première barrière d'isolation (ADR-10) : filtre automatique sur l'organisation courante et
/// refus d'écrire pour une autre organisation. La seconde est la RLS PostgreSQL (migration).
/// </summary>
internal sealed class IdentiteDbContext(DbContextOptions<IdentiteDbContext> options, OrganisationCourante organisationCourante)
    : DbContext(options)
{
    internal const string Schema = "identite";
    internal const string MigrationsTable = "__ef_migrations";

    public DbSet<Organisation> Organisations => Set<Organisation>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Membre> Membres => Set<Membre>();

    public DbSet<RoleAttribue> RolesAttribues => Set<RoleAttribue>();

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
        modelBuilder.Entity<Organisation>(organisation =>
        {
            organisation.HasKey(o => o.Id);
            organisation.Property(o => o.Id).ValueGeneratedNever();
            organisation.Property(o => o.Nom).HasMaxLength(Organisation.NomMaxLength).IsRequired();
            organisation.Property(o => o.CreeLe).IsRequired();
            organisation.HasQueryFilter(o => o.Id == OrganisationId);
        });

        modelBuilder.Entity<Site>(site =>
        {
            site.HasKey(s => s.Id);
            site.Property(s => s.Id).ValueGeneratedNever();
            site.Property(s => s.Nom).HasMaxLength(Site.NomMaxLength).IsRequired();
            // Clé (id, organisation) : les rôles ne peuvent viser qu'un site de leur propre organisation.
            site.HasAlternateKey(s => new { s.Id, s.OrganisationId });
            site.HasOne<Organisation>().WithMany().HasForeignKey(s => s.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Membre>(membre =>
        {
            membre.HasKey(m => m.Id);
            membre.Property(m => m.Id).ValueGeneratedNever();
            membre.Property(m => m.Version).IsConcurrencyToken();
            membre.HasAlternateKey(m => new { m.Id, m.OrganisationId });
            // Une adhésion active par compte et organisation ; les révoquées restent en historique.
            membre.HasIndex(m => new { m.OrganisationId, m.CompteId }).IsUnique().HasFilter("revoque_le IS NULL");
            membre.HasOne<Organisation>().WithMany().HasForeignKey(m => m.OrganisationId).OnDelete(DeleteBehavior.Restrict);
            membre.HasMany(m => m.Roles).WithOne()
                .HasForeignKey(r => new { r.MembreId, r.OrganisationId })
                .HasPrincipalKey(m => new { m.Id, m.OrganisationId })
                .OnDelete(DeleteBehavior.Cascade);
            membre.Navigation(m => m.Roles).HasField("_roles").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<RoleAttribue>(role =>
        {
            role.ToTable("roles_attribues", table => table.HasCheckConstraint(
                "ck_roles_attribues_site_selon_role",
                "(role = 'Administrateur') = (site_id IS NULL)"));
            role.HasKey(r => r.Id);
            role.Property(r => r.Id).ValueGeneratedNever();
            role.Property(r => r.Role).HasConversion<string>().HasMaxLength(32);
            // Préfixe (membre, organisation) : sert aussi la clé étrangère composite, sans index en double.
            role.HasIndex(r => new { r.MembreId, r.OrganisationId, r.Role, r.SiteId }).IsUnique().AreNullsDistinct(false);
            role.HasOne<Site>().WithMany()
                .HasForeignKey(r => new { r.SiteId, r.OrganisationId })
                .HasPrincipalKey(s => new { s.Id, s.OrganisationId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        var filtrer = typeof(IdentiteDbContext).GetMethod(nameof(FiltrerParOrganisation), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var type in modelBuilder.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => typeof(IAppartientAOrganisation).IsAssignableFrom(t)).ToList())
        {
            filtrer.MakeGenericMethod(type).Invoke(this, [modelBuilder]);
        }
    }

    // Filtre posé sur chaque entité marquée IAppartientAOrganisation : une nouvelle entité marquée
    // est filtrée sans y penser ; une entité non marquée est refusée à l'écriture (ExigerOrganisationCourante).
    private void FiltrerParOrganisation<TEntite>(ModelBuilder modelBuilder)
        where TEntite : class, IAppartientAOrganisation =>
        modelBuilder.Entity<TEntite>().HasQueryFilter(e => e.OrganisationId == OrganisationId);

    // Double barrière : la RLS refuserait aussi ces écritures, mais l'erreur applicative est plus claire.
    private void ExigerOrganisationCourante()
    {
        // Toute écriture dans une transaction, même d'une seule instruction : l'organisation y est posée
        // à l'ouverture (OrganisationTransactionInterceptor) ; un préfixe décalerait les résultats lus par EF.
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        foreach (var entree in ChangeTracker.Entries())
        {
            if (entree.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var organisation = entree.Entity switch
            {
                Organisation o => o.Id,
                IAppartientAOrganisation donnee => donnee.OrganisationId,
                _ => throw new InvalidOperationException(
                    $"{entree.Metadata.ClrType.Name} n'est rattachée à aucune organisation : marquer IAppartientAOrganisation."),
            };
            if (organisation != OrganisationId)
            {
                throw new InvalidOperationException("Écriture refusée : la donnée appartient à une autre organisation que celle de la requête.");
            }
        }
    }
}

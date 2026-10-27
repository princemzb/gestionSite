using GestionSite.Modules.Identite.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>Données du module Identité, dans le schéma PostgreSQL « identite ».</summary>
internal sealed class IdentiteDbContext(DbContextOptions<IdentiteDbContext> options) : DbContext(options)
{
    internal const string Schema = "identite";
    internal const string MigrationsTable = "__ef_migrations";

    public DbSet<Organisation> Organisations => Set<Organisation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Organisation>(organisation =>
        {
            organisation.ToTable("organisations");
            organisation.HasKey(o => o.Id);
            organisation.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
            organisation.Property(o => o.Nom).HasColumnName("nom").HasMaxLength(200).IsRequired();
            organisation.Property(o => o.CreeLe).HasColumnName("cree_le").IsRequired();
        });
    }
}

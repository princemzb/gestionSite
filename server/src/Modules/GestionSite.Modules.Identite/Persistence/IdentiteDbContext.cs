using GestionSite.Modules.Identite.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Données du module Identité, dans le schéma PostgreSQL « identite ». Noms de tables, colonnes
/// et contraintes en snake_case par convention (IdentitePersistence).
/// </summary>
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
            organisation.HasKey(o => o.Id);
            organisation.Property(o => o.Id).ValueGeneratedNever();
            organisation.Property(o => o.Nom).HasMaxLength(Organisation.NomMaxLength).IsRequired();
            organisation.Property(o => o.CreeLe).IsRequired();
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Utilisée uniquement par `dotnet ef` pour générer les migrations : aucune connexion n'est ouverte.
/// </summary>
internal sealed class IdentiteDesignTimeFactory : IDesignTimeDbContextFactory<IdentiteDbContext>
{
    public IdentiteDbContext CreateDbContext(string[] args) =>
        new(IdentitePersistence.Configure(new DbContextOptionsBuilder<IdentiteDbContext>(), "Host=localhost").Options);
}

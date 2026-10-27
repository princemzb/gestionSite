using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Utilisée par `dotnet ef` (et par les tests du modèle). Connexion : variable d'environnement
/// ConnectionStrings__Main, sinon la base locale Docker (valeurs non sensibles de server/compose.yaml).
/// Générer ou afficher une migration n'ouvre aucune connexion.
/// </summary>
internal sealed class IdentiteDesignTimeFactory : IDesignTimeDbContextFactory<IdentiteDbContext>
{
    private const string LocalDevelopment =
        "Host=localhost;Port=55433;Database=gestionsite;Username=gestionsite;Password=local-dev-password";

    public IdentiteDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Main") ?? LocalDevelopment;
        var options = new DbContextOptionsBuilder<IdentiteDbContext>();
        IdentitePersistence.Configure(options, NpgsqlDataSource.Create(connectionString));
        return new IdentiteDbContext(options.Options);
    }
}

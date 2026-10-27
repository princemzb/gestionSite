using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Utilisée par `dotnet ef` (et par les tests du modèle). Connexion : variable d'environnement
/// ConnectionStrings__Main, sinon la base locale Docker (valeurs non sensibles de server/compose.yaml,
/// à garder alignées avec appsettings.Development.json ; port changé : définir ConnectionStrings__Main).
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
        IdentitePersistence.Configure(options, connectionString);
        return new IdentiteDbContext(options.Options);
    }
}

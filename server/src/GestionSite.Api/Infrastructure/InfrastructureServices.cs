using Amazon.S3;
using GestionSite.SharedKernel;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GestionSite.Api.Infrastructure;

/// <summary>Enregistre la base de données, le stockage de fichiers et leurs contrôles de disponibilité.</summary>
internal static class InfrastructureServices
{
    internal const string ReadyTag = "ready";
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    internal static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.Section))
            .ValidateDataAnnotations()
            .Validate(options => CanBuildDataSource(options.Main), "ConnectionStrings:Main n'est pas une chaîne de connexion PostgreSQL valide.")
            .Validate(
                options => !CanBuildDataSource(options.Main) || KeepsSessionState(options.Main),
                "ConnectionStrings:Main doit être une connexion directe (ni multiplexage ni pooler « -pooler ») : l'isolation RLS repose sur une variable de session.")
            .Validate(
                options => string.IsNullOrEmpty(options.Migrations) || CanBuildDataSource(options.Migrations),
                "ConnectionStrings:Migrations n'est pas une chaîne de connexion PostgreSQL valide.")
            .ValidateOnStart();
        services.AddSingleton(provider => BuildDataSource(provider, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Main));
        // Résolue seulement par DatabaseMigrations, après son contrôle de présence de la chaîne.
        services.AddKeyedSingleton(DatabaseConnections.Migrations, (provider, _) =>
            BuildDataSource(provider, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Migrations!));
        services.AddScoped<OrganisationCourante>();

        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.Section))
            .ValidateDataAnnotations()
            .Validate(options => IsHttpUrl(options.ServiceUrl), "ObjectStorage:ServiceUrl doit être une adresse http ou https.")
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var storage = provider.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
            return ObjectStorageClientFactory.Create(storage.ServiceUrl, storage.AccessKey, storage.SecretKey, storage.Region);
        });

        services.AddOptions<MigrationOptions>()
            .Bind(configuration.GetSection(MigrationOptions.Section))
            .ValidateOnStart();
        services.AddHostedService<DatabaseMigrations>();

        // Délai borné : une dépendance muette doit donner un 503 rapide, pas une sonde bloquée.
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgresql", tags: [ReadyTag], timeout: CheckTimeout)
            .AddCheck<ObjectStorageHealthCheck>("stockage-fichiers", tags: [ReadyTag], timeout: CheckTimeout);
        return services;
    }

    private static NpgsqlDataSource BuildDataSource(IServiceProvider provider, string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString)
            .UseLoggerFactory(provider.GetRequiredService<ILoggerFactory>())
            .Build();

    /// <summary>Mêmes contrôles que la construction réelle (hôte obligatoire, etc.), sans ouvrir de connexion.</summary>
    private static bool CanBuildDataSource(string value)
    {
        try
        {
            using var dataSource = new NpgsqlDataSourceBuilder(value).Build();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Pooler en mode transaction (Neon « -pooler », PgBouncer) ou multiplexage : la variable de session
    // posée à l'ouverture pourrait viser une autre session serveur que la requête.
    private static bool KeepsSessionState(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return !builder.Multiplexing
            && !(builder.Host ?? string.Empty).Contains("-pooler", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

using Amazon.S3;
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
            .Validate(options => IsValidConnectionString(options.Main), "ConnectionStrings:Main n'est pas une chaîne de connexion PostgreSQL valide.")
            .ValidateOnStart();
        services.AddSingleton(provider => new NpgsqlDataSourceBuilder(
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Main)
            .UseLoggerFactory(provider.GetRequiredService<ILoggerFactory>())
            .Build());

        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var storage = provider.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
            return ObjectStorageClientFactory.Create(storage.ServiceUrl, storage.AccessKey, storage.SecretKey, storage.Region);
        });

        // Délai borné : une dépendance muette doit donner un 503 rapide, pas une sonde bloquée.
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgresql", tags: [ReadyTag], timeout: CheckTimeout)
            .AddCheck<ObjectStorageHealthCheck>("stockage-fichiers", tags: [ReadyTag], timeout: CheckTimeout);
        return services;
    }

    private static bool IsValidConnectionString(string value)
    {
        try
        {
            _ = new NpgsqlConnectionStringBuilder(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

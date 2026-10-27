using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GestionSite.Api.Infrastructure;

/// <summary>Enregistre la base de données, le stockage de fichiers et leurs contrôles de disponibilité.</summary>
internal static class InfrastructureServices
{
    internal const string ReadyTag = "ready";

    internal static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Main");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Chaîne de connexion « ConnectionStrings:Main » absente.");
        }

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var storage = provider.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
            var config = new AmazonS3Config
            {
                ServiceURL = storage.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = storage.Region,
            };
            return new AmazonS3Client(new BasicAWSCredentials(storage.AccessKey, storage.SecretKey), config);
        });

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgresql", tags: [ReadyTag])
            .AddCheck<ObjectStorageHealthCheck>("stockage-fichiers", tags: [ReadyTag]);
        return services;
    }
}

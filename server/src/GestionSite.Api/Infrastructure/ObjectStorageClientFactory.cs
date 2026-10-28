using Amazon.Runtime;
using Amazon.S3;

namespace GestionSite.Api.Infrastructure;

/// <summary>Construction unique du client S3, partagée par l'application et les tests.</summary>
public static class ObjectStorageClientFactory
{
    public static AmazonS3Client Create(string serviceUrl, string accessKey, string secretKey, string region) =>
        new(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = region,
            // Stockages compatibles S3 (R2, SeaweedFS) : pas d'envoi « aws-chunked » avec sommes de contrôle,
            // qu'ils peuvent enregistrer tel quel et corrompre le contenu.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
}

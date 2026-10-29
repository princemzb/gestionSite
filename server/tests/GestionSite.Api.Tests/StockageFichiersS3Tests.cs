using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GestionSite.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Tests;

/// <summary>Écriture des fichiers dans le stockage S3 : compatible R2, qui refuse l'envoi « aws-chunked ».</summary>
public sealed class StockageFichiersS3Tests
{
    [Fact]
    public async Task FilesAreSentInOneSignedBodyBecauseR2RejectsChunkedUploads()
    {
        using var client = new ClientCapturant();
        var stockage = new StockageFichiersS3(client, Options.Create(new ObjectStorageOptions
        {
            ServiceUrl = "http://localhost:8333",
            Bucket = "essai",
            AccessKey = "cle",
            SecretKey = "secret",
        }));

        await stockage.EcrireAsync("organisations/x/photo.jpg", [1, 2, 3], "image/jpeg", CancellationToken.None);

        var envoi = Assert.Single(client.Envois);
        Assert.False(envoi.UseChunkEncoding);
        Assert.Equal(("essai", "organisations/x/photo.jpg", "image/jpeg"), (envoi.BucketName, envoi.Key, envoi.ContentType));
    }

    private sealed class ClientCapturant() : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { ServiceURL = "http://localhost:8333" })
    {
        internal List<PutObjectRequest> Envois { get; } = [];

        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Envois.Add(request);
            return Task.FromResult(new PutObjectResponse());
        }
    }
}

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using GestionSite.SharedKernel;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Infrastructure;

/// <summary>Fichiers dans le compartiment privé configuré (ObjectStorage:Bucket) : SeaweedFS en local, R2 ailleurs.</summary>
internal sealed class StockageFichiersS3(IAmazonS3 client, IOptions<ObjectStorageOptions> options) : IStockageFichiers
{
    public async Task EcrireAsync(string cle, byte[] contenu, string typeContenu, CancellationToken cancellationToken)
    {
        using var flux = new MemoryStream(contenu, writable: false);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Value.Bucket,
            Key = cle,
            InputStream = flux,
            ContentType = typeContenu,
            AutoCloseStream = false,
            // R2 refuse l'envoi « aws-chunked » (STREAMING-AWS4-HMAC-SHA256-PAYLOAD not implemented) : corps signé d'un bloc,
            // comme pour les clés de protection du web (DepotClesS3).
            UseChunkEncoding = false,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(byte[] Contenu, string TypeContenu)?> LireAsync(string cle, CancellationToken cancellationToken)
    {
        try
        {
            using var reponse = await client.GetObjectAsync(options.Value.Bucket, cle, cancellationToken).ConfigureAwait(false);
            using var copie = new MemoryStream();
            await reponse.ResponseStream.CopyToAsync(copie, cancellationToken).ConfigureAwait(false);
            return (copie.ToArray(), reponse.Headers.ContentType);
        }
        catch (AmazonS3Exception absent) when (absent.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}

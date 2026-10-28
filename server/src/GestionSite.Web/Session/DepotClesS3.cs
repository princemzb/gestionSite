using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace GestionSite.Web.Session;

/// <summary>
/// Clés de chiffrement des cookies (Data Protection) conservées dans un stockage compatible S3 (R2 en
/// préproduction) : le disque des conteneurs est éphémère, et sans clés conservées chaque réveil
/// déconnecterait tout le monde. Compartiment privé ; une clé par objet sous un préfixe dédié.
/// L'interface de Data Protection est synchrone : appels rares (démarrage, rotation des clés).
/// </summary>
internal sealed class DepotClesS3(IAmazonS3 client, string compartiment, string prefixe) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var elements = new List<XElement>();
        string? suite = null;
        do
        {
            var page = client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = compartiment, Prefix = prefixe, ContinuationToken = suite })
                .GetAwaiter().GetResult();
            // Seuls les fichiers de clés : certains stockages listent aussi une entrée vide pour le « dossier ».
            foreach (var objet in (page.S3Objects ?? []).Where(o => o.Key.EndsWith(".xml", StringComparison.Ordinal) && o.Size > 0))
            {
                using var reponse = client.GetObjectAsync(compartiment, objet.Key).GetAwaiter().GetResult();
                elements.Add(XElement.Load(reponse.ResponseStream));
            }

            suite = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (suite is not null);

        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = compartiment,
            Key = $"{prefixe}{friendlyName}.xml",
            ContentBody = element.ToString(SaveOptions.DisableFormatting),
            ContentType = "application/xml",
            UseChunkEncoding = false,
        }).GetAwaiter().GetResult();
    }
}

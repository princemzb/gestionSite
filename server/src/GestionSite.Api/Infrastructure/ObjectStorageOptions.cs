using System.ComponentModel.DataAnnotations;

namespace GestionSite.Api.Infrastructure;

/// <summary>
/// Stockage de fichiers compatible S3 : SeaweedFS en local, Cloudflare R2 en préproduction (ADR-08).
/// Les clés viennent de la configuration de l'environnement, jamais du dépôt hors valeurs locales.
/// </summary>
public sealed class ObjectStorageOptions
{
    public const string Section = "ObjectStorage";

    [Required]
    [Url]
    public string ServiceUrl { get; init; } = string.Empty;

    [Required]
    public string AccessKey { get; init; } = string.Empty;

    [Required]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string Bucket { get; init; } = string.Empty;

    /// <summary>Région de signature ; « auto » pour R2.</summary>
    [Required]
    public string Region { get; init; } = "us-east-1";
}

namespace GestionSite.SharedKernel;

/// <summary>
/// Stockage privé des fichiers (photos, justificatifs) : compatible S3 en local et en préproduction (ADR-08).
/// Les clés sont construites par les modules, toujours préfixées par l'organisation.
/// </summary>
public interface IStockageFichiers
{
    /// <summary>Écrit (ou remplace) le fichier : une même clé réécrite avec le même contenu est sans effet visible.</summary>
    Task EcrireAsync(string cle, ReadOnlyMemory<byte> contenu, string typeContenu, CancellationToken cancellationToken);

    /// <summary>Contenu et type du fichier, ou null s'il n'existe pas.</summary>
    Task<(byte[] Contenu, string TypeContenu)?> LireAsync(string cle, CancellationToken cancellationToken);
}

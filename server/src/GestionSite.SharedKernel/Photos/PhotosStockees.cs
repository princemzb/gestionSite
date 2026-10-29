namespace GestionSite.SharedKernel.Photos;

/// <summary>
/// Écriture des photos préparées (JPEG réencodés) avant l'enregistrement en base, sous la clé de chaque ligne créée :
/// dépôt public, déclaration interne, clôture d'intervention. Un échec ensuite laisse au pire des fichiers orphelins,
/// jamais ceux d'un autre envoi (clés propres à chaque photo enregistrée).
/// </summary>
public static class PhotosStockees
{
    public static async Task<IReadOnlyList<T>> EcrireAsync<T>(
        IStockageFichiers stockage, IReadOnlyList<byte[]> jpegs, Func<int, byte[], T> creer, Func<T, string> cle, CancellationToken cancellationToken)
    {
        var photos = jpegs.Select((jpeg, i) => creer(i + 1, jpeg)).ToList();
        await Task.WhenAll(photos.Select((photo, i) => stockage.EcrireAsync(cle(photo), jpegs[i], TraitementPhotos.TypeContenu, cancellationToken)))
            .ConfigureAwait(false);
        return photos;
    }
}

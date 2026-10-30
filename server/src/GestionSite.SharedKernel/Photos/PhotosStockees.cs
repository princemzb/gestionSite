namespace GestionSite.SharedKernel.Photos;

/// <summary>
/// Écriture des photos préparées (JPEG réencodés) avant l'enregistrement en base, sous la clé de chaque ligne créée :
/// dépôt public, déclaration interne, clôture d'intervention, évaluation. Les clés sont propres à chaque photo : un envoi
/// ne remplace jamais les fichiers d'un autre.
/// </summary>
public static class PhotosStockees
{
    /// <summary>
    /// Écrit les photos ; une écriture partielle est retirée avant l'erreur. L'envoi rendu les supprime à sa fin s'il n'a
    /// pas été confirmé (base en échec, envoi concurrent perdant, rejeu) : aucun fichier orphelin.
    /// </summary>
    public static async Task<EnvoiPhotos<T>> EcrireAsync<T>(
        IStockageFichiers stockage, IReadOnlyList<byte[]> jpegs, Func<int, byte[], T> creer, Func<T, string> cle, CancellationToken cancellationToken)
    {
        var photos = jpegs.Select((jpeg, i) => creer(i + 1, jpeg)).ToList();
        var envoi = new EnvoiPhotos<T>(stockage, photos, [.. photos.Select(cle)]);
        try
        {
            await Task.WhenAll(photos.Select((photo, i) => stockage.EcrireAsync(cle(photo), jpegs[i], TraitementPhotos.TypeContenu, cancellationToken)))
                .ConfigureAwait(false);
        }
        catch
        {
            await envoi.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return envoi;
    }
}

/// <summary>Photos écrites pour un envoi, supprimées à la fin de l'envoi sauf confirmation après l'enregistrement en base.</summary>
public sealed class EnvoiPhotos<T> : IAsyncDisposable
{
    private readonly IStockageFichiers _stockage;
    private readonly IReadOnlyList<string> _cles;
    private bool _confirme;

    internal EnvoiPhotos(IStockageFichiers stockage, IReadOnlyList<T> photos, IReadOnlyList<string> cles)
    {
        _stockage = stockage;
        _cles = cles;
        Photos = photos;
    }

    public IReadOnlyList<T> Photos { get; }

    /// <summary>Les photos appartiennent désormais à une ligne enregistrée : elles sont gardées.</summary>
    public void Confirmer() => _confirme = true;

    public async ValueTask DisposeAsync()
    {
        if (_confirme)
        {
            return;
        }

        _confirme = true;
        // Nettoyage au mieux, hors annulation de la requête : un fichier non supprimé ne doit pas masquer l'erreur d'origine.
        foreach (var cle in _cles)
        {
            try
            {
                await _stockage.SupprimerAsync(cle, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Fichier laissé au pire orphelin (stockage indisponible) ; aucune donnée d'un autre envoi n'est touchée.
            }
        }
    }
}

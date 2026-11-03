namespace GestionSite.SharedKernel.Photos;

/// <summary>
/// Écriture des photos préparées (JPEG réencodés) avant l'enregistrement en base, sous la clé de chaque ligne créée :
/// dépôt public, déclaration interne, clôture d'intervention, évaluation. Les clés sont propres à chaque photo : un envoi
/// ne remplace jamais les fichiers d'un autre.
/// </summary>
public static class PhotosStockees
{
    /// <summary>
    /// Écrit les photos ; une écriture partielle est retirée avant l'erreur. L'envoi rendu retire ses fichiers à sa fin s'il
    /// n'a pas été confirmé et que la base confirme ne pas les porter (<paramref name="enregistrees"/>) : échec de la base,
    /// envoi concurrent perdant, rejeu. En cas de doute (base injoignable), les fichiers sont gardés : jamais une photo
    /// d'une ligne enregistrée supprimée.
    /// </summary>
    public static Task<EnvoiPhotos<T>> EcrireAsync<T>(
        IStockageFichiers stockage, IReadOnlyList<byte[]> jpegs, Func<int, byte[], T> creer, Func<T, string> cle,
        Func<IReadOnlyList<T>, Task<bool>> enregistrees, CancellationToken cancellationToken) =>
        EcrireAsync(stockage, jpegs, TraitementPhotos.TypeContenu, creer, cle, enregistrees, cancellationToken);

    /// <summary>Même écriture pour des fichiers d'un autre type (rapport PDF d'une vérification, S12-04).</summary>
    public static async Task<EnvoiPhotos<T>> EcrireAsync<T>(
        IStockageFichiers stockage, IReadOnlyList<byte[]> jpegs, string typeContenu, Func<int, byte[], T> creer, Func<T, string> cle,
        Func<IReadOnlyList<T>, Task<bool>> enregistrees, CancellationToken cancellationToken)
    {
        var photos = jpegs.Select((jpeg, i) => creer(i + 1, jpeg)).ToList();
        var envoi = new EnvoiPhotos<T>(stockage, photos, [.. photos.Select(cle)], enregistrees);
        try
        {
            // Chaque écriture dans sa tâche : une erreur levée d'emblée attend les autres, retirées ensuite avec elles.
            await Task.WhenAll(photos.Select(async (photo, i) =>
                await stockage.EcrireAsync(cle(photo), jpegs[i], typeContenu, cancellationToken).ConfigureAwait(false)))
                .ConfigureAwait(false);
        }
        catch
        {
            await envoi.RetirerAsync().ConfigureAwait(false);
            throw;
        }

        return envoi;
    }
}

/// <summary>Photos écrites pour un envoi, retirées à la fin de l'envoi sauf confirmation ou enregistrement constaté en base.</summary>
public sealed class EnvoiPhotos<T> : IAsyncDisposable
{
    private static readonly TimeSpan DelaiNettoyage = TimeSpan.FromSeconds(10);
    private readonly IStockageFichiers _stockage;
    private readonly IReadOnlyList<string> _cles;
    private readonly Func<IReadOnlyList<T>, Task<bool>> _enregistrees;
    private bool _termine;

    internal EnvoiPhotos(IStockageFichiers stockage, IReadOnlyList<T> photos, IReadOnlyList<string> cles, Func<IReadOnlyList<T>, Task<bool>> enregistrees)
    {
        _stockage = stockage;
        _cles = cles;
        _enregistrees = enregistrees;
        Photos = photos;
    }

    public IReadOnlyList<T> Photos { get; }

    /// <summary>Les photos appartiennent désormais à une ligne enregistrée : elles sont gardées.</summary>
    public void Confirmer() => _termine = true;

    public async ValueTask DisposeAsync()
    {
        if (_termine || Photos.Count == 0)
        {
            return;
        }

        _termine = true;
        try
        {
            if (await _enregistrees(Photos).ConfigureAwait(false))
            {
                return; // enregistrées malgré l'erreur (réponse perdue après la validation) : gardées
            }
        }
        catch (Exception)
        {
            return; // base injoignable : dans le doute, les fichiers restent (au pire orphelins, jamais perdus)
        }

        await RetirerAsync().ConfigureAwait(false);
    }

    // Nettoyage au mieux, en parallèle et borné, hors annulation de la requête : un fichier non retiré ne masque jamais
    // l'erreur d'origine et ne touche aucune donnée d'un autre envoi.
    internal async Task RetirerAsync()
    {
        _termine = true;
        using var delai = new CancellationTokenSource(DelaiNettoyage);
        try
        {
            await Task.WhenAll(_cles.Select(async cle => await _stockage.SupprimerAsync(cle, delai.Token).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Stockage indisponible : fichier laissé orphelin.
        }
    }
}

using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;

namespace GestionSite.Modules.Tests;

/// <summary>Photos écrites avant l'enregistrement en base : retirées si l'envoi n'est pas confirmé, aucun fichier orphelin.</summary>
public sealed class PhotosStockeesTests
{
    private static readonly byte[][] DeuxPhotos = [[1, 2], [3, 4]];

    [Fact]
    public async Task AnUnconfirmedUploadRemovesItsFiles()
    {
        var stockage = new StockageMemoire();

        await using (var envoi = await EcrireAsync(stockage))
        {
            Assert.Equal(2, envoi.Photos.Count);
            Assert.Equal(2, stockage.Fichiers.Count);
        }

        Assert.Empty(stockage.Fichiers);
    }

    [Fact]
    public async Task AConfirmedUploadKeepsItsFiles()
    {
        var stockage = new StockageMemoire();

        await using (var envoi = await EcrireAsync(stockage))
        {
            envoi.Confirmer();
        }

        Assert.Equal(["photo-1", "photo-2"], stockage.Fichiers.Keys.Order());
    }

    [Fact]
    public async Task FilesOfAnUploadAlreadySavedOrUncertainAreKept()
    {
        var stockage = new StockageMemoire();

        await using (await EcrireAsync(stockage, enregistrees: true))
        {
        }

        await using (await PhotosStockees.EcrireAsync(
            stockage, [[9]], (rang, _) => $"doute-{rang}", photo => photo, _ => throw new TimeoutException("base injoignable"), CancellationToken.None))
        {
        }

        Assert.Equal(["doute-1", "photo-1", "photo-2"], stockage.Fichiers.Keys.Order());
        Assert.Empty(stockage.Suppressions);
    }

    [Fact]
    public async Task APartialWriteIsRemovedBeforeTheErrorAndAFailedDeleteNeverHidesIt()
    {
        var stockage = new StockageMemoire { EchecEcriture = "photo-2", EchecSuppression = "photo-2" };

        var erreur = await Assert.ThrowsAsync<IOException>(() => EcrireAsync(stockage));

        Assert.Equal("écriture refusée", erreur.Message);
        Assert.Empty(stockage.Fichiers);
        Assert.Equal(["photo-1", "photo-2"], stockage.Suppressions.Order());
    }

    private static Task<EnvoiPhotos<string>> EcrireAsync(StockageMemoire stockage, bool enregistrees = false) =>
        PhotosStockees.EcrireAsync(stockage, DeuxPhotos, (rang, _) => $"photo-{rang}", photo => photo, _ => Task.FromResult(enregistrees), CancellationToken.None);

    private sealed class StockageMemoire : IStockageFichiers
    {
        internal Dictionary<string, byte[]> Fichiers { get; } = [];

        internal List<string> Suppressions { get; } = [];

        internal string? EchecEcriture { get; init; }

        internal string? EchecSuppression { get; init; }

        // Échec rendu comme une tâche en erreur, comme un envoi S3 refusé pendant que les autres aboutissent.
        public async Task EcrireAsync(string cle, byte[] contenu, string typeContenu, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (cle == EchecEcriture)
            {
                throw new IOException("écriture refusée");
            }

            Fichiers[cle] = contenu;
        }

        public Task<(byte[] Contenu, string TypeContenu)?> LireAsync(string cle, CancellationToken cancellationToken) =>
            Task.FromResult<(byte[], string)?>(Fichiers.TryGetValue(cle, out var contenu) ? (contenu, "image/jpeg") : null);

        public Task SupprimerAsync(string cle, CancellationToken cancellationToken)
        {
            Suppressions.Add(cle);
            if (cle == EchecSuppression)
            {
                throw new IOException("stockage indisponible");
            }

            Fichiers.Remove(cle);
            return Task.CompletedTask;
        }
    }
}

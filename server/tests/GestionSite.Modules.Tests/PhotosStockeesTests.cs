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
    public async Task APartialWriteIsRemovedBeforeTheErrorAndAFailedDeleteNeverHidesIt()
    {
        var stockage = new StockageMemoire { EchecEcriture = "photo-2", EchecSuppression = "photo-2" };

        var erreur = await Assert.ThrowsAsync<IOException>(() => EcrireAsync(stockage));

        Assert.Equal("écriture refusée", erreur.Message);
        Assert.Empty(stockage.Fichiers);
        Assert.Equal(["photo-1", "photo-2"], stockage.Suppressions.Order());
    }

    private static Task<EnvoiPhotos<string>> EcrireAsync(StockageMemoire stockage) =>
        PhotosStockees.EcrireAsync(stockage, DeuxPhotos, (rang, _) => $"photo-{rang}", photo => photo, CancellationToken.None);

    private sealed class StockageMemoire : IStockageFichiers
    {
        internal Dictionary<string, byte[]> Fichiers { get; } = [];

        internal List<string> Suppressions { get; } = [];

        internal string? EchecEcriture { get; init; }

        internal string? EchecSuppression { get; init; }

        public Task EcrireAsync(string cle, byte[] contenu, string typeContenu, CancellationToken cancellationToken)
        {
            if (cle == EchecEcriture)
            {
                throw new IOException("écriture refusée");
            }

            Fichiers[cle] = contenu;
            return Task.CompletedTask;
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

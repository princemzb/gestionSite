using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using GestionSite.Modules.Patrimoine.PlansBatiment;
using GestionSite.Modules.Patrimoine.RenduPdf;
using GestionSite.Tests;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Tests;

/// <summary>
/// Rendu d'une page de PDF (S21-05) : logique du processus enfant exécutée ici (PDFium chargé dans ce processus, pour des
/// PDF sans piège seulement), puis processus enfant réel lancé et borné par le module (délai, mémoire, abandon).
/// </summary>
public sealed class RenduPdfTests
{
    private const int Cote = 8_000;
    private const long Surface = 50_000_000;

    private static readonly byte[] TroisPages = PdfSynthetique.Pages(
        (595, 842, (1, 0, 0)), (842, 595, (0, 1, 0)), (300, 300, (0, 0, 1)));

    [Fact]
    public void TheChosenPageOfAMultiPageDocumentIsRenderedAsAPng()
    {
        var (code, entete, png) = Executer(TroisPages, page: 2);

        Assert.Equal(0, code);
        Assert.Equal("OK 3 842 595 8000 5653 " + png.Length, entete);
        using var image = new MagickImage(png);
        Assert.Equal((8_000u, 5_653u), (image.Width, image.Height));
        Assert.Equal("#00FF00", Couleur(image, 4_000, 1_000)); // page 2 : verte
        Assert.Equal("#000000", Couleur(image, 4_000, 2_826)); // trait en diagonale
    }

    [Fact]
    public void ASquarePageIsBoundedByTheSurface()
    {
        var (_, entete, png) = Executer(TroisPages, page: 3);

        Assert.StartsWith("OK 3 300 300 7071 7071 ", entete, StringComparison.Ordinal);
        using var image = new MagickImage(png);
        Assert.Equal("#0000FF", Couleur(image, 3_000, 500));
    }

    [Theory]
    [InlineData(4, "PAGE 3")]
    [InlineData(1_000_000, "PAGE 3")]
    public void AMissingPageReportsThePageCount(int page, string attendu) =>
        Assert.Equal(attendu, Executer(TroisPages, page).Entete);

    [Theory]
    [InlineData("secret")] // mot de passe d'ouverture
    [InlineData("")] // simples restrictions : s'ouvre seul, refusé quand même
    public void AnEncryptedDocumentIsRefused(string motDePasse) =>
        Assert.Equal("CHIFFRE", Executer(PdfSynthetique.Chiffre(motDePasse), page: 1).Entete);

    public static TheoryData<string, byte[]> Illisibles => new()
    {
        { "pas un PDF", "GIF89a......"u8.ToArray() },
        { "vide", [] },
        { "corrompu", PdfSynthetique.Corrompu() },
        { "sans page", PdfSynthetique.Assembler([PdfSynthetique.Texte("<< /Type /Catalog /Pages 2 0 R >>"), PdfSynthetique.Texte("<< /Type /Pages /Kids [] /Count 0 >>")]) },
        { "page sans surface", PdfSynthetique.Pages((0.5, 0.5, (1, 1, 1))) },
    };

    [Theory]
    [MemberData(nameof(Illisibles))]
    public void AnUnreadableDocumentIsRefused(string cas, byte[] contenu) =>
        Assert.True(Executer(contenu, page: 1).Entete == "ILLISIBLE", cas);

    [Fact]
    public void AnInputBeyondTheBoundIsRefusedWithoutBeingKept() =>
        Assert.Equal("ILLISIBLE", Executer([.. "%PDF-1.7\n"u8, .. new byte[RenduPage.EntreeMax]], page: 1).Entete);

    [Fact]
    public void AnImageTooLargeToAllocateIsRefused() =>
        Assert.Equal("ILLISIBLE", Executer(TroisPages, page: 1, cote: 2_000_000_000, surface: long.MaxValue).Entete);

    [Theory]
    [InlineData("1", "8000", "50000000")]
    [InlineData("0", "8000", "50000000", "1073741824")]
    [InlineData("1", "0", "50000000", "1073741824")]
    [InlineData("1", "8000", "0", "1073741824")]
    [InlineData("1", "8000", "50000000", "0")]
    [InlineData("-1", "8000", "50000000", "1073741824")]
    [InlineData("x", "8000", "50000000", "1073741824")]
    [InlineData("1", "x", "50000000", "1073741824")]
    [InlineData("1", "8000", "x", "1073741824")]
    [InlineData("1", "8000", "50000000", "x")]
    public void UnexpectedArgumentsStopBeforeReadingTheDocument(params string[] arguments)
    {
        using var entree = new MemoryStream(TroisPages);
        using var sortie = new MemoryStream();
        var limites = false;

        Assert.Equal(RenduPage.CodeUsage, RenduPage.Executer(arguments, entree, sortie, _ => limites = true));
        Assert.False(limites);
        Assert.Equal(0, entree.Position);
        Assert.Equal(0, sortie.Length);
    }

    [Theory]
    [InlineData(595, 842, 5_653, 8_000)] // A4 portrait : grand côté borné
    [InlineData(10_000, 10_000, 7_071, 7_071)] // carré : surface bornée
    [InlineData(14_400, 1, 8_000, 1)] // bandeau : au moins un pixel
    public void DimensionsAreBoundedBySideAndSurface(double largeurPt, double hauteurPt, int largeur, int hauteur) =>
        Assert.Equal((largeur, hauteur), RenduPage.Dimensions(largeurPt, hauteurPt, Cote, Surface));

    [Fact]
    public async Task TheChildProcessRendersThePageAndReportsTheDocument()
    {
        var rendue = await Processus().RendreAsync(TroisPages, 2, Cote, Surface, CancellationToken.None);

        Assert.Equal((3, 842d, 595d), (rendue.Pages, rendue.LargeurPt, rendue.HauteurPt));
        using var image = new MagickImage(rendue.Png);
        Assert.Equal((8_000u, 5_653u), (image.Width, image.Height));
    }

    [Fact]
    public async Task ChildProcessRefusalsBecomeInputRefusals()
    {
        var processus = Processus();

        var chiffre = await Assert.ThrowsAsync<PdfRefuseException>(() => processus.RendreAsync(PdfSynthetique.Chiffre("secret"), 1, Cote, Surface, CancellationToken.None));
        var page = await Assert.ThrowsAsync<PdfRefuseException>(() => processus.RendreAsync(TroisPages, 9, Cote, Surface, CancellationToken.None));
        var unePage = await Assert.ThrowsAsync<PdfRefuseException>(
            () => processus.RendreAsync(PdfSynthetique.Pages((100, 100, (1, 1, 1))), 2, Cote, Surface, CancellationToken.None));
        var corrompu = await Assert.ThrowsAsync<PdfRefuseException>(() => processus.RendreAsync(PdfSynthetique.Corrompu(), 1, Cote, Surface, CancellationToken.None));

        Assert.Equal(("pdf_protege", "fichier"), (chiffre.Code, chiffre.Champ));
        Assert.Contains("protégé", chiffre.Message, StringComparison.Ordinal);
        Assert.Equal(("page_absente", "page", (int?)3, "Page absente : ce PDF compte 3 pages."), (page.Code, page.Champ, page.Pages, page.Message));
        Assert.Equal("Page absente : ce PDF compte 1 page.", unePage.Message);
        Assert.Equal(("pdf_illisible", "fichier", (int?)null), (corrompu.Code, corrompu.Champ, corrompu.Pages));
        Assert.Contains("illisible", corrompu.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEndlessRenderingIsStoppedAtTheDeadline()
    {
        var chrono = Stopwatch.StartNew();

        var refus = await Assert.ThrowsAsync<PdfRefuseException>(
            () => Processus(delai: 2).RendreAsync(PdfSynthetique.FormulairesImbriques(), 1, Cote, Surface, CancellationToken.None));

        Assert.Equal("fichier", refus.Champ);
        Assert.Contains(refus.Code, (string[])["pdf_trop_long", "pdf_illisible"]); // délai, ou borne mémoire atteinte avant lui
        Assert.InRange(chrono.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(15));
        Assert.Empty(Enfants());
    }

    [Fact]
    public async Task AnAbandonedRequestStopsTheChildProcess()
    {
        using var abandon = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Processus().RendreAsync(PdfSynthetique.FormulairesImbriques(), 1, Cote, Surface, abandon.Token));

        Assert.Empty(Enfants());
    }

    [Fact]
    public async Task TheMemoryBoundStopsTheChildProcessOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // borne posée par le noyau Linux (RLIMIT_DATA), seule plateforme d'exécution
        }

        // 64 Mo : l'image de 45 Mpx ne peut être allouée (refus ou arrêt de l'enfant) ; le PDF de plus de 64 Ko remplit
        // aussi le tube d'entrée, que l'enfant arrêté ne lit plus.
        var gros = PdfSynthetique.Pages([.. Enumerable.Range(0, 2_000).Select(_ => (595d, 842d, (1d, 1d, 1d)))]);
        var refus = await Assert.ThrowsAsync<PdfRefuseException>(
            () => Processus(memoire: 64L * 1024 * 1024).RendreAsync(gros, 1, Cote, Surface, CancellationToken.None));

        Assert.Equal(("pdf_illisible", "fichier"), (refus.Code, refus.Champ));
        Assert.True(gros.Length > 64 * 1024);
    }

    [Fact]
    public async Task TheConfiguredLauncherReceivesOnlyTheRenderingArgumentsAndRunsTheRendering()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // lanceur isolé : image Linux du conteneur seulement (SOC-05)
        }

        // Lanceur d'essai : refuse tout autre appel que les 4 arguments numériques, puis lance le rendu directement.
        var commande = string.Join(' ', ProcessusRenduPdf.PreparerLancementDirect().Select(a => $"'{a}'"));
        using var lanceur = new LanceurEssai($"[ $# -eq 4 ] || exit 64\nexec {commande} \"$@\"");

        var rendue = await Processus(lanceur: lanceur.Chemin).RendreAsync(TroisPages, 3, 1_000, 1_000_000, CancellationToken.None);

        Assert.Equal((3, 300d, 300d), (rendue.Pages, rendue.LargeurPt, rendue.HauteurPt));
        using var image = new MagickImage(rendue.Png);
        Assert.Equal((1_000u, 1_000u), (image.Width, image.Height));
    }

    [Fact]
    public async Task ALauncherRefusalBecomesAnUnreadableDocument()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var lanceur = new LanceurEssai("echo 'lanceur-rendu-pdf : appelant (refus)' >&2\nexit 126");

        var refus = await Assert.ThrowsAsync<PdfRefuseException>(
            () => Processus(lanceur: lanceur.Chemin).RendreAsync(TroisPages, 1, Cote, Surface, CancellationToken.None));

        Assert.Equal(("pdf_illisible", "fichier"), (refus.Code, refus.Champ));
    }

    [Fact]
    public async Task AProcessKeepingTheErrorOutputOpenNeverBlocksTheRendering()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Processus échappé au rendu (hors de portée de l'API) qui garde la sortie d'erreur ouverte (sa sortie standard,
        // s'il la gardait, serait bornée par le délai du rendu).
        using var lanceur = new LanceurEssai("sleep 60 >/dev/null &\necho $! > \"$(dirname \"$0\")/echappe\"\nexit 3");
        var chrono = Stopwatch.StartNew();
        try
        {
            var refus = await Assert.ThrowsAsync<PdfRefuseException>(
                () => Processus(lanceur: lanceur.Chemin).RendreAsync(TroisPages, 1, Cote, Surface, CancellationToken.None));

            Assert.Equal("pdf_illisible", refus.Code);
            Assert.InRange(chrono.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(15));
        }
        finally
        {
            var echappe = Path.Combine(Path.GetDirectoryName(lanceur.Chemin)!, "echappe");
            if (File.Exists(echappe))
            {
                using var processus = Process.GetProcessById(int.Parse(File.ReadAllText(echappe).Trim(), CultureInfo.InvariantCulture));
                processus.Kill();
            }
        }
    }

    private static ProcessusRenduPdf Processus(int delai = 20, long memoire = 1024L * 1024 * 1024, string? lanceur = null) =>
        new(
            Options.Create(new FondsPlanOptions { RenduPdfDelaiSecondes = delai, RenduPdfMemoireOctets = memoire, RenduPdfLanceur = lanceur }),
            NullLogger<ProcessusRenduPdf>.Instance);

    // Processus de rendu encore en vie, lancés par ce processus de test.
    private static List<Process> Enfants() =>
        [.. Process.GetProcessesByName("dotnet").Where(p =>
        {
            try
            {
                return !p.HasExited && p.StartTime > DateTime.Now.AddMinutes(-1) && LigneDeCommande(p).Contains(ProcessusRenduPdf.Programme, StringComparison.Ordinal);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        })];

    private static string LigneDeCommande(Process processus) =>
        OperatingSystem.IsLinux() && File.Exists($"/proc/{processus.Id}/cmdline") ? File.ReadAllText($"/proc/{processus.Id}/cmdline") : string.Empty;

    private static (int Code, string Entete, byte[] Png) Executer(byte[] pdf, int page, int cote = Cote, long surface = Surface)
    {
        using var entree = new MemoryStream(pdf);
        using var sortie = new MemoryStream();
        long? limite = null;
        string[] arguments = [page.ToString(CultureInfo.InvariantCulture), cote.ToString(CultureInfo.InvariantCulture), surface.ToString(CultureInfo.InvariantCulture), "1073741824"];
        var code = RenduPage.Executer(arguments, entree, sortie, memoire => limite = memoire);
        Assert.Equal(1_073_741_824, limite);
        var octets = sortie.ToArray();
        var fin = Array.IndexOf(octets, (byte)'\n');
        return (code, Encoding.ASCII.GetString(octets, 0, fin), octets[(fin + 1)..]);
    }

    private static string Couleur(MagickImage image, int x, int y)
    {
        using var pixels = image.GetPixels();
        return pixels.GetPixel(x, y).ToColor()!.ToHexString();
    }

    // Lanceur d'essai : script shell exécutable dans un dossier temporaire, supprimé avec lui.
    private sealed class LanceurEssai : IDisposable
    {
        private readonly string _dossier = Directory.CreateTempSubdirectory("lanceur-rendu-pdf-").FullName;

        [UnsupportedOSPlatform("windows")]
        public LanceurEssai(string corps)
        {
            Chemin = Path.Combine(_dossier, "lanceur");
            File.WriteAllText(Chemin, $"#!/bin/sh\n{corps}\n");
            File.SetUnixFileMode(Chemin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public string Chemin { get; }

        public void Dispose() => Directory.Delete(_dossier, recursive: true);
    }
}

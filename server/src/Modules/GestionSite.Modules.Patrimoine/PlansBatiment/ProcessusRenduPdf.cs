using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Patrimoine.PlansBatiment;

/// <summary>Page de PDF rendue en PNG, avec le nombre de pages du document et la taille de la page en points (1/72 po).</summary>
internal sealed record PagePdfRendue(byte[] Png, int Pages, double LargeurPt, double HauteurPt);

/// <summary>
/// PDF refusé par le rendu (422) : code stable pour l'éditeur (« pdf_protege », « pdf_illisible », « pdf_trop_long »,
/// « page_absente »), champ, message, et nombre de pages du document pour une page absente.
/// </summary>
internal sealed class PdfRefuseException(string code, string champ, string message, int? pages = null) : Exception(message)
{
    public string Code { get; } = code;

    public string Champ { get; } = champ;

    public int? Pages { get; } = pages;
}

/// <summary>
/// Rendu d'une page de PDF (US-21, RP-21.3, S21-05) dans un processus enfant (GestionSite.Modules.Patrimoine.RenduPdf,
/// PDFium compilé sans V8 ni XFA) : un seul à la fois pour l'API, borné en temps (arrêt au-delà du délai) et en mémoire
/// (posée par le noyau dans l'enfant), sans les variables d'environnement de l'API (aucun secret transmis). Un PDF piégé
/// ne peut arrêter que l'enfant : son échec devient un refus de saisie, l'API reste debout. Dans l'image du conteneur,
/// l'enfant passe par le lanceur isolé (<see cref="FondsPlanOptions.RenduPdfLanceur"/>, SOC-05) : il tourne sous un compte
/// dédié, qui ne peut lire ni l'environnement, ni la mémoire, ni les fichiers de l'API, même si une faille de PDFium y
/// fait exécuter du code.
/// </summary>
internal sealed partial class ProcessusRenduPdf(IOptions<FondsPlanOptions> options, ILogger<ProcessusRenduPdf> journal)
{
    internal const string Programme = "GestionSite.Modules.Patrimoine.RenduPdf";
    private const int EnteteMax = 128;
    private const int ErreursMax = 4096;
    private static readonly TimeSpan ErreursAttente = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim Seul = new(1);
    private static readonly Lazy<string[]> LancementDirect = new(PreparerLancementDirect);

    /// <summary>
    /// Page <paramref name="page"/> (à partir de 1) rendue en PNG de <paramref name="cote"/> px de côté et
    /// <paramref name="surface"/> pixels au plus. Refus de saisie (champ « fichier » ou « page ») si le PDF est chiffré,
    /// illisible, trop long ou trop lourd à rendre, ou si la page n'existe pas. Attente abandonnée avec la requête.
    /// </summary>
    internal async Task<PagePdfRendue> RendreAsync(byte[] pdf, int page, uint cote, ulong surface, CancellationToken cancellationToken)
    {
        await Seul.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LancerAsync(pdf, page, cote, surface, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Seul.Release();
        }
    }

    private async Task<PagePdfRendue> LancerAsync(byte[] pdf, int page, uint cote, ulong surface, CancellationToken cancellationToken)
    {
        var reglages = options.Value;
        var lancement = reglages.RenduPdfLanceur is { } lanceur ? new[] { lanceur } : LancementDirect.Value;
        var info = new ProcessStartInfo
        {
            FileName = lancement[0],
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var argument in lancement.Skip(1).Concat(
            [page.ToString(CultureInfo.InvariantCulture), cote.ToString(CultureInfo.InvariantCulture), surface.ToString(CultureInfo.InvariantCulture),
             reglages.RenduPdfMemoireOctets.ToString(CultureInfo.InvariantCulture)]))
        {
            info.ArgumentList.Add(argument);
        }

        // Environnement vide : ni chaîne de connexion ni clé de stockage pour un processus qui lit un fichier reçu.
        info.Environment.Clear();
        info.Environment["DOTNET_gcServer"] = "0";
        info.Environment["DOTNET_EnableDiagnostics"] = "0";

        using var processus = Process.Start(info) ?? throw new InvalidOperationException("Processus de rendu PDF non démarré.");
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        delai.CancelAfter(TimeSpan.FromSeconds(reglages.RenduPdfDelaiSecondes));
        var erreurs = LireErreursAsync(processus.StandardError.BaseStream);
        var reussi = false;
        var journalise = false;
        try
        {
            var envoi = EnvoyerAsync(processus.StandardInput.BaseStream, pdf, delai.Token);
            var resultat = await LireResultatAsync(processus.StandardOutput.BaseStream, cote, surface, delai.Token).ConfigureAwait(false);
            await processus.WaitForExitAsync(delai.Token).ConfigureAwait(false);
            await envoi.ConfigureAwait(false);
            if (processus.ExitCode != 0 || resultat is null)
            {
                journalise = true;
                Journaliser(processus.ExitCode, await AttendreErreursAsync(erreurs).ConfigureAwait(false));
                throw Illisible();
            }

            reussi = true;
            return resultat;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogDelaiDepasse(journal, reglages.RenduPdfDelaiSecondes);
            throw new PdfRefuseException(
                "pdf_trop_long", TraitementFonds.Champ, "PDF trop long à rendre : exportez la seule page du plan, ou en image (PNG ou JPEG).");
        }
        finally
        {
            // Jamais d'enfant laissé en vie (délai, abandon, refus lu avant sa fin) ; sa sortie d'erreur se termine avec lui.
            // Lanceur isolé : l'API tue le lanceur (son compte) et le noyau tue l'enfant (autre compte, hors de sa portée,
            // interdit de créer des processus) ; lancement direct : tout l'arbre.
            processus.Kill(entireProcessTree: reglages.RenduPdfLanceur is null);
            await processus.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (!journalise && await AttendreErreursAsync(erreurs).ConfigureAwait(false) is { Length: > 0 } messages && reussi)
            {
                LogMessagesRendu(journal, messages); // rendu réussi avec un avertissement du lanceur (seccomp indisponible)
            }
        }
    }

    // L'enfant peut s'arrêter avant d'avoir tout lu (PDF trop gros, borne mémoire) : son refus vient de sa sortie.
    private static async Task EnvoyerAsync(Stream entree, byte[] pdf, CancellationToken cancellationToken)
    {
        try
        {
            await entree.WriteAsync(pdf, cancellationToken).ConfigureAwait(false);
            await entree.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception arret) when (arret is IOException or OperationCanceledException)
        {
            // Entrée fermée par l'enfant, ou délai dépassé : le refus vient de la lecture de sa sortie, jamais d'ici.
        }
        finally
        {
            await entree.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Ligne d'en-tête bornée puis, pour un rendu réussi, le PNG de la taille annoncée (bornée par les dimensions annoncées).
    private static async Task<PagePdfRendue?> LireResultatAsync(Stream sortie, uint cote, ulong surface, CancellationToken cancellationToken)
    {
        var entete = await LireLigneAsync(sortie, cancellationToken).ConfigureAwait(false);
        switch (entete?.Split(' '))
        {
            case ["CHIFFRE"]:
                throw new PdfRefuseException(
                    "pdf_protege", TraitementFonds.Champ, "PDF protégé (mot de passe ou restrictions) : enregistrez-le sans protection, puis importez-le de nouveau.");
            case ["PAGE", var nombre] when int.TryParse(nombre, NumberStyles.None, CultureInfo.InvariantCulture, out var pages) && pages > 0:
                throw new PdfRefuseException(
                    "page_absente", "page", string.Create(CultureInfo.InvariantCulture, $"Page absente : ce PDF compte {pages} page{(pages > 1 ? "s" : string.Empty)}."),
                    pages);
            case ["OK", var pages, var largeurPt, var hauteurPt, var largeur, var hauteur, var taille]
                when int.TryParse(pages, NumberStyles.None, CultureInfo.InvariantCulture, out var nombrePages) && nombrePages > 0
                && Points(largeurPt) is { } l && Points(hauteurPt) is { } h
                && uint.TryParse(largeur, NumberStyles.None, CultureInfo.InvariantCulture, out var largeurPx) && largeurPx is > 0 && largeurPx <= cote
                && uint.TryParse(hauteur, NumberStyles.None, CultureInfo.InvariantCulture, out var hauteurPx) && hauteurPx is > 0 && hauteurPx <= cote
                && (ulong)largeurPx * hauteurPx <= surface
                && long.TryParse(taille, NumberStyles.None, CultureInfo.InvariantCulture, out var octets)
                && octets > 0 && octets <= TailleMaxPng(largeurPx, hauteurPx):
                var png = new byte[octets];
                try
                {
                    await sortie.ReadExactlyAsync(png, cancellationToken).ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    return null; // enfant arrêté en cours d'écriture (mémoire du conteneur) : PDF trop lourd
                }

                return new PagePdfRendue(png, nombrePages, l, h);
            default:
                return null; // ILLISIBLE, en-tête inattendu ou sortie vide (arrêt brutal)
        }
    }

    // RVB sans filtre, compressé : au pire les données brutes, plus l'en-tête de chaque bloc compressé et du fichier.
    private static long TailleMaxPng(uint largeur, uint hauteur) => (((3L * largeur) + 1) * hauteur * 101 / 100) + (1024 * 1024);

    private static double? Points(string valeur) =>
        double.TryParse(valeur, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var points) && points is >= 1 and <= 1_000_000
            ? points
            : null;

    private static async Task<string?> LireLigneAsync(Stream sortie, CancellationToken cancellationToken)
    {
        var octets = new byte[EnteteMax];
        for (var i = 0; i < EnteteMax; i++)
        {
            if (await sortie.ReadAsync(octets.AsMemory(i, 1), cancellationToken).ConfigureAwait(false) == 0)
            {
                return null;
            }

            if (octets[i] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(octets, 0, i);
            }
        }

        return null;
    }

    // Sortie d'erreur gardée pour le journal (4 Ko au plus), le reste lu et ignoré : l'enfant n'est jamais bloqué dessus.
    private static async Task<string> LireErreursAsync(Stream erreurs)
    {
        var debut = new byte[ErreursMax];
        var lus = 0;
        var bloc = new byte[4096];
        int n;
        try
        {
            while ((n = await erreurs.ReadAsync(bloc).ConfigureAwait(false)) > 0)
            {
                var copie = Math.Min(n, ErreursMax - lus);
                Array.Copy(bloc, 0, debut, lus, copie);
                lus += copie;
            }
        }
        catch (Exception fermee) when (fermee is IOException or ObjectDisposedException)
        {
            // Lecture abandonnée (sortie d'erreur gardée ouverte par un autre processus, puis fermée avec le Process).
        }

        return Encoding.UTF8.GetString(debut, 0, lus);
    }

    // Sortie d'erreur attendue au plus 2 s après la fin du processus : un processus échappé au rendu qui la garderait
    // ouverte ne bloque jamais l'API (ni ce rendu, ni les suivants) ; le ménage du lanceur suivant le tue (SOC-05).
    private static async Task<string> AttendreErreursAsync(Task<string> erreurs)
    {
        try
        {
            return await erreurs.WaitAsync(ErreursAttente).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return "(sortie d'erreur encore ouverte par un autre processus)";
        }
    }

    private void Journaliser(int code, string erreurs) => LogRenduEchoue(journal, code, erreurs.Length > 0 ? erreurs : "(aucune)");

    private static PdfRefuseException Illisible() =>
        new("pdf_illisible", TraitementFonds.Champ, "PDF illisible ou trop lourd à rendre : exportez la page du plan en PDF simple ou en image (PNG ou JPEG).");

    // Lancement direct (sans lanceur isolé) : hôte dotnet du runtime courant, avec la configuration et le manifeste de
    // dépendances de l'application (bibliothèque native de PDFium à sa place, publiée pour une plateforme ou non) ;
    // ramasse-miettes réglé par l'environnement.
    internal static string[] PreparerLancementDirect()
    {
        var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var dependances = ((string?)AppContext.GetData("APP_CONTEXT_DEPS_FILES"))?.Split(';')[0]
            ?? throw new InvalidOperationException("Manifeste de dépendances de l'application introuvable.");
        var configuration = string.Concat(dependances.AsSpan(0, dependances.Length - ".deps.json".Length), ".runtimeconfig.json");
        return [dotnet, "exec", "--runtimeconfig", configuration, "--depsfile", dependances, Path.Combine(AppContext.BaseDirectory, $"{Programme}.dll")];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendu PDF arrêté : délai de {Delai} s dépassé.")]
    private static partial void LogDelaiDepasse(ILogger journal, int delai);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendu PDF réussi avec des messages : {Messages}")]
    private static partial void LogMessagesRendu(ILogger journal, string messages);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendu PDF refusé (code de sortie {Code}) ; erreurs : {Erreurs}")]
    private static partial void LogRenduEchoue(ILogger journal, int code, string erreurs);
}

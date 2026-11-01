using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace GestionSite.Modules.Patrimoine.RenduPdf;

/// <summary>
/// Bornes posées par le processus de rendu sur lui-même, avant de lire le PDF (Linux, image du conteneur) : mémoire
/// privée en écriture plafonnée par le noyau (RLIMIT_DATA : une allocation au-delà échoue, PDFium s'arrête, l'API reste
/// debout) et premier processus arrêté si le conteneur manque de mémoire (oom_score_adj). Sans effet ailleurs (poste de
/// développement) : le délai imposé par l'API reste la seule borne.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Exécuté seulement dans le processus enfant ; vérifié par les tests du processus réel et le test de fumée de l'image.")]
internal static partial class LimitesProcessus
{
    private const int RlimitData = 2; // Linux, toutes architectures

    internal static void Poser(long memoireOctets)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var limite = new Limite { Courante = (ulong)memoireOctets, Maximale = (ulong)memoireOctets };
        if (setrlimit(RlimitData, ref limite) != 0)
        {
            Environment.Exit(RenduPage.CodeLimites); // jamais de rendu sans sa borne mémoire
        }

        try
        {
            File.WriteAllText("/proc/self/oom_score_adj", "1000");
        }
        catch (IOException)
        {
            // Priorité d'arrêt : défense supplémentaire seulement (la borne mémoire est posée).
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Limite
    {
        public ulong Courante;
        public ulong Maximale;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int setrlimit(int ressource, ref Limite limite);
}

using System.Diagnostics.CodeAnalysis;

namespace GestionSite.Modules.Patrimoine.RenduPdf;

/// <summary>
/// Processus enfant (S21-05) : arguments « page cote surface memoire », PDF sur l'entrée, résultat sur la sortie
/// (<see cref="RenduPage"/>). Ses bornes (mémoire, priorité d'arrêt) sont posées avant toute lecture du PDF.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Point d'entrée du processus enfant : la logique est dans RenduPage, testée en processus et hors processus.")]
internal static class Program
{
    private static int Main(string[] args)
    {
        using var entree = Console.OpenStandardInput();
        using var sortie = Console.OpenStandardOutput();
        return RenduPage.Executer(args, entree, sortie, LimitesProcessus.Poser);
    }
}

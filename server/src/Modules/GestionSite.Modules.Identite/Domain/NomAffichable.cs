using GestionSite.SharedKernel;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Noms affichés (organisation, site…) : une ligne, règles du noyau (TexteAffichable).</summary>
internal static class NomAffichable
{
    internal const int MaxLength = 200;

    internal static string Valider(string nom, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nom, paramName);
        return TexteAffichable.Nettoyer(nom, int.MaxValue) is not { } valide
            ? throw new ArgumentException("Le nom doit tenir sur une ligne et contenir au moins un caractère visible.", paramName)
            : valide.Length <= MaxLength
                ? valide
                : throw new ArgumentException($"Le nom ne peut pas dépasser {MaxLength} caractères.", paramName);
    }
}

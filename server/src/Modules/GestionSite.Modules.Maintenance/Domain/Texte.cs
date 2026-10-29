using GestionSite.SharedKernel;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Texte saisi obligatoire, règles du noyau (TexteAffichable).</summary>
internal static class Texte
{
    internal static string Exiger(string? valeur, int longueurMax, string champ, bool multiligne = false) =>
        TexteAffichable.Nettoyer(valeur, longueurMax, multiligne)
            ?? throw new SaisieInvalideException(champ, $"{champ} est obligatoire, visible et limité à {longueurMax} caractères.");
}

using GestionSite.SharedKernel;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>
/// Saisie refusée d'un champ du formulaire (422 « donnee_invalide » + champ). Distincte d'ArgumentException :
/// une erreur interne n'est jamais présentée comme une erreur de saisie.
/// </summary>
internal sealed class SaisieInvalideException(string champ, string message) : Exception(message)
{
    internal string Champ { get; } = champ;
}

/// <summary>Texte saisi obligatoire, règles du noyau (TexteAffichable).</summary>
internal static class Texte
{
    internal static string Exiger(string? valeur, int longueurMax, string champ, bool multiligne = false) =>
        TexteAffichable.Nettoyer(valeur, longueurMax, multiligne)
            ?? throw new SaisieInvalideException(champ, $"{champ} est obligatoire, visible et limité à {longueurMax} caractères.");
}

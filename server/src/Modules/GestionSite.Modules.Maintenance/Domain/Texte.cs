namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Texte saisi obligatoire : espaces de bord retirés, longueur bornée (refus : 422 « donnee_invalide »).</summary>
internal static class Texte
{
    internal static string Exiger(string? valeur, int longueurMax, string champ)
    {
        var texte = valeur?.Trim() ?? string.Empty;
        if (texte.Length == 0 || texte.Length > longueurMax)
        {
            throw new ArgumentException($"{champ} est obligatoire et limité à {longueurMax} caractères.", champ);
        }

        return texte;
    }
}

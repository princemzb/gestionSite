using System.Globalization;
using System.Text;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Clé de comparaison des noms de site (décision du porteur du 03/11, US-31) : sans majuscules, accents ni ponctuation, espaces
/// réduits. « Chapelle Saint-Roch » et « chapelle saint roch » désignent le même site.
/// </summary>
internal static class NomSite
{
    internal static string Cle(string nom)
    {
        ArgumentNullException.ThrowIfNull(nom);
        var cle = new StringBuilder(nom.Length);
        var separer = false;
        foreach (var c in nom.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (!char.IsLetterOrDigit(c))
            {
                separer = cle.Length > 0;
                continue;
            }

            if (separer)
            {
                cle.Append(' ');
                separer = false;
            }

            cle.Append(char.ToLowerInvariant(c));
        }

        return cle.ToString();
    }
}

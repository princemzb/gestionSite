using System.Buffers;
using System.Globalization;
using System.Text;

namespace GestionSite.SharedKernel;

/// <summary>
/// Texte saisi affiché à d'autres personnes (noms, emplacements, descriptions) : NFC, longueur bornée, au moins un
/// caractère visible, sans ce que PostgreSQL ou UTF-8 refusent ni contrôle d'inversion bidirectionnelle.
/// Une seule ligne, sauf si <c>multiligne</c> (retours à la ligne et tabulations admis, ex. description).
/// </summary>
public static class TexteAffichable
{
    /// <summary>Le texte nettoyé et normalisé, ou null s'il est vide, invisible, trop long ou interdit.</summary>
    public static string? Nettoyer(string? texte, int longueurMax, bool multiligne = false)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            return null;
        }

        var nettoye = Rogner(texte);
        if (!EstValide(nettoye, multiligne))
        {
            return null;
        }

        // Forme NFC : « É » précomposé ou « E » + accent combinant donnent le même texte stocké. Longueur comptée
        // en unités UTF-16 : plus strict que varchar(n) (points de code), donc jamais rejeté par la base.
        nettoye = nettoye.Normalize(NormalizationForm.FormC);
        return nettoye.Length <= longueurMax ? nettoye : null;
    }

    // Refusés : ce que PostgreSQL ou UTF-8 ne stockent pas (NUL, demi-paire de substitution), les retours
    // à la ligne (sauf \n, \r et tabulation d'un texte multiligne) et les contrôles d'inversion bidirectionnelle (texte affiché à l'envers). Les autres
    // caractères de format invisibles du plan de base sont refusés, sauf U+200C (persan) et U+200D (émojis) ;
    // les étiquettes de drapeau (hors plan de base) restent permises.
    // Au moins une lettre, un chiffre, une ponctuation ou un symbole. Limite connue : les homoglyphes
    // et quelques caractères d'apparence vide (U+3164, U+2800) ne sont pas détectés.
    private static bool EstValide(string nom, bool multiligne)
    {
        var visible = false;
        var reste = nom.AsSpan();
        while (!reste.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(reste, out var rune, out var lus) != OperationStatus.Done)
            {
                return false;
            }

            var categorie = Rune.GetUnicodeCategory(rune);
            if ((categorie is UnicodeCategory.Control && !(multiligne && rune.Value is '\n' or '\r' or '\t'))
                || categorie is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                || EstControleBidirectionnel(rune.Value)
                || (categorie == UnicodeCategory.Format && rune.Value < 0x10000 && rune.Value is not (ZeroWidthNonJoiner or ZeroWidthJoiner)))
            {
                return false;
            }

            visible |= Rune.IsLetter(rune) || Rune.IsNumber(rune) || Rune.IsPunctuation(rune) || Rune.IsSymbol(rune)
                || categorie == UnicodeCategory.SpacingCombiningMark;
            reste = reste[lus..];
        }

        return visible;
    }

    // Espaces et caractères de format invisibles en bordure (U+200B, U+2060, U+FEFF…) : sinon deux noms
    // identiques à l'œil différeraient. Les étiquettes de drapeau (hors plan de base) ne sont pas touchées.
    private static string Rogner(string nom)
    {
        static bool Invisible(char c) => char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format;

        var debut = 0;
        var fin = nom.Length;
        while (debut < fin && Invisible(nom[debut]))
        {
            debut++;
        }

        while (fin > debut && Invisible(nom[fin - 1]))
        {
            fin--;
        }

        return nom[debut..fin];
    }

    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;

    private static bool EstControleBidirectionnel(int point) =>
        point is (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x200E or 0x200F or 0x061C;
}

using System.Buffers;
using System.Globalization;
using System.Text;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Règles communes des noms affichés (organisation, site…) : une ligne, visible, NFC, longueur bornée.</summary>
internal static class NomAffichable
{
    // Compté en unités UTF-16 : plus strict que varchar(200) (points de code), donc jamais rejeté par la base.
    internal const int MaxLength = 200;

    internal static string Valider(string nom, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nom, paramName);
        var nomNettoye = Nettoyer(nom);
        if (!EstNomValide(nomNettoye))
        {
            throw new ArgumentException("Le nom doit tenir sur une ligne et contenir au moins un caractère visible.", paramName);
        }

        // Forme NFC : « É » précomposé ou « E » + accent combinant donnent le même nom stocké.
        nomNettoye = nomNettoye.Normalize(NormalizationForm.FormC);
        if (nomNettoye.Length > MaxLength)
        {
            throw new ArgumentException($"Le nom ne peut pas dépasser {MaxLength} caractères.", paramName);
        }

        return nomNettoye;
    }

    // Refusés : ce que PostgreSQL ou UTF-8 ne stockent pas (NUL, demi-paire de substitution), les retours
    // à la ligne et les contrôles d'inversion bidirectionnelle (texte affiché à l'envers). Les autres
    // caractères de format invisibles du plan de base sont refusés, sauf U+200C (persan) et U+200D (émojis) ;
    // les étiquettes de drapeau (hors plan de base) restent permises.
    // Au moins une lettre, un chiffre, une ponctuation ou un symbole. Limite connue : les homoglyphes
    // et quelques caractères d'apparence vide (U+3164, U+2800) ne sont pas détectés.
    private static bool EstNomValide(string nom)
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
            if (categorie is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
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
    private static string Nettoyer(string nom)
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

using System.Buffers;
using System.Globalization;
using System.Text;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Organisation cliente (église ou réseau d'églises), frontière d'isolation des données (RG-01).</summary>
internal sealed class Organisation
{
    // Compté en unités UTF-16 : plus strict que varchar(200) (points de code), donc jamais rejeté par la base.
    internal const int NomMaxLength = 200;

    private Organisation()
    {
    }

    internal Organisation(Guid id, string nom, DateTimeOffset creeLe)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant d'une organisation ne peut pas être vide.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(nom);
        var nomNettoye = Nettoyer(nom);
        if (nomNettoye.Length > NomMaxLength)
        {
            throw new ArgumentException($"Le nom ne peut pas dépasser {NomMaxLength} caractères.", nameof(nom));
        }

        if (!EstNomValide(nomNettoye))
        {
            throw new ArgumentException("Le nom doit tenir sur une ligne et contenir au moins un caractère visible.", nameof(nom));
        }

        // PostgreSQL « timestamp with time zone » : UTC seulement, à la microseconde (le reste serait perdu).
        var utc = creeLe.ToUniversalTime();
        utc = utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));

        // Valeur par défaut ou bornes extrêmes : Npgsql écrirait « -infinity » / « infinity ».
        // Minimum testé après arrondi (MinValue + quelques ticks y retombe) ; maximum avant.
        if (utc.UtcDateTime == DateTime.MinValue || creeLe.UtcDateTime == DateTime.MaxValue)
        {
            throw new ArgumentException("La date de création est obligatoire et doit être une date réelle.", nameof(creeLe));
        }

        Id = id;
        Nom = nomNettoye;
        CreeLe = utc;
    }

    public Guid Id { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public DateTimeOffset CreeLe { get; private set; }

    // Refusés : ce que PostgreSQL ou UTF-8 ne stockent pas (NUL, demi-paire de substitution), les retours
    // à la ligne et les contrôles d'inversion bidirectionnelle (texte affiché à l'envers). Les autres
    // caractères de format restent permis (U+200C persan, U+200D et étiquettes des émojis composés).
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
                || EstControleBidirectionnel(rune.Value))
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

    private static bool EstControleBidirectionnel(int point) =>
        point is (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x200E or 0x200F or 0x061C;
}

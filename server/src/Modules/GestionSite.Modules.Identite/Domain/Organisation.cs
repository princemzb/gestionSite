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
        var nomNettoye = nom.Trim();
        if (nomNettoye.Length > NomMaxLength)
        {
            throw new ArgumentException($"Le nom ne peut pas dépasser {NomMaxLength} caractères.", nameof(nom));
        }

        if (!EstNomValide(nomNettoye))
        {
            throw new ArgumentException("Le nom doit tenir sur une ligne, sans caractère de contrôle ni invisible seul.", nameof(nom));
        }

        // Valeur par défaut ou bornes extrêmes : Npgsql écrirait « -infinity » / « infinity ».
        if (creeLe.UtcDateTime == DateTime.MinValue || creeLe.UtcDateTime == DateTime.MaxValue)
        {
            throw new ArgumentException("La date de création est obligatoire et doit être une date réelle.", nameof(creeLe));
        }

        Id = id;
        Nom = nomNettoye;
        // PostgreSQL « timestamp with time zone » : UTC seulement, à la microseconde (le reste serait perdu).
        var utc = creeLe.ToUniversalTime();
        CreeLe = utc.AddTicks(-(utc.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));
    }

    public Guid Id { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public DateTimeOffset CreeLe { get; private set; }

    private const int ZeroWidthJoiner = 0x200D;

    // PostgreSQL refuse le caractère NUL, l'encodage UTF-8 échoue sur une demi-paire de substitution,
    // et un nom fait seulement de caractères invisibles paraîtrait vide.
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
            // Caractères de format refusés (inversion bidirectionnelle, espaces de largeur nulle…),
            // sauf le liant U+200D des émojis composés : pas de noms d'apparence trompeuse.
            if (categorie is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                || (categorie == UnicodeCategory.Format && rune.Value != ZeroWidthJoiner))
            {
                return false;
            }

            visible |= rune.Value != ZeroWidthJoiner && !Rune.IsWhiteSpace(rune);
            reste = reste[lus..];
        }

        return visible;
    }
}

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

        if (!EstTexteStockable(nomNettoye))
        {
            throw new ArgumentException("Le nom contient un caractère de contrôle ou invalide.", nameof(nom));
        }

        if (creeLe == default)
        {
            throw new ArgumentException("La date de création est obligatoire.", nameof(creeLe));
        }

        Id = id;
        Nom = nomNettoye;
        CreeLe = creeLe.ToUniversalTime(); // PostgreSQL « timestamp with time zone » n'accepte que l'UTC.
    }

    public Guid Id { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public DateTimeOffset CreeLe { get; private set; }

    // PostgreSQL refuse le caractère NUL et l'encodage UTF-8 échoue sur une moitié de paire de substitution.
    private static bool EstTexteStockable(string texte)
    {
        for (var i = 0; i < texte.Length; i++)
        {
            if (char.IsControl(texte[i]) || char.IsLowSurrogate(texte[i]))
            {
                return false;
            }

            if (char.IsHighSurrogate(texte[i]))
            {
                if (i + 1 == texte.Length || !char.IsLowSurrogate(texte[i + 1]))
                {
                    return false;
                }

                i++;
            }
        }

        return true;
    }
}

namespace GestionSite.Modules.Identite.Domain;

/// <summary>Organisation cliente (église ou réseau d'églises), frontière d'isolation des données (RG-01).</summary>
internal sealed class Organisation
{
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
}

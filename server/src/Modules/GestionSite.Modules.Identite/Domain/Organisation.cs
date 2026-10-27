namespace GestionSite.Modules.Identite.Domain;

/// <summary>Organisation cliente (église ou réseau d'églises), frontière d'isolation des données (RG-01).</summary>
internal sealed class Organisation
{
    private Organisation()
    {
    }

    internal Organisation(Guid id, string nom, DateTimeOffset creeLe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nom);
        Id = id;
        Nom = nom.Trim();
        CreeLe = creeLe;
    }

    public Guid Id { get; private set; }

    public string Nom { get; private set; } = string.Empty;

    public DateTimeOffset CreeLe { get; private set; }
}

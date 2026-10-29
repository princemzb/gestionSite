using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Jeton de renouvellement d'un appareil, conservé par son empreinte SHA-256 seulement. Chaque usage le
/// remplace par un nouveau de la même famille (rotation) ; un jeton déjà remplacé présenté de nouveau
/// signale un vol possible et révoque toute la famille.
/// </summary>
internal sealed class JetonRenouvellement : IDonneeGlobale
{
    internal const int EmpreinteLength = 32;

    private JetonRenouvellement()
    {
    }

    internal JetonRenouvellement(Guid id, Guid compteId, Guid famille, byte[] empreinte, DateTimeOffset creeLe, DateTimeOffset expireLe)
    {
        Identifiants.ExigerNonVide(id, nameof(id));
        Identifiants.ExigerNonVide(compteId, nameof(compteId));
        Identifiants.ExigerNonVide(famille, nameof(famille));
        ArgumentNullException.ThrowIfNull(empreinte);
        if (empreinte.Length != EmpreinteLength)
        {
            throw new ArgumentException("Empreinte SHA-256 attendue.", nameof(empreinte));
        }

        Id = id;
        CompteId = compteId;
        Famille = famille;
        Empreinte = empreinte;
        CreeLe = Horodatage.Valider(creeLe, nameof(creeLe));
        ExpireLe = Horodatage.Valider(expireLe, nameof(expireLe));
        if (ExpireLe <= CreeLe)
        {
            throw new ArgumentException("L'expiration doit suivre la création.", nameof(expireLe));
        }
    }

    public Guid Id { get; private set; }

    public Guid CompteId { get; private set; }

    /// <summary>Lignée de rotation d'un même appareil (une connexion).</summary>
    public Guid Famille { get; private set; }

    public byte[] Empreinte { get; private set; } = [];

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset ExpireLe { get; private set; }

    public DateTimeOffset? RevoqueLe { get; private set; }

    /// <summary>Jeton émis en remplacement (rotation) ; un jeton remplacé n'est plus utilisable.</summary>
    public Guid? RemplacePar { get; private set; }

    internal bool EstUtilisable(DateTimeOffset maintenant) => RevoqueLe is null && RemplacePar is null && maintenant < ExpireLe;

    internal void Remplacer(Guid successeur, DateTimeOffset le)
    {
        RemplacePar = successeur;
        Revoquer(le);
    }

    internal void Revoquer(DateTimeOffset le) => RevoqueLe ??= Horodatage.Valider(le, nameof(le));
}

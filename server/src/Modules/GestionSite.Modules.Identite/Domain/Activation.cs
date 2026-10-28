using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Lien d'activation d'un compte (ADR-10) : à usage unique, valable 72 h, conservé par son empreinte
/// SHA-256 seulement ; le secret n'est montré qu'une fois, à l'administrateur qui crée le membre.
/// </summary>
internal sealed class Activation : IDonneeGlobale
{
    internal const int EmpreinteLength = 32;
    internal static readonly TimeSpan Validite = TimeSpan.FromHours(72);

    private Activation()
    {
    }

    internal Activation(Guid id, Guid compteId, Guid organisationId, byte[] empreinte, DateTimeOffset creeLe)
    {
        Identifiants.ExigerNonVide(id, nameof(id));
        Identifiants.ExigerNonVide(compteId, nameof(compteId));
        Identifiants.ExigerNonVide(organisationId, nameof(organisationId));
        ArgumentNullException.ThrowIfNull(empreinte);
        if (empreinte.Length != EmpreinteLength)
        {
            throw new ArgumentException("Empreinte SHA-256 attendue.", nameof(empreinte));
        }

        Id = id;
        CompteId = compteId;
        OrganisationId = organisationId;
        Empreinte = empreinte;
        CreeLe = Horodatage.Valider(creeLe, nameof(creeLe));
        ExpireLe = CreeLe + Validite;
    }

    public Guid Id { get; private set; }

    public Guid CompteId { get; private set; }

    /// <summary>
    /// Organisation qui a créé le compte et émis le lien : seule elle peut en émettre un nouveau. Une autre
    /// organisation ne reçoit jamais de lien pour un compte qu'elle n'a pas créé (prise de compte).
    /// </summary>
    public Guid OrganisationId { get; private set; }

    public byte[] Empreinte { get; private set; } = [];

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset ExpireLe { get; private set; }

    public DateTimeOffset? UtiliseLe { get; private set; }

    internal bool EstUtilisable(DateTimeOffset maintenant) => UtiliseLe is null && maintenant < ExpireLe;

    internal void Utiliser(DateTimeOffset le)
    {
        if (!EstUtilisable(le))
        {
            throw new InvalidOperationException("Lien d'activation expiré ou déjà utilisé.");
        }

        UtiliseLe = Horodatage.Valider(le, nameof(le));
    }
}

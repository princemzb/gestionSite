using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Lien de réinitialisation du mot de passe (US-31, décision du porteur du 03/11) : créé par un administrateur pour un compte
/// que son organisation a créé et qui n'est lié à aucune autre, tant que l'e-mail n'est pas en service. À usage unique,
/// valable 1 heure, conservé par son empreinte SHA-256 seulement ; le secret n'est montré qu'une fois, à l'administrateur.
/// </summary>
internal sealed class Reinitialisation : IDonneeGlobale
{
    internal const int EmpreinteLength = 32;
    internal static readonly TimeSpan Validite = TimeSpan.FromHours(1);

    private Reinitialisation()
    {
    }

    internal Reinitialisation(Guid id, Guid compteId, Guid organisationId, Guid auteurCompteId, byte[] empreinte, DateTimeOffset creeLe)
    {
        Identifiants.ExigerNonVide(id, nameof(id));
        Identifiants.ExigerNonVide(compteId, nameof(compteId));
        Identifiants.ExigerNonVide(organisationId, nameof(organisationId));
        Identifiants.ExigerNonVide(auteurCompteId, nameof(auteurCompteId));
        ArgumentNullException.ThrowIfNull(empreinte);
        if (empreinte.Length != EmpreinteLength)
        {
            throw new ArgumentException("Empreinte SHA-256 attendue.", nameof(empreinte));
        }

        Id = id;
        CompteId = compteId;
        OrganisationId = organisationId;
        AuteurCompteId = auteurCompteId;
        Empreinte = empreinte;
        CreeLe = Horodatage.Valider(creeLe, nameof(creeLe));
        ExpireLe = CreeLe + Validite;
    }

    public Guid Id { get; private set; }

    public Guid CompteId { get; private set; }

    /// <summary>Organisation qui a émis le lien : le compte doit y être encore membre actif, et lié à aucune autre, à l'usage.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>Administrateur qui a créé le lien (aussi inscrit au journal des habilitations).</summary>
    public Guid AuteurCompteId { get; private set; }

    public byte[] Empreinte { get; private set; } = [];

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset ExpireLe { get; private set; }

    public DateTimeOffset? UtiliseLe { get; private set; }

    internal bool EstUtilisable(DateTimeOffset maintenant) => UtiliseLe is null && maintenant < ExpireLe;

    internal void Utiliser(DateTimeOffset le)
    {
        if (!EstUtilisable(le))
        {
            throw new InvalidOperationException("Lien de réinitialisation expiré ou déjà utilisé.");
        }

        UtiliseLe = Horodatage.Valider(le, nameof(le));
    }
}

namespace GestionSite.SharedKernel;

/// <summary>
/// Organisation de la requête en cours (une par portée de service). Sans organisation définie,
/// aucune donnée d'organisation n'est visible : ni par les filtres applicatifs, ni par la RLS.
/// </summary>
public sealed class OrganisationCourante
{
    /// <summary>Variable de session PostgreSQL lue par les politiques RLS.</summary>
    public const string VariableSession = "app.organisation_id";

    private bool _transactionOuverte;

    public Guid? Id { get; private set; }

    /// <summary>Signalé par la persistance à l'ouverture et à la fin de chaque transaction.</summary>
    public void SignalerTransaction(bool ouverte) => _transactionOuverte = ouverte;

    /// <summary>
    /// Définie une seule fois par portée : changer d'organisation en cours de requête est refusé. Peut l'être
    /// après un premier accès à la base (contrôle d'adhésion) : chaque lecture transmet la valeur du moment ;
    /// mais pas pendant une transaction, dont la valeur est fixée à l'ouverture.
    /// </summary>
    public void Definir(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'organisation courante ne peut pas être vide.", nameof(id));
        }

        if (Id is { } actuelle && actuelle != id)
        {
            throw new InvalidOperationException("L'organisation courante est déjà définie pour cette requête.");
        }

        if (_transactionOuverte && Id is null)
        {
            // La transaction a reçu « aucune organisation » à son ouverture (SET LOCAL) : les barrières divergeraient.
            throw new InvalidOperationException("L'organisation courante doit être définie avant d'ouvrir une transaction.");
        }

        Id = id;
    }
}

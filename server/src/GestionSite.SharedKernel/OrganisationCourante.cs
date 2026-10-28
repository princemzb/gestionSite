namespace GestionSite.SharedKernel;

/// <summary>
/// Organisation de la requête en cours (une par portée de service). Sans organisation définie,
/// aucune donnée d'organisation n'est visible : ni par les filtres applicatifs, ni par la RLS.
/// </summary>
public sealed class OrganisationCourante
{
    /// <summary>Variable de session PostgreSQL lue par les politiques RLS.</summary>
    public const string VariableSession = "app.organisation_id";

    private bool _transmise;

    public Guid? Id { get; private set; }

    /// <summary>Définie une seule fois par portée : changer d'organisation en cours de requête est refusé.</summary>
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

        if (Id is null && _transmise)
        {
            // La variable de session RLS a déjà reçu « aucune organisation » : les barrières divergeraient.
            throw new InvalidOperationException("L'organisation courante doit être définie avant le premier accès à la base.");
        }

        Id = id;
    }

    /// <summary>Appelée quand la valeur est transmise à une connexion : elle ne peut plus apparaître ensuite.</summary>
    public void MarquerTransmise() => _transmise = true;
}

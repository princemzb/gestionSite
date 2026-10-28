namespace GestionSite.SharedKernel;

/// <summary>
/// Organisation de la requête en cours (une par portée de service). Sans organisation définie,
/// aucune donnée d'organisation n'est visible : ni par les filtres applicatifs, ni par la RLS.
/// </summary>
public sealed class OrganisationCourante
{
    /// <summary>Variable de session PostgreSQL lue par les politiques RLS.</summary>
    public const string VariableSession = "app.organisation_id";

    public Guid? Id { get; private set; }

    /// <summary>
    /// Définie une seule fois par portée : changer d'organisation en cours de requête est refusé. Peut l'être
    /// après un premier accès à la base (contrôle d'adhésion) : chaque commande transmet la valeur du moment.
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

        Id = id;
    }
}

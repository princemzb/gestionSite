namespace GestionSite.SharedKernel;

/// <summary>
/// Compte authentifié de la requête (une par portée de service), issu du jeton d'accès. Sert aux
/// politiques RLS « par compte » (ses propres adhésions, toutes organisations confondues) et aux auteurs.
/// </summary>
public sealed class CompteCourant
{
    /// <summary>Variable PostgreSQL lue par les politiques RLS « par compte ».</summary>
    public const string VariableSession = "app.compte_id";

    public Guid? Id { get; private set; }

    public void Definir(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Le compte courant ne peut pas être vide.", nameof(id));
        }

        if (Id is { } actuel && actuel != id)
        {
            throw new InvalidOperationException("Le compte courant est déjà défini pour cette requête.");
        }

        Id = id;
    }
}

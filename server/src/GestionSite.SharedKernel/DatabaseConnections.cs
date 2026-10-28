namespace GestionSite.SharedKernel;

/// <summary>Clés des sources de données PostgreSQL fournies par l'hôte.</summary>
public static class DatabaseConnections
{
    /// <summary>
    /// Source de données « keyed » du rôle propriétaire du schéma, réservée à l'application des migrations.
    /// La source par défaut (non « keyed ») est celle du rôle applicatif, soumis à la RLS.
    /// </summary>
    public const string Migrations = "migrations";
}

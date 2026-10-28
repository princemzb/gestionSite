namespace GestionSite.SharedKernel.Persistence;

/// <summary>Organisation et compte de la requête, transmis à la base pour les politiques RLS.</summary>
public sealed record ContexteRequete(OrganisationCourante Organisation, CompteCourant Compte)
{
    /// <summary>Seconde barrière (ADR-10) : intercepteurs à ajouter au contexte EF de chaque module.</summary>
    public IReadOnlyList<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor> Intercepteurs() =>
        [new OrganisationCommandInterceptor(Organisation, Compte), new OrganisationTransactionInterceptor(Organisation, Compte)];
}

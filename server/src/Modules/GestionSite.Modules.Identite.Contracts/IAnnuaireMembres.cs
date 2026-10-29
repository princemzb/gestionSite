namespace GestionSite.Modules.Identite.Contracts;

/// <summary>
/// Noms affichés des membres de l'organisation courante, pour nommer les auteurs d'un historique. À appeler après une
/// autorisation favorable (organisation fixée) : un compte étranger à l'organisation n'a pas de nom ici.
/// </summary>
public interface IAnnuaireMembres
{
    Task<IReadOnlyDictionary<Guid, string>> NomsAsync(IReadOnlyCollection<Guid> compteIds, CancellationToken cancellationToken);
}

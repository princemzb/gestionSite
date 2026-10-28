namespace GestionSite.Modules.Plateforme.PocSynchronisation;

/// <summary>
/// POC PREP-06 (expérimental) : désactivé par défaut. Activé en développement et dans les tests seulement ;
/// ni tables ni routes sinon. À remplacer par la synchronisation réelle (US-14).
/// </summary>
public sealed class PocSynchronisationOptions
{
    public const string Section = "Poc:Synchronisation";

    public bool Active { get; init; }
}

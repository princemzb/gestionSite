namespace GestionSite.Modules.Identite.Contracts;

/// <summary>
/// Actions soumises à autorisation (docs/metier/PERMISSIONS.md). Toutes s'exercent sur un site, sauf
/// celles d'administration (GererComptes, AutoriserSupport) qui portent sur toute l'organisation. Les
/// conditions propres à un module (intervention affectée, délégation budgétaire…) restent à vérifier
/// par ce module en plus de le droit.
/// </summary>
public enum Droit
{
    /// <summary>Lire le site : inventaire, grilles, plans, opérations autorisées (comptes internes du site).</summary>
    ConsulterSite,
    DeposerSignalement,
    QualifierAffecter,
    /// <summary>Le module Maintenance vérifie en plus que le technicien est affecté.</summary>
    DocumenterIntervention,
    /// <summary>Le module Maintenance vérifie en plus que le technicien est affecté.</summary>
    CloturerIntervention,
    RouvrirArbitrer,
    /// <summary>Pasteur : relance interne seulement (PERMISSIONS.md), à distinguer par le module.</summary>
    Relancer,
    LancerIa,
    /// <summary>Selon délégation (DEC-02), vérifiée par le module Budget.</summary>
    EngagerBudget,
    EnregistrerAccordFinance,
    ConsommerStock,
    ReceptionnerAjusterStock,
    VoirPosition,
    LireContactsDeclarant,
    /// <summary>Estimation d'un dossier affecté au technicien : règle propre au module (DEC-04).</summary>
    LireMontants,
    ModifierInventaire,
    ModifierGrilles,
    /// <summary>Droit nominatif attribuable par l'administrateur : à venir avec les plans (US-21).</summary>
    ModifierPlans,
    GererComptes,
    AutoriserSupport,
}

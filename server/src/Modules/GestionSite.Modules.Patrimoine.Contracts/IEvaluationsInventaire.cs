namespace GestionSite.Modules.Patrimoine.Contracts;

/// <summary>Répartition d'un lot par état de l'échelle commune (somme = quantité du lot).</summary>
/// <param name="Bon"></param>
/// <param name="Satisfaisant"></param>
/// <param name="Degrade"></param>
/// <param name="Mauvais"></param>
/// <param name="Critique"></param>
public sealed record RepartitionInventaire(int Bon, int Satisfaisant, int Degrade, int Mauvais, int Critique);

/// <summary>
/// Évaluation US-11 saisie pendant une inspection (US-12, décision du 30/10) : mêmes règles qu'une évaluation directe
/// (grille en vigueur, état ou répartition, photo pour « Mauvais » et « Critique »).
/// </summary>
/// <param name="CommandeId">Identifiant d'envoi de l'évaluation (dérivé de celui de l'inspection).</param>
/// <param name="EvaluateurCompteId">Compte qui saisit l'inspection.</param>
/// <param name="GrilleVersion"></param>
/// <param name="Constats">Une réponse par point de la grille (vrai conforme, faux défaut, null non vérifié).</param>
/// <param name="Mesures">Une valeur par mesure de la grille.</param>
/// <param name="Etat">Composant individuel : Bon, Satisfaisant, Degrade, Mauvais ou Critique.</param>
/// <param name="Repartition">Lot : répartition par état.</param>
/// <param name="Remarque"></param>
/// <param name="Photos">Photos reçues (JPEG, PNG ou HEIC), réencodées par l'évaluation.</param>
/// <param name="ObserveLe">
/// Instant de l'observation (date réelle de l'inspection) ; l'évaluation est datée ainsi, et une évaluation plus récente
/// du composant n'est jamais masquée. Null : heure du serveur.
/// </param>
public sealed record SaisieEvaluationInventaire(
    Guid CommandeId, Guid EvaluateurCompteId, int GrilleVersion, IReadOnlyList<bool?> Constats, IReadOnlyList<decimal?> Mesures, string? Etat,
    RepartitionInventaire? Repartition, string? Remarque, IReadOnlyList<byte[]> Photos, DateTimeOffset? ObserveLe = null);

/// <summary>Évaluation enregistrée : note affichée (une décimale) et son libellé.</summary>
/// <param name="Id"></param>
/// <param name="Note"></param>
/// <param name="Libelle"></param>
public sealed record EvaluationInventaire(Guid Id, decimal Note, string Libelle);

/// <summary>
/// Évaluation refusée par l'inventaire, avec un code stable (« composant_retire », « grille_modifiee »,
/// « commande_reutilisee », « modifie_entre_temps », « composant_introuvable ») ; une saisie invalide reste une
/// SaisieInvalideException du noyau.
/// </summary>
public sealed class RefusInventaireException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Évaluation d'un composant par un autre module (US-12 : inspection interne). À appeler après une autorisation
/// favorable sur le site (organisation courante fixée) ; le module appelant a vérifié les droits de l'évaluateur. Un
/// rejeu de la même commande rend la même évaluation.
/// </summary>
public interface IEvaluationsInventaire
{
    Task<EvaluationInventaire> EvaluerAsync(Guid siteId, Guid composantId, SaisieEvaluationInventaire saisie, CancellationToken cancellationToken);
}

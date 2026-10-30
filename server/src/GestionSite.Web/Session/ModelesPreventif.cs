namespace GestionSite.Web.Session;

// Réponses de l'API du préventif (US-12, CONTRATS « préventif »), lues par les écrans S12-06 et S12-07.
internal sealed record ClassementApi(
    bool Erp, string? Type, IReadOnlyList<string> TypesSecondaires, int? Categorie, int? Effectif, bool LocauxSommeil, bool MonumentHistorique,
    string PermisAvant1997, DateOnly? DerniereVisiteCommission);

internal sealed record BatimentClasseApi(Guid Id, string Nom, ClassementApi? Classement, uint Version);

internal sealed record EtablissementApi(bool? SalariesPresents, uint Version, IReadOnlyList<BatimentClasseApi> Batiments);

internal sealed record PeriodeApi(int Valeur, string Unite);

internal sealed record CibleApi(string Type, Guid BatimentId, Guid ZoneId, Guid? ComposantId, string? CorpsEtat, string Libelle);

internal sealed record EcartApi(string Motif, string? Commentaire, DateTimeOffset Le, Guid AuteurCompteId, string? Auteur);

internal sealed record PropositionApi(
    string Cle, string Modele, string Libelle, string Nature, string Base, string Reference, bool AConfirmer, string Intervenant, PeriodeApi Periode,
    string Mode, DateOnly Echeance, CibleApi Cible, string Statut, EcartApi? Ecart);

internal sealed record IntervenantPlanApi(Guid? CompteId, string? Nom, string? Prestataire, Guid? ReferentCompteId, string? Referent);

internal sealed record EcheanceApi(
    DateOnly Echeance, DateOnly OuvertureFenetre, string Etat, string? Statut, Guid? OccurrenceId, IntervenantPlanApi? Intervenant, uint Version);

internal sealed record PlanApi(
    Guid Id, string Libelle, string Nature, string Base, string? Reference, string? Modele, string? PropositionCle, string? TypeIntervenant, CibleApi Cible,
    IntervenantPlanApi? Intervenant, string? DonneurOrdre, PeriodeApi? Periode, string Mode, DateOnly PremiereEcheance, bool Suspendu, string? MotifSuspension,
    bool SuspensionAutomatique, EcheanceApi? Prochaine, uint Version);

internal sealed record OccurrenceApi(
    Guid Id, DateOnly Echeance, string Statut, IntervenantPlanApi? Intervenant, DateTimeOffset? AffecteeLe, DateOnly? RealiseeLe, DateOnly? SauteeLe,
    string? MotifSaut, string? CommentaireSaut, Guid? ControleId, uint Version);

internal sealed record EvenementPlanApi(DateTimeOffset Le, string Nature, Guid? AuteurCompteId, string? Auteur, System.Text.Json.JsonElement? Detail);

internal sealed record FichePlanApi(PlanApi Plan, IReadOnlyList<OccurrenceApi> Occurrences, IReadOnlyList<EvenementPlanApi> Historique);

internal sealed record PointControleApi(string Texte, bool? Conforme, bool Danger);

internal sealed record MesureControleApi(string Libelle, string Unite, decimal? Valeur);

internal sealed record ObservationApi(string Libelle, Guid ZoneId, bool Danger);

internal sealed record RapportApi(int Taille, string Empreinte);

internal sealed record ConstatApi(int Rang, string Libelle, bool Danger, Guid DossierId, bool Rattache, string StatutDossier);

internal sealed record ControleApi(
    Guid Id, Guid PlanId, string Plan, string Nature, DateOnly Echeance, DateOnly RealiseLe, DateTimeOffset SaisiLe, Guid AuteurCompteId, string? Auteur,
    Guid? IntervenantCompteId, string? Intervenant, string? Prestataire, string? NumeroAgrement, string Resultat, string? CompteRendu, string? CorpsEtat,
    Guid? ComposantId, Guid ZoneId, Guid BatimentId, int? GrilleVersion, IReadOnlyList<PointControleApi> Points, IReadOnlyList<MesureControleApi> Mesures,
    Guid? EvaluationId, decimal? Note, string? LibelleNote, IReadOnlyList<int> Photos, IReadOnlyList<ObservationApi> Observations, RapportApi? Rapport,
    IReadOnlyList<ConstatApi>? Constats, bool ReservesLevees);

internal sealed record ControleResumeApi(
    Guid Id, Guid PlanId, string Plan, string Nature, DateOnly Echeance, DateOnly RealiseLe, string Resultat, Guid? ComposantId, Guid BatimentId, int Defauts,
    decimal? Note, bool Rapport);

internal sealed record ControleEnregistreApi(Guid Id, string Statut, Guid? EvaluationId, decimal? Note, string? Libelle);

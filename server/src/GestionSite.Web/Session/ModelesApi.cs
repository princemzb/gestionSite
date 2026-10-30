namespace GestionSite.Web.Session;

// Réponses de l'API lues par l'application web (docs/dev/CONTRATS.md).
internal sealed record JetonsApi(string JetonAcces, DateTimeOffset AccesExpireLe, string JetonRenouvellement, DateTimeOffset RenouvellementExpireLe);

internal sealed record MoiApi(Guid CompteId, string? Email, IReadOnlyList<OrganisationApi> Organisations, IReadOnlyList<InvitationApi>? Invitations);

internal sealed record InvitationApi(Guid MembreId, Guid OrganisationId, string Organisation);

internal sealed record OrganisationApi(Guid Id, string Nom, IReadOnlyList<RoleApi> Roles, IReadOnlyList<SiteApi> Sites);

internal sealed record RoleApi(string Role, Guid? SiteId);

internal sealed record SiteApi(Guid Id, string Nom);

internal sealed record MembreApi(Guid MembreId, Guid CompteId, string Nom, string? Email, bool InvitationEnAttente, int Version, IReadOnlyList<RoleApi> Roles);

internal sealed record ActivationApi(string Jeton, DateTimeOffset ExpireLe);

internal sealed record MembreCreeApi(Guid MembreId, Guid CompteId, int Version, ActivationApi? Activation);

internal sealed record ProblemeApi(string? Code, string? Title, string? Champ);

internal sealed record ActivationReussieApi(string Email);

internal sealed record LienPublicApi(Guid SiteId, string Nom, string Code, string? NumeroUrgence);

internal sealed record SitePublicApi(string Site, string Organisation);

internal sealed record AccuseApi(string Site, bool Danger, string? NumeroUrgence);

internal sealed record DepotRecuApi(
    Guid Id, DateTimeOffset RecuLe, string Nature, bool Danger, bool ActiviteBloquee, string Emplacement, string Description,
    string NomDeclarant, string? ContactDeclarant, IReadOnlyList<int> Photos);

internal sealed record CreeApi(Guid Id);

internal sealed record ZoneApi(Guid Id, string Nom, Guid? NiveauId, bool PartiesCommunes);

internal sealed record NiveauApi(Guid Id, string Nom, int Ordre);

internal sealed record BatimentApi(Guid Id, string Nom, IReadOnlyList<NiveauApi> Niveaux, IReadOnlyList<ZoneApi> Zones);

internal sealed record RepartitionApi(int Bon, int Satisfaisant, int Degrade, int Mauvais, int Critique);

internal sealed record LotApi(string Unite, int Quantite, RepartitionApi? Repartition);

internal sealed record ComposantApi(
    Guid Id, Guid ZoneId, string Nom, string CorpsEtat, string? Emplacement, string? Marque, string? Modele, string? NumeroSerie,
    int? AnneeMiseEnService, int? MoisMiseEnService, string? Remarques, bool SoumisAVerification, LotApi? Lot,
    DateTimeOffset? RetireLe, string? MotifRetrait, uint Version);

internal sealed record ListeComposantsApi(IReadOnlyList<ComposantApi> Composants, bool Tronquee);

internal sealed record CompteZoneApi(Guid ZoneId, int Actifs);

internal sealed record EvenementApi(
    DateTimeOffset Le, string Nature, Guid? AuteurCompteId, string? Auteur, System.Text.Json.JsonElement? Avant, System.Text.Json.JsonElement? Apres);

internal sealed record FicheComposantApi(ComposantApi Composant, IReadOnlyList<EvenementApi> Historique);

internal sealed record DossierResumeApi(
    Guid Id, string Titre, string Statut, string? Priorite, Guid? ZoneId, string? Zone, bool Danger, bool ActiviteBloquee, string Origine,
    DateTimeOffset RecuLe, DateTimeOffset? EcheancePriseEnCharge, DateTimeOffset? EcheanceResolution);

internal sealed record LieuApi(Guid ZoneId, string Zone, string Batiment, Guid? ComposantId, string? Composant, string? CorpsEtat, bool PhotoApresIntervention);

internal sealed record InterventionApi(
    int Numero, string Titre, string Statut, Guid? IntervenantCompteId, string? Intervenant, string? Prestataire, Guid? ReferentCompteId,
    string? Referent, DateTimeOffset CreeLe, DateTimeOffset? DemarreeLe, string? MotifAttente, string? CommentaireAttente, DateOnly? ReexamenLe,
    string? Actions, string? EtatFinal, DateTimeOffset? ClotureeLe, IReadOnlyList<int> Photos, bool PhotoExigee);

internal sealed record EvenementDossierApi(DateTimeOffset Le, string Nature, Guid? AuteurCompteId, string? Auteur, System.Text.Json.JsonElement? Detail);

internal sealed record DossierApi(
    Guid Id, string Titre, string Statut, string Nature, string Description, string Emplacement, bool Danger, bool ActiviteBloquee,
    string Origine, DateTimeOffset RecuLe, string NomDeclarant, string? ContactDeclarant, IReadOnlyList<int> Photos, string? Priorite,
    LieuApi? Lieu, DateTimeOffset? QualifieLe, Guid? DoublonDe, string? MotifClassement, string? CommentaireClassement,
    DateTimeOffset? AffecteLe, DateTimeOffset? EcheancePriseEnCharge, DateTimeOffset? EcheanceResolution, DateTimeOffset? AttenteDepuis,
    DateOnly? ReexamenLe, long DureeAttenteSecondes, DateTimeOffset? ClotureLe, IReadOnlyList<InterventionApi> Interventions,
    IReadOnlyList<DossierResumeApi> Doublons, IReadOnlyList<DossierResumeApi> Suggestions, IReadOnlyList<EvenementDossierApi> Historique, uint Version);

internal sealed record IntervenantApi(Guid CompteId, string Nom, IReadOnlyCollection<string> Roles);

internal sealed record MonInterventionApi(
    Guid DossierId, int Numero, string Titre, string Statut, string Dossier, string? Priorite, string Emplacement, bool Danger,
    DateTimeOffset? EcheancePriseEnCharge, DateTimeOffset? EcheanceResolution, bool Prestataire);

internal sealed record NumeroApi(int Numero);

internal sealed record MesureGrilleApi(string Libelle, string Unite);

internal sealed record GrilleApi(
    string CorpsEtat, int Version, bool Adaptee, IReadOnlyList<string> Criteres, IReadOnlyList<MesureGrilleApi> Mesures,
    DateTimeOffset? AdapteeLe = null, Guid? AuteurCompteId = null, string? Auteur = null);

internal sealed record ContenuGrilleApi(IReadOnlyList<string> Criteres, IReadOnlyList<MesureGrilleApi> Mesures);

internal sealed record VersionCreeeApi(int Version);

internal sealed record PointEvalueApi(string Texte, bool? Conforme);

internal sealed record MesureEvalueeApi(string Libelle, string Unite, decimal? Valeur);

internal sealed record EvaluationApi(
    Guid Id, DateTimeOffset EvalueLe, Guid EvaluateurCompteId, string? Evaluateur, int GrilleVersion, string? Etat, RepartitionApi? Repartition,
    decimal Note, string Libelle, IReadOnlyList<PointEvalueApi> Points, IReadOnlyList<MesureEvalueeApi> Mesures, string? Remarque, int Photos);

internal sealed record EvaluationCreeeApi(Guid Id, decimal Note, string Libelle, bool ProposerDossier);

internal sealed record CouvertureApi(int Evalues, int Actifs);

internal sealed record PointHistoriqueApi(string Mois, decimal? Note, string? Libelle);

internal sealed record CritiqueApi(Guid Id, string Nom, Guid ZoneId);

internal sealed record ZoneNoteeApi(
    Guid ZoneId, string Nom, Guid BatimentId, string Batiment, string? Niveau, bool PartiesCommunes, decimal? Note, string? Libelle,
    CouvertureApi Couverture, int Anciennes, int AEvaluer, int Critiques, DateTimeOffset? PlusAncienne, IReadOnlyList<PointHistoriqueApi> Historique);

internal sealed record ScoreApi(
    decimal? Note, string? Libelle, CouvertureApi Couverture, decimal PartAncienne, IReadOnlyList<CritiqueApi> Critiques, IReadOnlyList<ZoneNoteeApi> Zones,
    IReadOnlyList<PointHistoriqueApi> Historique);

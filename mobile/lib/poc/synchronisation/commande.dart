/// POC PREP-06 (expérimental) : commande hors connexion et son état local.
/// Protocole : docs/dev/SYNCHRONISATION.md.
library;

/// État local d'une commande : elle ne quitte la file qu'après accusé durable
/// du serveur ; un conflit ou un refus reste sur l'appareil, récupérable.
enum EtatCommande { enAttente, conflit, rejetee }

/// Modification du texte d'une note de terrain, préparée hors connexion.
class Commande {
  const Commande({
    required this.operationId,
    required this.deviceId,
    required this.acteurId,
    required this.organisationId,
    required this.siteId,
    required this.noteId,
    required this.baseVersion,
    required this.texte,
    required this.observeLe,
    this.etat = EtatCommande.enAttente,
    this.detail,
  });

  factory Commande.fromJson(Map<String, Object?> json) => Commande(
    operationId: json['operationId']! as String,
    deviceId: json['deviceId']! as String,
    acteurId: json['acteurId']! as String,
    organisationId: json['organisationId']! as String,
    siteId: json['siteId']! as String,
    noteId: json['noteId']! as String,
    baseVersion: json['baseVersion']! as int,
    texte: json['texte']! as String,
    observeLe: DateTime.parse(json['observeLe']! as String),
    etat: EtatCommande.values.byName(json['etat']! as String),
    detail: json['detail'] as String?,
  );

  final String operationId;
  final String deviceId;
  final String acteurId;
  final String organisationId;
  final String siteId;
  final String noteId;
  final int baseVersion;
  final String texte;
  final DateTime observeLe;
  final EtatCommande etat;
  final String? detail;

  Commande avecEtat(EtatCommande nouvelEtat, String? nouveauDetail) => Commande(
    operationId: operationId,
    deviceId: deviceId,
    acteurId: acteurId,
    organisationId: organisationId,
    siteId: siteId,
    noteId: noteId,
    baseVersion: baseVersion,
    texte: texte,
    observeLe: observeLe,
    etat: nouvelEtat,
    detail: nouveauDetail,
  );

  /// Corps envoyé au serveur (sans l'état local).
  Map<String, Object?> versServeur() => {
    'operationId': operationId,
    'deviceId': deviceId,
    'acteurId': acteurId,
    'siteId': siteId,
    'noteId': noteId,
    'baseVersion': baseVersion,
    'texte': texte,
    'observeLe': observeLe.toUtc().toIso8601String(),
  };

  Map<String, Object?> toJson() => {
    ...versServeur(),
    'organisationId': organisationId,
    'etat': etat.name,
    'detail': detail,
  };
}

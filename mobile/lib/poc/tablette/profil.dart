/// POC-03 (S04-01, expérimental) : profil d'un compte sur une tablette partagée
/// (RG-23, R13-04, R13-05, T-01, T-02).
///
/// Fichiers du profil, dans `profils/<compte>/` :
/// - `envoi.cle` + `envoi.db` : file d'envoi, jetons et état ; clé aléatoire
///   chiffrée par la clé Keystore `poc03.<compte>.envoi` (sans PIN) ;
/// - `consultation.cle` : clé aléatoire des bases de consultation, scellée par
///   une clé dérivée du PIN (Argon2id), puis chiffrée par la clé Keystore
///   `poc03.<compte>.consultation` (non extractible, tablette déverrouillée) ;
/// - `consultation_<organisation>.db` : instantané en lecture d'une
///   organisation. Une base par organisation : l'accès coupé par un
///   administrateur s'efface en supprimant le fichier, même profil verrouillé.
///
/// Copier les fichiers ailleurs ne sert à rien : les clés du Keystore ne
/// quittent pas l'appareil. Sur l'appareil, deviner le PIN demande Keystore et
/// Argon2id à chaque essai ; 5 échecs bloquent le profil jusqu'à une connexion
/// en ligne (limite appliquée par l'application, risque consigné).
library;

import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:sqlite3/sqlite3.dart';

import 'base_chiffree.dart';
import 'cryptographie.dart';
import 'file_envoi.dart';
import 'limite.dart';
import 'plateforme.dart';

enum ResultatDeverrouillage { ouvert, pinFaux, bloque, expire, revoque }

class Deverrouillage {
  const Deverrouillage(this.resultat, {this.essaisRestants});

  final ResultatDeverrouillage resultat;
  final int? essaisRestants;
}

/// Jetons de la session du compte SUR CET APPAREIL.
class Jetons {
  const Jetons(this.acces, this.renouvellement);

  factory Jetons.depuisJson(Map<String, Object?> json) =>
      Jetons(json['acces']! as String, json['renouvellement']! as String);

  final String acces;
  final String renouvellement;

  Map<String, Object?> versJson() => {
    'acces': acces,
    'renouvellement': renouvellement,
  };
}

/// Contact réussi : renouvellement accepté ET adhésions relues (R13-04).
class Contact {
  const Contact({
    required this.serveurMs,
    required this.jetons,
    required this.organisations,
  });

  final int serveurMs;
  final Jetons jetons;

  /// Organisations encore accessibles depuis cet appareil.
  final Set<String> organisations;
}

class ProfilVerrouille implements Exception {
  const ProfilVerrouille();

  @override
  String toString() => 'ProfilVerrouille';
}

/// Écrit [contenu] de façon atomique : jamais un fichier de clé à moitié écrit.
void _ecrireAtomique(File fichier, Uint8List contenu) {
  final temporaire = File('${fichier.path}.tmp')
    ..writeAsBytesSync(contenu, flush: true);
  temporaire.renameSync(fichier.path);
}

final _identifiant = RegExp(r'^[0-9a-fA-F-]{36}$');

class ProfilTablette {
  ProfilTablette._(
    this.compteId,
    this._dossier,
    this._coffre,
    this._horloge,
    this.file,
    this.limite,
  );

  static const maxEchecs = 5;
  static const _cleEchecs = 'echecs';
  static const _cleContact = 'contact';
  static const _clePointage = 'pointage';
  static const _cleJetons = 'jetons';
  static const _cleOrganisations = 'organisations';
  static const _cleRevoque = 'revoque';

  final String compteId;
  final Directory _dossier;
  final Coffre _coffre;
  final HorlogeAppareil _horloge;

  /// Toujours disponible, même verrouillé (envoi en arrière-plan).
  final FileEnvoi file;

  /// Limite hors connexion (24 h ; raccourcie dans les essais).
  final Duration limite;

  Uint8List? _cleConsultation;
  final Map<String, Database> _consultations = {};

  String get _aliasEnvoi => 'poc03.$compteId.envoi';
  String get _aliasConsultation => 'poc03.$compteId.consultation';
  File get _fichierCleConsultation => File('${_dossier.path}/consultation.cle');
  String _cheminConsultation(String organisationId) =>
      '${_dossier.path}/consultation_$organisationId.db';

  static Directory dossierDu(Directory racine, String compteId) {
    if (!_identifiant.hasMatch(compteId)) {
      throw ArgumentError.value(compteId, 'compteId', 'identifiant attendu');
    }

    return Directory('${racine.path}/profils/$compteId');
  }

  /// Comptes préparés sur cette tablette.
  static List<String> presents(Directory racine) {
    final profils = Directory('${racine.path}/profils');
    if (!profils.existsSync()) {
      return const [];
    }

    return profils
        .listSync()
        .whereType<Directory>()
        .map((d) => d.uri.pathSegments.where((s) => s.isNotEmpty).last)
        .where(_identifiant.hasMatch)
        .where((id) => File('${profils.path}/$id/envoi.cle').existsSync())
        .toList()
      ..sort();
  }

  /// Préparation en ligne (après connexion par mot de passe) : clés, file et
  /// clé de consultation scellée par le [pin].
  static Future<ProfilTablette> preparer({
    required Directory racine,
    required String compteId,
    required String pin,
    required Contact contact,
    required Coffre coffre,
    required HorlogeAppareil horloge,
    ParametresArgon2 parametres = ParametresArgon2.owasp,
    Duration limite = limiteHorsConnexion,
  }) async {
    verifierFormatPin(pin);
    final dossier = dossierDu(racine, compteId);
    if (File('${dossier.path}/envoi.cle').existsSync()) {
      // Recréer la clé de la file rendrait illisibles les saisies en attente.
      throw StateError('Profil déjà préparé : reprendreEnLigne.');
    }

    dossier.createSync(recursive: true);
    final cleEnvoi = octetsAleatoires(32);
    await coffre.creerCle('poc03.$compteId.envoi', deverrouillageExige: false);
    _ecrireAtomique(
      File('${dossier.path}/envoi.cle'),
      await coffre.chiffrer('poc03.$compteId.envoi', cleEnvoi),
    );
    final file = FileEnvoi(
      ouvrirBaseChiffree('${dossier.path}/envoi.db', cleEnvoi),
    );
    effacer(cleEnvoi);
    final profil = ProfilTablette._(
      compteId,
      dossier,
      coffre,
      horloge,
      file,
      limite,
    );
    await profil._nouvelleCleConsultation(pin, parametres);
    await profil.enregistrerContact(contact);
    return profil;
  }

  /// Charge un profil préparé, verrouillé : seule la file est ouverte.
  static Future<ProfilTablette> charger({
    required Directory racine,
    required String compteId,
    required Coffre coffre,
    required HorlogeAppareil horloge,
    Duration limite = limiteHorsConnexion,
  }) async {
    final dossier = dossierDu(racine, compteId);
    final cleEnvoi = await coffre.dechiffrer(
      'poc03.$compteId.envoi',
      File('${dossier.path}/envoi.cle').readAsBytesSync(),
    );
    final file = FileEnvoi(
      ouvrirBaseChiffree('${dossier.path}/envoi.db', cleEnvoi),
    );
    effacer(cleEnvoi);
    return ProfilTablette._(compteId, dossier, coffre, horloge, file, limite);
  }

  static void verifierFormatPin(String pin) {
    if (!RegExp(r'^\d{6}$').hasMatch(pin)) {
      throw ArgumentError.value('******', 'pin', 'six chiffres attendus');
    }
  }

  bool get deverrouille => _cleConsultation != null;

  int get echecs => (file.lire(_cleEchecs)?['n'] as int?) ?? 0;

  bool get bloque => echecs >= maxEchecs;

  bool get revoque => file.lire(_cleRevoque) != null;

  Jetons? get jetons {
    final json = file.lire(_cleJetons);
    return json == null ? null : Jetons.depuisJson(json);
  }

  /// Rotation : le nouveau jeton de renouvellement remplace l'ancien.
  void enregistrerJetons(Jetons jetons) =>
      file.ecrire(_cleJetons, jetons.versJson());

  /// Session refusée (expirée, rejeu) : connexion requise, rien d'effacé.
  void oublierSession() => file.ecrire(_cleJetons, null);

  Set<String> get organisations =>
      ((file.lire(_cleOrganisations)?['ids'] as List<Object?>?) ?? const [])
          .cast<String>()
          .toSet();

  DernierContact? get dernierContact {
    final json = file.lire(_cleContact);
    return json == null ? null : DernierContact.depuisJson(json);
  }

  /// Temps hors connexion consommé ; le pointage est enregistré pour survivre
  /// à un redémarrage.
  Future<EtatLimite> etatLimite() async {
    final contact = dernierContact!;
    final pointage = file.lire(_clePointage);
    final etat = evaluerLimite(
      contact: contact,
      pointage: pointage == null ? null : Pointage.depuisJson(pointage),
      maintenant: await _horloge.lire(),
      limite: limite,
    );
    file.ecrire(_clePointage, etat.pointage.versJson());
    return etat;
  }

  /// Heure de saisie sur l'horloge du serveur (R14-06).
  Future<({int serveurMs, bool garantie})> heureDeSaisie() async {
    final etat = await etatLimite();
    return (
      serveurMs: dernierContact!.serveurMs + etat.consomme.inMilliseconds,
      garantie: etat.heureGarantie,
    );
  }

  /// Déverrouillage par PIN. Le compteur d'échecs est augmenté AVANT l'essai :
  /// arrêter l'application pendant la vérification ne rend pas d'essai.
  Future<Deverrouillage> deverrouiller(String pin) async {
    if (revoque) {
      return const Deverrouillage(ResultatDeverrouillage.revoque);
    }

    if (bloque) {
      return const Deverrouillage(ResultatDeverrouillage.bloque);
    }

    if ((await etatLimite()).expire) {
      return const Deverrouillage(ResultatDeverrouillage.expire);
    }

    final tentative = echecs + 1;
    file.ecrire(_cleEchecs, {'n': tentative});
    final Uint8List cle;
    try {
      // PIN mal formé : compté comme un PIN faux.
      cle = RegExp(r'^\d{6}$').hasMatch(pin)
          ? await _ouvrirCleConsultation(pin)
          : throw const ScellementInvalide();
    } on ScellementInvalide {
      return tentative >= maxEchecs
          ? const Deverrouillage(ResultatDeverrouillage.bloque)
          : Deverrouillage(
              ResultatDeverrouillage.pinFaux,
              essaisRestants: maxEchecs - tentative,
            );
    }

    file.ecrire(_cleEchecs, {'n': 0});
    _cleConsultation = cle;
    return const Deverrouillage(ResultatDeverrouillage.ouvert);
  }

  Future<Uint8List> _ouvrirCleConsultation(String pin) async {
    final enveloppe = jsonDecode(
      utf8.decode(
        await _coffre.dechiffrer(
          _aliasConsultation,
          _fichierCleConsultation.readAsBytesSync(),
        ),
      ),
    ) as Map<String, Object?>;
    final cleDuPin = deriverCleDuPin(
      pin,
      base64Decode(enveloppe['sel']! as String),
      ParametresArgon2.depuisJson(enveloppe['argon2']! as Map<String, Object?>),
    );
    try {
      return ouvrir(cleDuPin, base64Decode(enveloppe['scelle']! as String));
    } finally {
      effacer(cleDuPin);
    }
  }

  Future<void> _nouvelleCleConsultation(
    String pin,
    ParametresArgon2 parametres,
  ) async {
    final sel = octetsAleatoires(16);
    final cle = octetsAleatoires(32);
    final cleDuPin = deriverCleDuPin(pin, sel, parametres);
    final enveloppe = utf8.encode(
      jsonEncode({
        'sel': base64Encode(sel),
        'argon2': parametres.versJson(),
        'scelle': base64Encode(sceller(cleDuPin, cle)),
      }),
    );
    effacer(cleDuPin);
    effacer(cle);
    await _coffre.creerCle(_aliasConsultation, deverrouillageExige: true);
    _ecrireAtomique(
      _fichierCleConsultation,
      await _coffre.chiffrer(_aliasConsultation, enveloppe),
    );
  }

  /// Ferme les bases de consultation et efface la clé de la mémoire.
  void verrouiller() {
    for (final base in _consultations.values) {
      base.close();
    }
    _consultations.clear();
    effacer(_cleConsultation);
    _cleConsultation = null;
  }

  /// Base de consultation d'une organisation accessible ; profil déverrouillé.
  Database consultation(String organisationId) {
    final cle = _cleConsultation;
    if (cle == null) {
      throw const ProfilVerrouille();
    }

    if (!organisations.contains(organisationId)) {
      throw StateError('Organisation non accessible depuis cet appareil.');
    }

    return _consultations.putIfAbsent(organisationId, () {
      final base = ouvrirBaseChiffree(_cheminConsultation(organisationId), cle)
        ..execute(
          'CREATE TABLE IF NOT EXISTS elements ('
          'id TEXT PRIMARY KEY, site_id TEXT NOT NULL, contenu TEXT NOT NULL)',
        );
      return base;
    });
  }

  /// Remplace l'instantané d'une organisation (téléchargé en ligne).
  void remplacerInstantane(
    String organisationId,
    List<Map<String, Object?>> elements,
  ) {
    final base = consultation(organisationId)
      ..execute('BEGIN IMMEDIATE')
      ..execute('DELETE FROM elements');
    try {
      final insertion = base.prepare(
        'INSERT INTO elements (id, site_id, contenu) VALUES (?, ?, ?)',
      );
      for (final e in elements) {
        insertion.execute([e['id'], e['siteId'], jsonEncode(e['contenu'])]);
      }
      insertion.close();
      base.execute('COMMIT');
    } catch (_) {
      base.execute('ROLLBACK');
      rethrow;
    }
  }

  /// Contact réussi : nouveau départ des 24 h ; les organisations qui ne sont
  /// plus accessibles depuis cet appareil sont effacées immédiatement.
  Future<void> enregistrerContact(Contact contact) async {
    final instant = await _horloge.lire();
    for (final organisation in organisations.difference(
      contact.organisations,
    )) {
      _consultations.remove(organisation)?.close();
      supprimerBase(_cheminConsultation(organisation));
    }
    file
      ..ecrire(
        _cleContact,
        DernierContact.maintenant(contact.serveurMs, instant).versJson(),
      )
      ..ecrire(_clePointage, null)
      ..ecrire(_cleJetons, contact.jetons.versJson())
      ..ecrire(_cleOrganisations, {'ids': contact.organisations.toList()})
      ..ecrire(_cleRevoque, null);
  }

  /// Après connexion en ligne par mot de passe (PIN oublié, profil bloqué ou
  /// session révoquée) : instantané effacé (clé détruite, puis fichiers), nouveau
  /// PIN, compteur remis à zéro. La file d'envoi est intacte.
  Future<void> reprendreEnLigne({
    required String nouveauPin,
    required Contact contact,
    ParametresArgon2 parametres = ParametresArgon2.owasp,
  }) async {
    verifierFormatPin(nouveauPin);
    await _effacerConsultation();
    await _nouvelleCleConsultation(nouveauPin, parametres);
    file.ecrire(_cleEchecs, {'n': 0});
    await enregistrerContact(contact);
  }

  /// Session du compte révoquée sur cet appareil (par son titulaire, R14 Q8) :
  /// consultation détruite (clé d'abord), jetons supprimés. Les saisies restent,
  /// pour être envoyées par le même compte après une nouvelle connexion.
  Future<void> revoquer() async {
    await _effacerConsultation();
    file
      ..ecrire(_cleJetons, null)
      ..ecrire(_cleOrganisations, {'ids': <String>[]})
      ..ecrire(_cleRevoque, {'le': DateTime.now().toUtc().toIso8601String()});
  }

  Future<void> _effacerConsultation() async {
    verrouiller();
    await _coffre.supprimerCle(_aliasConsultation);
    if (_fichierCleConsultation.existsSync()) {
      _fichierCleConsultation.deleteSync();
    }
    for (final fichier in _dossier.listSync().whereType<File>()) {
      final nom = fichier.uri.pathSegments.last;
      if (nom.startsWith('consultation_') && nom.endsWith('.db')) {
        supprimerBase(fichier.path);
      }
    }
  }

  /// Retrait du profil de la tablette (refusé si des saisies attendent).
  Future<void> retirer() async {
    if (file.commandes.isNotEmpty) {
      throw StateError('Des saisies attendent leur envoi.');
    }

    await _effacerConsultation();
    file.fermer();
    await _coffre.supprimerCle(_aliasEnvoi);
    _dossier.deleteSync(recursive: true);
  }

  void fermer() {
    verrouiller();
    file.fermer();
  }
}

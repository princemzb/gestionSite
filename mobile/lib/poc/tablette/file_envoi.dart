/// POC-03 (S04-01, expérimental) : file d'envoi d'un profil (T-02).
///
/// Base SQLCipher propre au compte, dont la clé est protégée par une clé du
/// Keystore SANS le PIN : l'envoi en arrière-plan fonctionne profil verrouillé,
/// et un PIN oublié ou un blocage ne fait perdre aucune saisie. Elle contient
/// aussi les jetons et l'état du profil (dernier contact, échecs de PIN).
/// Risque résiduel consigné dans POC-03 : l'application peut lire ces saisies
/// sans le PIN.
library;

import 'dart:convert';
import 'dart:typed_data';

import 'package:pointycastle/export.dart';
import 'package:sqlite3/sqlite3.dart';

/// État local d'une commande : elle ne quitte la file qu'après accusé durable.
enum EtatEnvoi { enAttente, conflit, rejetee }

class PhotoEnAttente {
  const PhotoEnAttente({
    required this.fichierId,
    required this.taille,
    required this.empreinte,
    required this.complete,
  });

  final String fichierId;
  final int taille;

  /// SHA-256 hexadécimal, vérifié par le serveur à la fin de l'envoi.
  final String empreinte;
  final bool complete;
}

class CommandeTablette {
  const CommandeTablette({
    required this.operationId,
    required this.acteurId,
    required this.organisationId,
    required this.type,
    required this.corps,
    required this.saisieServeurMs,
    required this.heureGarantie,
    this.fichiers = const [],
    this.etat = EtatEnvoi.enAttente,
    this.detail,
  });

  final String operationId;
  final String acteurId;
  final String organisationId;
  final String type;
  final Map<String, Object?> corps;

  /// Heure de saisie estimée sur l'horloge du serveur (R14-06).
  final int saisieServeurMs;
  final bool heureGarantie;
  final List<String> fichiers;
  final EtatEnvoi etat;
  final String? detail;

  Map<String, Object?> versServeur(String appareilId) => {
    'operationId': operationId,
    'appareilId': appareilId,
    'acteurId': acteurId,
    'type': type,
    'corps': corps,
    'fichiers': fichiers,
    'saisieLe': DateTime.fromMillisecondsSinceEpoch(
      saisieServeurMs,
      isUtc: true,
    ).toIso8601String(),
    'heureGarantie': heureGarantie,
  };
}

/// Empreinte SHA-256 hexadécimale.
String empreinteSha256(Uint8List octets) => SHA256Digest()
    .process(octets)
    .map((o) => o.toRadixString(16).padLeft(2, '0'))
    .join();

class FileEnvoi {
  FileEnvoi(this._base) {
    _base.execute('''
      CREATE TABLE IF NOT EXISTS etat (cle TEXT PRIMARY KEY, valeur TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS commandes (
        rang INTEGER PRIMARY KEY AUTOINCREMENT,
        operation_id TEXT NOT NULL UNIQUE,
        acteur_id TEXT NOT NULL,
        organisation_id TEXT NOT NULL,
        type TEXT NOT NULL,
        corps TEXT NOT NULL,
        fichiers TEXT NOT NULL,
        saisie_serveur_ms INTEGER NOT NULL,
        heure_garantie INTEGER NOT NULL,
        etat TEXT NOT NULL,
        detail TEXT
      );
      CREATE TABLE IF NOT EXISTS photos (
        fichier_id TEXT PRIMARY KEY,
        operation_id TEXT NOT NULL REFERENCES commandes(operation_id),
        octets BLOB NOT NULL,
        empreinte TEXT NOT NULL,
        complete INTEGER NOT NULL DEFAULT 0
      );
    ''');
  }

  final Database _base;

  Map<String, Object?>? lire(String cle) {
    final lignes = _base.select('SELECT valeur FROM etat WHERE cle = ?', [cle]);
    return lignes.isEmpty
        ? null
        : jsonDecode(lignes.first['valeur'] as String) as Map<String, Object?>;
  }

  void ecrire(String cle, Map<String, Object?>? valeur) {
    if (valeur == null) {
      _base.execute('DELETE FROM etat WHERE cle = ?', [cle]);
    } else {
      _base.execute(
        'INSERT INTO etat (cle, valeur) VALUES (?, ?) '
        'ON CONFLICT (cle) DO UPDATE SET valeur = excluded.valeur',
        [cle, jsonEncode(valeur)],
      );
    }
  }

  /// Enregistre la commande ET ses photos dans une seule transaction, avant
  /// toute annonce de succès : jamais l'une sans les autres.
  void ajouter(CommandeTablette commande, {List<Uint8List> photos = const []}) {
    if (photos.length != commande.fichiers.length) {
      throw ArgumentError('Une photo par fichier annoncé.');
    }

    _transaction(() {
      _base.execute(
        'INSERT INTO commandes (operation_id, acteur_id, organisation_id, type, '
        'corps, fichiers, saisie_serveur_ms, heure_garantie, etat, detail) '
        'VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
        [
          commande.operationId,
          commande.acteurId,
          commande.organisationId,
          commande.type,
          jsonEncode(commande.corps),
          jsonEncode(commande.fichiers),
          commande.saisieServeurMs,
          if (commande.heureGarantie) 1 else 0,
          commande.etat.name,
          commande.detail,
        ],
      );
      for (var i = 0; i < photos.length; i++) {
        _base.execute(
          'INSERT INTO photos (fichier_id, operation_id, octets, empreinte) '
          'VALUES (?, ?, ?, ?)',
          [
            commande.fichiers[i],
            commande.operationId,
            photos[i],
            empreinteSha256(photos[i]),
          ],
        );
      }
    });
  }

  List<CommandeTablette> get commandes => _base
      .select('SELECT * FROM commandes ORDER BY rang')
      .map(
        (l) => CommandeTablette(
          operationId: l['operation_id'] as String,
          acteurId: l['acteur_id'] as String,
          organisationId: l['organisation_id'] as String,
          type: l['type'] as String,
          corps: jsonDecode(l['corps'] as String) as Map<String, Object?>,
          fichiers: (jsonDecode(l['fichiers'] as String) as List<Object?>)
              .cast<String>(),
          saisieServeurMs: l['saisie_serveur_ms'] as int,
          heureGarantie: l['heure_garantie'] == 1,
          etat: EtatEnvoi.values.byName(l['etat'] as String),
          detail: l['detail'] as String?,
        ),
      )
      .toList();

  List<CommandeTablette> get enAttente =>
      commandes.where((c) => c.etat == EtatEnvoi.enAttente).toList();

  List<PhotoEnAttente> photosDe(String operationId) => _base
      .select(
        'SELECT fichier_id, length(octets) AS taille, empreinte, complete '
        'FROM photos WHERE operation_id = ? ORDER BY rowid',
        [operationId],
      )
      .map(
        (l) => PhotoEnAttente(
          fichierId: l['fichier_id'] as String,
          taille: l['taille'] as int,
          empreinte: l['empreinte'] as String,
          complete: l['complete'] == 1,
        ),
      )
      .toList();

  /// Tranche [debut, fin[ d'une photo, lue sans charger tout le fichier.
  Uint8List morceau(String fichierId, int debut, int fin) {
    final lignes = _base.select(
      'SELECT substr(octets, ?, ?) AS m FROM photos WHERE fichier_id = ?',
      [debut + 1, fin - debut, fichierId],
    );
    return lignes.first['m'] as Uint8List;
  }

  void marquerPhotoComplete(String fichierId) => _base.execute(
    'UPDATE photos SET complete = 1 WHERE fichier_id = ?',
    [fichierId],
  );

  void marquerPhotoIncomplete(String fichierId) => _base.execute(
    'UPDATE photos SET complete = 0 WHERE fichier_id = ?',
    [fichierId],
  );

  /// Accusé durable du serveur : la commande et ses photos quittent la file.
  void retirer(String operationId) => _transaction(() {
    _base
      ..execute('DELETE FROM photos WHERE operation_id = ?', [operationId])
      ..execute('DELETE FROM commandes WHERE operation_id = ?', [operationId]);
  });

  void marquer(String operationId, EtatEnvoi etat, String? detail) =>
      _base.execute(
        'UPDATE commandes SET etat = ?, detail = ? WHERE operation_id = ?',
        [etat.name, detail, operationId],
      );

  void _transaction(void Function() travail) {
    _base.execute('BEGIN IMMEDIATE');
    try {
      travail();
      _base.execute('COMMIT');
    } catch (_) {
      _base.execute('ROLLBACK');
      rethrow;
    }
  }

  void fermer() => _base.close();
}

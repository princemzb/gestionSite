/// POC-03 (S04-01, expérimental) : ouverture d'une base SQLCipher avec une clé
/// brute de 32 octets (aucune dérivation par SQLCipher : la clé est déjà
/// aléatoire, la dérivation du PIN est faite avant, par Argon2id).
library;

import 'dart:io';
import 'dart:typed_data';

import 'package:sqlite3/sqlite3.dart';

import 'cryptographie.dart';

/// La base ne s'ouvre pas avec cette clé (autre profil, clé détruite, fichier
/// altéré).
class BaseIllisible implements Exception {
  const BaseIllisible(this.chemin);

  final String chemin;

  @override
  String toString() => 'BaseIllisible: $chemin';
}

/// Bibliothèque chargée sans SQLCipher : `PRAGMA key` serait ignoré et la
/// base écrite en clair.
class SqlcipherAbsent implements Exception {
  const SqlcipherAbsent();

  @override
  String toString() => 'SqlcipherAbsent';
}

/// Ouvre (ou crée) la base [chemin] chiffrée par [cle] ; vérifie que SQLCipher
/// est bien chargé, puis la clé en lisant le schéma : SQLCipher ne la contrôle
/// qu'à la première lecture.
Database ouvrirBaseChiffree(String chemin, Uint8List cle) {
  final base = sqlite3.open(chemin);
  try {
    if (base.select('PRAGMA cipher_version;').isEmpty) {
      throw const SqlcipherAbsent();
    }
    base
      ..execute("PRAGMA key = \"x'${enHexa(cle)}'\";")
      // Efface la mémoire de travail de SQLCipher à la libération (clé comprise).
      ..execute('PRAGMA cipher_memory_security = ON;')
      // Premier plan et arrière-plan (WorkManager) ouvrent la même file.
      ..execute('PRAGMA busy_timeout = 5000;')
      ..select('SELECT count(*) FROM sqlite_master;');
    return base;
  } on SqliteException {
    base.close();
    throw BaseIllisible(chemin);
  } on SqlcipherAbsent {
    base.close();
    rethrow;
  }
}

/// Supprime une base et ses fichiers annexes (journal, WAL).
void supprimerBase(String chemin) {
  for (final suffixe in ['', '-journal', '-wal', '-shm']) {
    final fichier = File('$chemin$suffixe');
    if (fichier.existsSync()) {
      fichier.deleteSync();
    }
  }
}

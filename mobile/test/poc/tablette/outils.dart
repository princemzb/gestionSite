// POC-03 : doubles de test du Keystore et de l'horloge (le vrai Keystore est
// exercé sur l'émulateur, integration_test/poc03_test.dart).
import 'dart:typed_data';

import 'package:gestionsite_terrain/poc/tablette/cryptographie.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';

/// Keystore logiciel : AES-256-GCM, clés gardées en mémoire.
class CoffreLogiciel implements Coffre {
  final cles = <String, Uint8List>{};
  final creations = <String, bool>{};

  @override
  Future<NiveauCle> creerCle(
    String alias, {
    required bool deverrouillageExige,
  }) async {
    cles[alias] = octetsAleatoires(32);
    creations[alias] = deverrouillageExige;
    return NiveauCle.logiciel;
  }

  @override
  Future<Uint8List> chiffrer(String alias, Uint8List donnees) async {
    final cle = cles[alias];
    if (cle == null) {
      throw const CleIntrouvable();
    }

    return sceller(cle, donnees);
  }

  @override
  Future<Uint8List> dechiffrer(String alias, Uint8List donnees) async {
    final cle = cles[alias];
    if (cle == null) {
      throw const CleIntrouvable();
    }

    try {
      return ouvrir(cle, donnees);
    } on ScellementInvalide {
      throw const DechiffrementRefuse();
    }
  }

  @override
  Future<void> supprimerCle(String alias) async => cles.remove(alias);
}

/// Horloge pilotée : [avancer] fait passer le temps réel (monotone ET murale),
/// [regler] ne change que l'heure de la tablette, [redemarrer] remet le
/// monotone à zéro.
class HorlogeFactice implements HorlogeAppareil {
  int monotoneMs = 1000000;
  int demarrages = 3;
  int muraleMs = DateTime.utc(2026, 10, 30, 8).millisecondsSinceEpoch;

  void avancer(Duration duree) {
    monotoneMs += duree.inMilliseconds;
    muraleMs += duree.inMilliseconds;
  }

  void regler(Duration decalage) => muraleMs += decalage.inMilliseconds;

  void redemarrer({required Duration eteinte}) {
    demarrages++;
    monotoneMs = 0;
    muraleMs += eteinte.inMilliseconds;
  }

  @override
  Future<InstantAppareil> lire() async => InstantAppareil(
    monotoneMs: monotoneMs,
    demarrages: demarrages,
    muraleMs: muraleMs,
  );
}

/// Argon2id allégé pour des tests rapides (le coût réel est mesuré à part).
const argon2Test = ParametresArgon2(memoireKio: 256, iterations: 1);

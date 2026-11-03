/// POC-03 (S04-01, expérimental) : primitives de chiffrement côté Dart.
///
/// - Argon2id : dérive une clé du PIN à 6 chiffres (lent exprès, pour freiner
///   les essais sur l'appareil) ;
/// - AES-256-GCM : scelle une clé de base avec la clé dérivée du PIN.
///
/// Implémentation : pointycastle (MIT), en Dart pur ; temps mesurés dans
/// docs/poc/POC-03.md.
library;

import 'dart:math';
import 'dart:typed_data';

import 'package:pointycastle/export.dart';

final Random _aleatoire = Random.secure();

/// Octets aléatoires cryptographiques (générateur du système).
Uint8List octetsAleatoires(int nombre) => Uint8List.fromList(
  List<int>.generate(nombre, (_) => _aleatoire.nextInt(256)),
);

/// Paramètres d'Argon2id, conservés avec la clé scellée pour pouvoir les
/// renforcer plus tard sans rendre les profils existants illisibles.
class ParametresArgon2 {
  const ParametresArgon2({
    required this.memoireKio,
    required this.iterations,
    this.parallelisme = 1,
  });

  factory ParametresArgon2.depuisJson(Map<String, Object?> json) =>
      ParametresArgon2(
        memoireKio: json['m']! as int,
        iterations: json['t']! as int,
        parallelisme: json['p']! as int,
      );

  /// Recommandation OWASP (Argon2id, m = 19 Mio, t = 2, p = 1).
  static const owasp = ParametresArgon2(memoireKio: 19456, iterations: 2);

  final int memoireKio;
  final int iterations;
  final int parallelisme;

  Map<String, Object?> versJson() => {
    'm': memoireKio,
    't': iterations,
    'p': parallelisme,
  };
}

/// Clé de 32 octets dérivée du [pin] et du [sel] propre au profil.
Uint8List deriverCleDuPin(
  String pin,
  Uint8List sel,
  ParametresArgon2 parametres,
) {
  final generateur = Argon2BytesGenerator()
    ..init(
      Argon2Parameters(
        Argon2Parameters.ARGON2_id,
        sel,
        desiredKeyLength: 32,
        iterations: parametres.iterations,
        memory: parametres.memoireKio,
        lanes: parametres.parallelisme,
        version: Argon2Parameters.ARGON2_VERSION_13,
      ),
    );
  return generateur.process(Uint8List.fromList(pin.codeUnits));
}

/// Données altérées, ou ouvertes avec une autre clé (PIN faux).
class ScellementInvalide implements Exception {
  const ScellementInvalide();

  @override
  String toString() => 'ScellementInvalide';
}

const _tailleNonce = 12;

/// AES-256-GCM : nonce aléatoire de 12 octets devant le chiffré, étiquette de
/// 128 bits à la fin.
Uint8List sceller(Uint8List cle, Uint8List clair) {
  final nonce = octetsAleatoires(_tailleNonce);
  final chiffre = GCMBlockCipher(AESEngine())
    ..init(true, AEADParameters(KeyParameter(cle), 128, nonce, Uint8List(0)));
  return Uint8List.fromList([...nonce, ...chiffre.process(clair)]);
}

/// Inverse de [sceller] ; lève [ScellementInvalide] si l'étiquette ne
/// correspond pas.
Uint8List ouvrir(Uint8List cle, Uint8List scelle) {
  if (scelle.length < _tailleNonce + 16) {
    throw const ScellementInvalide();
  }

  final chiffre = GCMBlockCipher(AESEngine())
    ..init(
      false,
      AEADParameters(
        KeyParameter(cle),
        128,
        Uint8List.sublistView(scelle, 0, _tailleNonce),
        Uint8List(0),
      ),
    );
  try {
    return chiffre.process(Uint8List.sublistView(scelle, _tailleNonce));
  } on InvalidCipherTextException {
    throw const ScellementInvalide();
  }
}

/// Efface une clé en mémoire dès qu'elle ne sert plus (limite : le ramasse-
/// miettes de Dart peut en avoir gardé des copies, risque consigné).
void effacer(Uint8List? octets) {
  octets?.fillRange(0, octets.length, 0);
}

/// Représentation hexadécimale (clé brute de SQLCipher : `x'…'`).
String enHexa(Uint8List octets) =>
    octets.map((o) => o.toRadixString(16).padLeft(2, '0')).join();

import 'dart:math';

final Random _aleatoire = Random.secure();

/// UUID version 4 (aléatoire) : identifiant d'opération créé sur l'appareil,
/// avant tout envoi, pour que chaque renvoi porte le même.
String nouvelIdentifiant() {
  final octets = List<int>.generate(16, (_) => _aleatoire.nextInt(256));
  octets[6] = (octets[6] & 0x0f) | 0x40;
  octets[8] = (octets[8] & 0x3f) | 0x80;
  final hex = octets.map((o) => o.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
      '${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}

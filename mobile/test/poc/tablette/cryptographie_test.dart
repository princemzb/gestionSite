import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/cryptographie.dart';

import 'outils.dart';

void main() {
  test(
    'scellement AES-GCM : aller-retour, autre clé et altération refusées',
    () {
      final cle = octetsAleatoires(32);
      final clair = Uint8List.fromList('clé de base'.codeUnits);
      final scelle = sceller(cle, clair);

      expect(ouvrir(cle, scelle), clair);
      expect(sceller(cle, clair), isNot(scelle), reason: 'nonce aléatoire');
      expect(
        () => ouvrir(octetsAleatoires(32), scelle),
        throwsA(isA<ScellementInvalide>()),
      );
      final altere = Uint8List.fromList(scelle)..[20] ^= 1;
      expect(() => ouvrir(cle, altere), throwsA(isA<ScellementInvalide>()));
      expect(
        () => ouvrir(cle, Uint8List(10)),
        throwsA(isA<ScellementInvalide>()),
      );
      expect(const ScellementInvalide().toString(), 'ScellementInvalide');
    },
  );

  test(
    'Argon2id : même PIN et même sel donnent la même clé, sinon une autre',
    () {
      final sel = octetsAleatoires(16);
      final a = deriverCleDuPin('123456', sel, argon2Test);

      expect(a, hasLength(32));
      expect(deriverCleDuPin('123456', sel, argon2Test), a);
      expect(deriverCleDuPin('123457', sel, argon2Test), isNot(a));
      expect(
        deriverCleDuPin('123456', octetsAleatoires(16), argon2Test),
        isNot(a),
      );
    },
  );

  test('paramètres Argon2 sérialisés avec la clé scellée', () {
    final copie = ParametresArgon2.depuisJson(
      ParametresArgon2.owasp.versJson(),
    );

    expect(copie.memoireKio, 19456);
    expect(copie.iterations, 2);
    expect(copie.parallelisme, 1);
  });

  test('effacement et hexadécimal', () {
    final octets = Uint8List.fromList([1, 0xab, 255]);
    expect(enHexa(octets), '01abff');

    effacer(octets);
    effacer(null);
    expect(octets, [0, 0, 0]);
  });
}

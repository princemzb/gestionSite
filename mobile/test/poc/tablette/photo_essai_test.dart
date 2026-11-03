import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/photo_essai.dart';

void main() {
  test('bloc EXIF inséré juste après le début du JPEG, détecté', () {
    final jpeg = Uint8List.fromList([0xff, 0xd8, 0xff, 0xdb, 1, 2]);
    final avec = avecExif(jpeg);

    expect(avec.sublist(0, 4), [0xff, 0xd8, 0xff, 0xe1]);
    final longueur = (avec[4] << 8) | avec[5];
    expect(avec.length, 2 + 2 + longueur + 4);
    expect(avec.sublist(avec.length - 4), [0xff, 0xdb, 1, 2]);
    expect(contientExif(avec), isTrue);
    expect(contientExif(jpeg), isFalse);
    expect(() => avecExif(Uint8List.fromList([1, 2])), throwsArgumentError);
  });

  testWidgets('photo de bruit générée en PNG', (tester) async {
    final png = await tester.runAsync(
      () => photoDEssai(largeur: 16, hauteur: 8),
    );

    expect(png!.sublist(1, 4), 'PNG'.codeUnits);
  });
}

/// POC-03 (S04-01, expérimental) : photos d'essai générées sur l'appareil
/// (aucune vraie photo, aucune position réelle).
library;

import 'dart:async';
import 'dart:math';
import 'dart:typed_data';
import 'dart:ui' as ui;

/// Image PNG de [largeur] × [hauteur] pixels de bruit : la réduire puis la
/// réencoder en JPEG donne un fichier de plusieurs mégaoctets, comme une photo.
Future<Uint8List> photoDEssai({
  required int largeur,
  required int hauteur,
  int graine = 3,
}) async {
  final pixels = Uint8List(largeur * hauteur * 4);
  final mots = pixels.buffer.asUint32List();
  final aleatoire = Random(graine);
  for (var i = 0; i < mots.length; i++) {
    mots[i] = aleatoire.nextInt(1 << 32) | 0xff000000; // opaque
  }

  final termine = Completer<ui.Image>();
  ui.decodeImageFromPixels(
    pixels,
    largeur,
    hauteur,
    ui.PixelFormat.rgba8888,
    termine.complete,
  );
  final image = await termine.future;
  final png = await image.toByteData(format: ui.ImageByteFormat.png);
  image.dispose();
  return png!.buffer.asUint8List();
}

/// Insère dans un JPEG un bloc EXIF avec une orientation (6 : à tourner de
/// 90°) et une position GPS fictive, pour vérifier qu'elles disparaissent.
Uint8List avecExif(Uint8List jpeg, {int orientation = 6}) {
  if (jpeg.length < 2 || jpeg[0] != 0xff || jpeg[1] != 0xd8) {
    throw ArgumentError('JPEG attendu');
  }

  // TIFF petit-boutiste : IFD0 (orientation, pointeur GPS), IFD GPS (latitude).
  final tiff = BytesBuilder()
    ..add([0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00])
    ..add(_u16(2))
    ..add(_entree(0x0112, 3, 1, orientation))
    ..add(_entree(0x8825, 4, 1, 38)) // IFD GPS à l'octet 38
    ..add(_u32(0))
    ..add(_u16(2))
    ..add(_entree(0x0001, 2, 2, 0x4e)) // GPSLatitudeRef = "N"
    ..add(_entree(0x0002, 5, 3, 68)) // GPSLatitude : 3 rationnels à l'octet 68
    ..add(_u32(0))
    ..add([
      ..._u32(48),
      ..._u32(1),
      ..._u32(51),
      ..._u32(1),
      ..._u32(0),
      ..._u32(1),
    ]);
  final charge = [...'Exif'.codeUnits, 0, 0, ...tiff.takeBytes()];
  final longueur = charge.length + 2;
  return Uint8List.fromList([
    0xff,
    0xd8,
    0xff,
    0xe1,
    longueur >> 8,
    longueur & 0xff,
    ...charge,
    ...jpeg.sublist(2),
  ]);
}

List<int> _u16(int v) => [v & 0xff, v >> 8];

List<int> _u32(int v) => [v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff, v >> 24];

List<int> _entree(int etiquette, int type, int nombre, int valeur) => [
  ..._u16(etiquette),
  ..._u16(type),
  ..._u32(nombre),
  ..._u32(valeur),
];

/// Vrai si le JPEG contient un bloc EXIF (APP1 « Exif »).
bool contientExif(Uint8List jpeg) {
  final marque = 'Exif'.codeUnits;
  for (var i = 0; i + 4 <= jpeg.length; i++) {
    if (jpeg[i] == marque[0] &&
        jpeg[i + 1] == marque[1] &&
        jpeg[i + 2] == marque[2] &&
        jpeg[i + 3] == marque[3]) {
      return true;
    }
  }

  return false;
}

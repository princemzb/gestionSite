import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  final appels = <MethodCall>[];
  const plateforme = PlateformeAndroid();

  setUp(() {
    appels.clear();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(PlateformeAndroid.canal, (appel) async {
          appels.add(appel);
          final arguments = appel.arguments as Map<Object?, Object?>?;
          switch (appel.method) {
            case 'infos':
              return {
                'sdk': 34,
                'fabricant': 'Google',
                'modele': 'sdk_gphone64_arm64',
                'appareilSecurise': true,
                'strongBoxDeclaree': false,
              };
            case 'horloge':
              return {'monotoneMs': 5, 'demarrages': 2, 'muraleMs': 9};
            case 'dossier':
              return '/data/user/0/fr.gestionsite.terrain/no_backup';
            case 'creerCle':
              return 'tee';
            case 'chiffrer':
              return Uint8List.fromList([1, 2, 3]);
            case 'dechiffrer':
              final alias = arguments!['alias'];
              if (alias == 'poc03.absente') {
                throw PlatformException(code: 'cle_absente');
              }
              if (alias == 'poc03.alteree') {
                throw PlatformException(code: 'dechiffrement');
              }
              if (alias == 'poc03.autre') {
                throw PlatformException(code: 'keystore');
              }
              return Uint8List.fromList([4]);
            case 'supprimerCle':
              return null;
            case 'reduirePhoto':
              if ((arguments!['octets']! as Uint8List).isEmpty) {
                throw PlatformException(code: 'argument', message: 'illisible');
              }
              return {
                'octets': Uint8List.fromList([0xff, 0xd8]),
                'largeur': 2560,
                'hauteur': 1920,
              };
          }
          return null;
        });
  });

  test('infos, horloge et dossier', () async {
    final infos = await plateforme.infos();
    expect(infos.sdk, 34);
    expect(infos.fabricant, 'Google');
    expect(infos.modele, 'sdk_gphone64_arm64');
    expect(infos.appareilSecurise, isTrue);
    expect(infos.strongBoxDeclaree, isFalse);

    final instant = await plateforme.lire();
    expect(
      [instant.monotoneMs, instant.demarrages, instant.muraleMs],
      [5, 2, 9],
    );
    expect(await plateforme.dossier(), endsWith('no_backup'));
  });

  test('clés : création, chiffrement, erreurs traduites', () async {
    expect(
      await plateforme.creerCle('poc03.a', deverrouillageExige: true),
      NiveauCle.tee,
    );
    expect(appels.last.arguments, {
      'alias': 'poc03.a',
      'deverrouillageExige': true,
    });
    expect(await plateforme.chiffrer('poc03.a', Uint8List(1)), [1, 2, 3]);
    final clair = await plateforme.dechiffrer('poc03.a', Uint8List(1));
    expect(clair, [4]);
    clair.fillRange(0, 1, 0); // effaçable en mémoire (vue du canal : non)
    expect(clair, [0]);
    await expectLater(
      plateforme.dechiffrer('poc03.absente', Uint8List(1)),
      throwsA(isA<CleIntrouvable>()),
    );
    await expectLater(
      plateforme.dechiffrer('poc03.alteree', Uint8List(1)),
      throwsA(isA<DechiffrementRefuse>()),
    );
    await expectLater(
      plateforme.dechiffrer('poc03.autre', Uint8List(1)),
      throwsA(isA<PlatformException>()),
    );
    await plateforme.supprimerCle('poc03.a');
    expect(appels.last.method, 'supprimerCle');
  });

  test('réduction des photos : paramètres du serveur, refus traduit', () async {
    final photo = await plateforme.reduire(Uint8List.fromList([1]));
    expect(appels.last.arguments, containsPair('coteMax', 2560));
    expect(appels.last.arguments, containsPair('qualite', 85));
    expect([photo.largeur, photo.hauteur], [2560, 1920]);
    expect(photo.octets, [0xff, 0xd8]);

    await expectLater(
      plateforme.reduire(Uint8List(0)),
      throwsA(isA<PhotoRefusee>().having((e) => e.motif, 'motif', 'illisible')),
    );
    expect(const PhotoRefusee('x').toString(), 'PhotoRefusee: x');
    expect(const CleIntrouvable().toString(), 'CleIntrouvable');
    expect(const DechiffrementRefuse().toString(), 'DechiffrementRefuse');
  });

  test('écran éteint : flux d\'événements du greffon', () async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockStreamHandler(
          PlateformeAndroid.canalEcran,
          MockStreamHandler.inline(
            onListen: (arguments, events) => events
              ..success(1234)
              ..endOfStream(),
          ),
        );

    expect(await plateforme.ecranEteint.toList(), [1234]);
  });
}

import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/harnais.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';

import 'outils.dart';
import 'serveur_factice.dart';

const compte = '0b6f8a39-6a7c-4c6e-9d43-6c2c1d1f0a01';

class _Reducteur implements ReducteurPhoto {
  @override
  Future<PhotoReduite> reduire(Uint8List octets) async =>
      PhotoReduite(octets, 1, 1);
}

DependancesHarnais _dependances(
  Directory racine, {
  bool securise = true,
  List<String>? programmations,
}) => DependancesHarnais(
  racine: racine,
  coffre: CoffreLogiciel(),
  horloge: HorlogeFactice(),
  reducteur: _Reducteur(),
  infos: () async => InfosAppareil(
    sdk: 34,
    fabricant: 'Google',
    modele: 'émulateur',
    appareilSecurise: securise,
    strongBoxDeclaree: false,
  ),
  photoDEssai: () async => Uint8List.fromList(List.filled(3000, 1)),
  programmerEnvoi: () async => programmations?.add('programmé'),
);

void main() {
  late Directory racine;
  late ServeurFactice serveur;

  setUp(() async {
    racine = Directory.systemTemp.createTempSync('poc03_harnais_');
    serveur = ServeurFactice()
      ..ajouterCompte('a@demo.invalid', compte, 'mot-de-passe-a')
      ..ajouterAdhesion(compte, 'org-x')
      ..definirInstantane('org-x', [
        {'id': 'e1', 'siteId': 's1', 'contenu': 'démo'},
      ]);
    await serveur.demarrer();
  });

  tearDown(() async {
    await serveur.arreter();
    racine.deleteSync(recursive: true);
  });

  testWidgets('écran technique : libellé « sans design », boutons actifs', (
    tester,
  ) async {
    // Format de la tablette (paysage 1600 × 2560 en points) : tout est construit.
    tester.view
      ..physicalSize = const Size(1600, 2560)
      ..devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      MaterialApp(home: HarnaisPoc03(dependances: _dependances(racine))),
    );

    expect(find.textContaining('sans design'), findsOneWidget);
    await tester.tap(find.text('Infos'));
    await tester.pump();
    await tester.tap(find.text('Verrouiller'));
    await tester.pump();
    expect(find.text('Verrouillé'), findsOneWidget);

    await tester.runAsync(() async {
      for (final libelle in [
        'Déverrouiller',
        'État',
        'Connexion et préparation',
        'Saisie avec photo',
        'Synchroniser',
        'Programmer l\'envoi en arrière-plan',
      ]) {
        await tester.tap(find.text(libelle));
      }
      await Future<void>.delayed(const Duration(milliseconds: 200));
    });
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();
    await tester.pumpWidget(const SizedBox());
  });
}

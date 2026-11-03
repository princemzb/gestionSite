import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/verrou.dart';

import 'outils.dart';

void main() {
  late HorlogeFactice horloge;
  late List<String> motifs;
  late StreamController<void> ecran;
  late VerrouAutomatique verrou;

  setUp(() {
    horloge = HorlogeFactice();
    motifs = [];
    ecran = StreamController<void>();
  });

  /// Créé dans le corps du test : l'écoute de l'écran et la minuterie
  /// tournent alors dans le temps simulé de `testWidgets`.
  Future<void> ouvrir({Duration grace = const Duration(minutes: 5)}) async {
    verrou = VerrouAutomatique(
      horloge: horloge,
      ecranEteint: ecran.stream,
      verrouiller: motifs.add,
      grace: grace,
    );
    await verrou.demarrer();
  }

  tearDown(() async {
    await verrou.fermer();
    await ecran.close();
  });

  testWidgets('5 minutes sans activité verrouillent ; une activité repousse', (
    tester,
  ) async {
    await ouvrir();
    await tester.pump(const Duration(minutes: 4));
    await verrou.activite();
    await tester.pump(const Duration(minutes: 4));
    expect(motifs, isEmpty);

    await tester.pump(const Duration(minutes: 1, seconds: 1));
    expect(motifs, ['inactivite']);
  });

  testWidgets('écran éteint : grâce de 5 minutes, puis verrouillage par la '
      'minuterie (processus non gelé), une seule fois', (tester) async {
    await ouvrir();
    ecran
      ..add(null)
      ..add(null);
    await tester.pump();
    await tester.pump(const Duration(minutes: 4, seconds: 59));
    expect(motifs, isEmpty);

    await tester.pump(const Duration(seconds: 2));
    expect(motifs, ['ecran_eteint']);
  });

  testWidgets('écran éteint puis rallumé avant 5 minutes : pas de '
      'verrouillage, l\'inactivité repart du retour', (tester) async {
    await ouvrir();
    ecran.add(null);
    await tester.pump();
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(minutes: 4));
    await tester.pump(const Duration(minutes: 4));
    await verrou.cycleDeVie(AppLifecycleState.resumed);
    await tester.pump(const Duration(minutes: 4, seconds: 59));
    expect(motifs, isEmpty);

    await tester.pump(const Duration(seconds: 2));
    expect(motifs, ['inactivite']);
  });

  testWidgets('écran éteint, processus gelé : verrouillé au retour d\'après '
      'l\'horloge monotone', (tester) async {
    await ouvrir();
    ecran.add(null);
    await tester.pump();
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(minutes: 5)); // minuterie non déclenchée
    await verrou.cycleDeVie(AppLifecycleState.resumed);

    expect(motifs, ['ecran_eteint']);
  });

  testWidgets('5 minutes hors de l\'application : verrouillage par la '
      'minuterie', (tester) async {
    await ouvrir();
    await verrou.cycleDeVie(AppLifecycleState.inactive);
    await verrou.cycleDeVie(AppLifecycleState.hidden);
    await verrou.cycleDeVie(AppLifecycleState.paused);
    await verrou.activite(); // ignorée hors de l'application
    await tester.pump(const Duration(minutes: 4, seconds: 59));
    expect(motifs, isEmpty);

    await tester.pump(const Duration(seconds: 2));
    expect(motifs, ['hors_application']);
  });

  testWidgets('écran éteint pendant la sortie : motif précisé, échéance '
      'inchangée', (tester) async {
    await ouvrir();
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(minutes: 3));
    await tester.pump(const Duration(minutes: 3));
    ecran.add(null);
    await tester.pump();
    horloge.avancer(const Duration(minutes: 2));
    await verrou.cycleDeVie(AppLifecycleState.resumed);

    expect(motifs, ['ecran_eteint']);
  });

  testWidgets('retour avant 5 minutes : pas de verrouillage', (tester) async {
    await ouvrir();
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(minutes: 4, seconds: 59));
    await tester.pump(const Duration(minutes: 4, seconds: 59));
    await verrou.cycleDeVie(AppLifecycleState.resumed);
    await tester.pump(const Duration(seconds: 25));

    expect(motifs, isEmpty);
    verrou.arreter(); // minuterie d'inactivité encore armée
  });

  testWidgets('processus gelé hors de l\'application : verrouillé au retour '
      'd\'après l\'horloge monotone', (tester) async {
    await ouvrir();
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(minutes: 6)); // minuterie non déclenchée
    await verrou.cycleDeVie(AppLifecycleState.resumed);

    expect(motifs, ['hors_application']);
  });

  testWidgets('durées raccourcies par configuration (essais sur appareil)', (
    tester,
  ) async {
    await ouvrir(grace: const Duration(seconds: 20));
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(seconds: 10));
    await verrou.cycleDeVie(AppLifecycleState.resumed);
    expect(motifs, isEmpty);

    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(seconds: 21));
    await verrou.cycleDeVie(AppLifecycleState.resumed);
    expect(motifs, ['hors_application']);
  });

  testWidgets('sortie et retour rapprochés : pas de verrouillage '
      'ni de grâce restée armée', (tester) async {
    await ouvrir();
    final sortie = verrou.cycleDeVie(AppLifecycleState.paused);
    final retour = verrou.cycleDeVie(AppLifecycleState.resumed);
    await sortie;
    await retour;
    horloge.avancer(const Duration(minutes: 6));
    await tester.pump(const Duration(minutes: 4));

    expect(motifs, isEmpty);
    verrou.arreter();
  });

  testWidgets('inactivité constatée au retour', (tester) async {
    await ouvrir();
    horloge.avancer(const Duration(minutes: 6));
    await verrou.cycleDeVie(AppLifecycleState.resumed);

    expect(motifs, ['inactivite']);
  });

  testWidgets('détaché : verrouillé ; arrêté : plus rien', (tester) async {
    await ouvrir();
    await verrou.cycleDeVie(AppLifecycleState.detached);
    expect(motifs, ['detache']);

    await verrou.cycleDeVie(AppLifecycleState.paused);
    await verrou.activite();
    await tester.pump(const Duration(minutes: 10));
    expect(motifs, ['detache']);
  });
}

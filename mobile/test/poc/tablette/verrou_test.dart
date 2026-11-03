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
    verrou = VerrouAutomatique(
      horloge: horloge,
      ecranEteint: ecran.stream,
      verrouiller: motifs.add,
    );
  });

  tearDown(() async {
    await verrou.fermer();
    await ecran.close();
  });

  testWidgets('5 minutes sans activité verrouillent ; une activité repousse', (
    tester,
  ) async {
    await verrou.demarrer();
    await tester.pump(const Duration(minutes: 4));
    await verrou.activite();
    await tester.pump(const Duration(minutes: 4));
    expect(motifs, isEmpty);

    await tester.pump(const Duration(minutes: 1, seconds: 1));
    expect(motifs, ['inactivite']);
  });

  testWidgets('écran éteint : verrouillage immédiat, une seule fois', (
    tester,
  ) async {
    await verrou.demarrer();
    ecran
      ..add(null)
      ..add(null);
    await tester.pump();

    expect(motifs, ['ecran_eteint']);
  });

  testWidgets('30 s hors de l\'application : verrouillage par la minuterie', (
    tester,
  ) async {
    await verrou.demarrer();
    await verrou.cycleDeVie(AppLifecycleState.inactive);
    await verrou.cycleDeVie(AppLifecycleState.hidden);
    await verrou.cycleDeVie(AppLifecycleState.paused);
    await tester.pump(const Duration(seconds: 29));
    expect(motifs, isEmpty);

    await tester.pump(const Duration(seconds: 2));
    expect(motifs, ['hors_application']);
  });

  testWidgets('retour avant 30 s : pas de verrouillage', (tester) async {
    await verrou.demarrer();
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(seconds: 10));
    await tester.pump(const Duration(seconds: 10));
    await verrou.cycleDeVie(AppLifecycleState.resumed);
    await tester.pump(const Duration(seconds: 25));

    expect(motifs, isEmpty);
    verrou.arreter(); // minuterie d'inactivité encore armée
  });

  testWidgets('processus gelé hors de l\'application : verrouillé au retour '
      'd\'après l\'horloge monotone', (tester) async {
    await verrou.demarrer();
    await verrou.cycleDeVie(AppLifecycleState.paused);
    horloge.avancer(const Duration(minutes: 2)); // minuterie non déclenchée
    await verrou.cycleDeVie(AppLifecycleState.resumed);

    expect(motifs, ['hors_application']);
  });

  testWidgets('inactivité constatée au retour', (tester) async {
    await verrou.demarrer();
    horloge.avancer(const Duration(minutes: 6));
    await verrou.cycleDeVie(AppLifecycleState.resumed);

    expect(motifs, ['inactivite']);
  });

  testWidgets('détaché : verrouillé ; arrêté : plus rien', (tester) async {
    await verrou.demarrer();
    await verrou.cycleDeVie(AppLifecycleState.detached);
    expect(motifs, ['detache']);

    await verrou.cycleDeVie(AppLifecycleState.paused);
    await verrou.activite();
    await tester.pump(const Duration(minutes: 10));
    expect(motifs, ['detache']);
  });
}

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/main.dart' as app;

void main() {
  testWidgets('affiche l\'écran technique provisoire', (tester) async {
    await tester.pumpWidget(const app.GestionSiteTerrainApp());

    expect(find.textContaining('Écran technique provisoire'), findsOneWidget);
    expect(find.byType(MaterialApp), findsOneWidget);
  });

  testWidgets('main() démarre l\'application', (tester) async {
    app.main();
    await tester.pump();

    expect(find.byType(app.GestionSiteTerrainApp), findsOneWidget);
  });
}

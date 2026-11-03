/// POC-03 (S04-01, expérimental) : point d'entrée de l'écran technique.
/// `flutter run -t lib/poc/tablette/main_poc03.dart` (docs/poc/POC-03.md).
library;

import 'package:flutter/material.dart';

import 'arriere_plan.dart';
import 'harnais.dart';
import 'photo_essai.dart';
import 'plateforme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  const plateforme = PlateformeAndroid();
  final racine = await racineAppareil(plateforme);
  runApp(
    MaterialApp(
      home: HarnaisPoc03(
        dependances: DependancesHarnais(
          racine: racine,
          coffre: plateforme,
          horloge: plateforme,
          reducteur: plateforme,
          infos: plateforme.infos,
          photoDEssai: () => photoDEssai(largeur: 4000, hauteur: 3000),
          programmerEnvoi: programmerEnvoi,
          ecranEteint: plateforme.ecranEteint,
        ),
      ),
    ),
  );
}

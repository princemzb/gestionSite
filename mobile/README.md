# Application terrain gestionSite

Application Flutter pour tablettes Android et iPad (ADR-08). Elle n'affiche pour l'instant qu'un **écran technique provisoire** : aucun écran designé avant la validation des maquettes par le porteur du projet.

## Porte qualité
`mobile/eng/verify.sh` : format, analyse stricte, tests, couverture ≥ 80 % de **tout** `lib/` (un fichier sans test compte à 0 %). Identique à la CI, qui ajoute l'audit OSV de `pubspec.lock` et l'analyse Sonar (`princemzb_gestionSite_mobile`, code Dart et natif).

## Version finale Android
Signature par `android/key.properties` (jamais versionné) ; sans ce fichier, `flutter build apk --release` échoue volontairement.

## Points ouverts
- iPhone encore ciblé et iOS 15 minimum (valeurs du gabarit) : à trancher avec DEC-11 (usage téléphone) et DEC-06 (versions minimales).

## Commandes (vérifiées le 27 octobre 2026)
Version de Flutter : celle de `environment.flutter` dans `pubspec.yaml` (3.47.1), reprise par la CI.

- `flutter pub get --enforce-lockfile` (versions exactes de `pubspec.lock`)
- `dart format --output=none --set-exit-if-changed lib test`
- `flutter analyze --fatal-infos` : analyse stricte (`analysis_options.yaml`)
- `flutter test --coverage` (la CI exige ≥ 80 % de lignes couvertes ; l'audit OSV vérifie `pubspec.lock`)
- `flutter build apk --debug` puis `adb install -r build/app/outputs/flutter-apk/app-debug.apk` (tablette en débogage USB)

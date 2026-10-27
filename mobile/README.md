# Application terrain gestionSite

Application Flutter pour tablettes Android et iPad (ADR-08). Elle n'affiche pour l'instant qu'un **écran technique provisoire** : aucun écran designé avant la validation des maquettes par le porteur du projet.

## Commandes (vérifiées le 27 octobre 2026)
Version de Flutter : celle de `environment.flutter` dans `pubspec.yaml` (3.47.1), reprise par la CI.

- `flutter pub get --enforce-lockfile` (versions exactes de `pubspec.lock`)
- `dart format --output=none --set-exit-if-changed lib test`
- `flutter analyze --fatal-infos` : analyse stricte (`analysis_options.yaml`)
- `flutter test --coverage` (la CI exige ≥ 80 % de lignes couvertes ; l'audit OSV vérifie `pubspec.lock`)
- `flutter build apk --debug` puis `adb install -r build/app/outputs/flutter-apk/app-debug.apk` (tablette en débogage USB)

# Application terrain gestionSite

Application Flutter pour tablettes Android et iPad (ADR-08). Elle n'affiche pour l'instant qu'un **écran technique provisoire** : aucun écran designé avant la validation des maquettes par le porteur du projet.

## Commandes (vérifiées le 27 octobre 2026, Flutter 3.47.1)
- `flutter pub get`
- `dart format --output=none --set-exit-if-changed lib test`
- `flutter analyze --fatal-infos` : analyse stricte (`analysis_options.yaml`)
- `flutter test --coverage`
- `flutter build apk --debug` puis `adb install -r build/app/outputs/flutter-apk/app-debug.apk` (tablette en débogage USB)

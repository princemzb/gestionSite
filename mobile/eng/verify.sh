#!/usr/bin/env bash
# Porte qualité locale et CI de l'application Flutter (ADR-09) : mêmes contrôles partout.
# Usage : mobile/eng/verify.sh   (depuis n'importe quel répertoire)
set -euo pipefail

cd "$(dirname "$0")/.."
readonly MIN_LINE_COVERAGE=80
readonly ALL_LIB_TEST=test/all_lib_coverage_test.dart

flutter pub get --enforce-lockfile
dart format --output=none --set-exit-if-changed lib test
flutter analyze --fatal-infos

# Importe chaque fichier de lib/ pour qu'un fichier sans test compte à 0 % au lieu d'être ignoré.
trap 'rm -f "$ALL_LIB_TEST"' EXIT
{
  echo "// Généré par eng/verify.sh, non versionné."
  echo "// ignore_for_file: unused_import, directives_ordering"
  find lib -name '*.dart' ! -name '*.g.dart' | sort | sed "s#^lib/#import 'package:gestionsite_terrain/#; s#\$#';#"
  echo "void main() {}"
} > "$ALL_LIB_TEST"
flutter test --coverage

python3 - coverage/lcov.info "$MIN_LINE_COVERAGE" <<'PY'
import sys
found = hit = 0
for line in open(sys.argv[1]):
    if line.startswith("LF:"):
        found += int(line[3:])
    elif line.startswith("LH:"):
        hit += int(line[3:])
if found == 0:
    sys.exit("Aucune ligne mesurée : rapport de couverture vide.")
coverage = 100 * hit / found
print(f"Couverture des lignes Dart : {coverage:.1f} % (minimum {sys.argv[2]} %)")
sys.exit(0 if coverage >= float(sys.argv[2]) else 1)
PY

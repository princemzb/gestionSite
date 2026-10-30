#!/usr/bin/env bash
# Porte qualité du JavaScript servi par le web (ADR-11) : tests node:test des fonctions pures
# de l'éditeur de plans de bâtiment, couverture ≥ 80 % (lignes, branches, fonctions), rapport lcov pour Sonar.
# Usage : server/eng/verify-js.sh   (depuis n'importe quel répertoire ; Node.js 22 requis)
set -euo pipefail

cd "$(dirname "$0")/.."
readonly MIN_COVERAGE=80
readonly LCOV=artifacts/coverage-js/lcov.info

mkdir -p "$(dirname "$LCOV")"
node --test --disable-warning=ExperimentalWarning \
  --experimental-test-coverage \
  --test-coverage-include='src/GestionSite.Web/wwwroot/plans/noyau/**' \
  --test-coverage-lines="$MIN_COVERAGE" \
  --test-coverage-branches="$MIN_COVERAGE" \
  --test-coverage-functions="$MIN_COVERAGE" \
  --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=lcov --test-reporter-destination="$LCOV" \
  'tests/js/**/*.test.mjs'
# Chemins du rapport relatifs à la racine du dépôt, base de l'analyse Sonar.
sed -e 's#^SF:src/#SF:server/src/#' "$LCOV" > "$LCOV.tmp" && mv "$LCOV.tmp" "$LCOV"
echo "Rapport lcov : server/$LCOV"

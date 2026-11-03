#!/usr/bin/env bash
# Porte qualité du manuel d'utilisation (DOC-01) : tests node:test de l'assemblage (assemblage.mjs), couverture ≥ 80 %
# (lignes, branches, fonctions), rapport lcov pour Sonar, puis contrôle du manuel (ancres, images, liens) sans navigateur.
# Prérequis : Node.js 22 et « npm ci » dans scripts/manuel (PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 suffit ici).
set -euo pipefail

cd "$(dirname "$0")"
readonly MIN_COVERAGE=80
readonly LCOV=sortie/lcov.info

mkdir -p sortie
node --test --disable-warning=ExperimentalWarning \
  --experimental-test-coverage \
  --test-coverage-include="assemblage.mjs" \
  --test-coverage-lines="$MIN_COVERAGE" \
  --test-coverage-branches="$MIN_COVERAGE" \
  --test-coverage-functions="$MIN_COVERAGE" \
  --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=lcov --test-reporter-destination="$LCOV" \
  assemblage.test.mjs

# Chemins absolus : Sonar rattache ainsi le rapport au fichier, quel que soit le répertoire de l'analyse.
sed -e "s#^SF:assemblage.mjs#SF:$(pwd)/assemblage.mjs#" "$LCOV" > "$LCOV.tmp" && mv "$LCOV.tmp" "$LCOV"
grep -q "^SF:$(pwd)/assemblage.mjs$" "$LCOV"
echo "Rapport lcov : scripts/manuel/$LCOV"

node livret.mjs --verifier

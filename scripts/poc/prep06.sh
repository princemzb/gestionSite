#!/usr/bin/env bash
# POC PREP-06 (expérimental) — rapport exécutable : commandes hors connexion rejouables.
# Prérequis : Docker démarré (server/compose.yaml), .NET 10 et Flutter. Base locale uniquement.
# Usage : scripts/poc/prep06.sh
set -euo pipefail
cd "$(dirname "$0")/../.."
API=http://localhost:5280
RUN=$(date +%s)
LOG=$(mktemp)

# Le conteneur de création du compartiment se termine après usage : « --wait » le signale en erreur.
docker compose -f server/compose.yaml up -d --wait >/dev/null 2>&1 || true
echo "1/4 Démarrage de l'API (migrations appliquées au démarrage, POC actif en Development)…"
(cd server/src/GestionSite.Api && dotnet run >"$LOG" 2>&1) &
API_PID=$!
trap 'kill $API_PID 2>/dev/null; pkill -f GestionSite.Api 2>/dev/null || true; rm -f "$LOG"' EXIT
for _ in $(seq 1 90); do curl -sf "$API/health/ready" >/dev/null 2>&1 && break; sleep 1; done
curl -sf "$API/health/ready" >/dev/null || { echo "API non prête"; tail -20 "$LOG"; exit 1; }

echo "2/4 Ouverture d'une organisation de démonstration (commande d'exploitation)…"
SORTIE=$(cd server/src/GestionSite.Api && dotnet run --no-build -- ouvrir-organisation --nom "POC PREP-06 $RUN" --admin-email "admin.$RUN@poc.test" --admin-nom "Admin POC")
JETON_ACTIVATION=$(echo "$SORTIE" | grep -o '[A-Za-z0-9_-]\{40,\}$')

echo "3/4 Site et deux techniciens (deux profils sur la même tablette)…"
ENV_SCENARIO=$(python3 scripts/poc/prep06_preparer.py "$API" "$JETON_ACTIVATION" "admin.$RUN@poc.test")

echo "4/4 Scénario tablette (Dart) contre l'API réelle :"
cd mobile
env PREP06_API=$API $ENV_SCENARIO flutter test test/poc/synchronisation_e2e_test.dart --reporter expanded 2>&1 \
  | grep -E "RAPPORT|All tests passed|Some tests failed|Expected|Actual" | sed 's/^/  /'

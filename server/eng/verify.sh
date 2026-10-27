#!/usr/bin/env bash
# Porte qualité locale et CI du serveur (ADR-09) : mêmes contrôles partout.
# Usage : server/eng/verify.sh   (depuis n'importe quel répertoire)
set -euo pipefail

cd "$(dirname "$0")/.."
readonly MIN_LINE_COVERAGE=80
readonly RESULTS=artifacts/test-results
readonly COVERAGE=artifacts/coverage

rm -rf artifacts
dotnet tool restore
dotnet restore GestionSite.slnx
dotnet build GestionSite.slnx --no-restore --configuration Release
dotnet format GestionSite.slnx --verify-no-changes --no-restore
dotnet test GestionSite.slnx --no-build --configuration Release \
  --collect:"XPlat Code Coverage" --results-directory "$RESULTS" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura,opencover

dotnet reportgenerator -reports:"$RESULTS/**/coverage.cobertura.xml" -targetdir:"$COVERAGE" \
  -reporttypes:"JsonSummary;Cobertura;TextSummary"
cat "$COVERAGE/Summary.txt"

python3 - "$COVERAGE/Summary.json" "$MIN_LINE_COVERAGE" <<'PY'
import json, sys
summary = json.load(open(sys.argv[1]))["summary"]
coverage, minimum = summary["linecoverage"], float(sys.argv[2])
print(f"Couverture des lignes : {coverage} % (minimum {minimum} %)")
sys.exit(0 if coverage >= minimum else 1)
PY

vulnerable=$(dotnet list GestionSite.slnx package --vulnerable --include-transitive 2>&1)
if grep -qiE "has the following vulnerable|comporte les packages vulnérables suivants" <<<"$vulnerable"; then
  echo "$vulnerable"
  echo "Dépendances vulnérables détectées." >&2
  exit 1
fi
echo "Aucune dépendance vulnérable."

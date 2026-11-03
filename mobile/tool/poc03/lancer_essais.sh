#!/usr/bin/env bash
# POC-03 (S04-01, expérimental) : essais Android réels (émulateur ou tablette).
# Démarre le serveur factice sur le Mac, lance integration_test/poc03_test.dart
# sur l'appareil, puis affiche les lignes « RAPPORT | … ».
#
# Usage : mobile/tool/poc03/lancer_essais.sh <numéro-adb> [adresse-du-serveur-vue-de-l-appareil]
#   émulateur : mobile/tool/poc03/lancer_essais.sh emulator-5554
#               (serveur vu en http://10.0.2.2:8765, heure modifiable par adb root)
#   tablette  : mobile/tool/poc03/lancer_essais.sh <numéro> http://<IP-du-Mac>:8765
#               (même réseau Wi-Fi ; l'essai d'heure passe en manuel, sans root)
set -euo pipefail

cd "$(dirname "$0")/../.."
readonly APPAREIL="${1:?numéro adb attendu (adb devices)}"
readonly SERVEUR="${2:-http://10.0.2.2:8765}"
readonly PORT=8765
readonly JOURNAL="build/poc03"
export ANDROID_SERIAL="$APPAREIL"
export ADB="${ADB:-$HOME/Library/Android/sdk/platform-tools/adb}"
# macOS sans licence Xcode acceptée : outils en ligne de commande pour les bibliothèques natives.
if [[ "$(uname)" == "Darwin" && -d /Library/Developer/CommandLineTools ]] && ! xcodebuild -license check >/dev/null 2>&1; then
  export DEVELOPER_DIR=/Library/Developer/CommandLineTools
fi

HEURE=false
if [[ "$SERVEUR" == "http://10.0.2.2:$PORT" ]]; then
  "$ADB" root >/dev/null && sleep 3 && "$ADB" wait-for-device
  HEURE=true
  # Le Keystore refuse une clé « tablette déverrouillée exigée » sans verrouillage
  # d'écran : l'émulateur reçoit un code d'essai (sans effet s'il en a déjà un).
  export POC03_CODE_ECRAN="${POC03_CODE_ECRAN:-1234}"
  "$ADB" shell locksettings set-pin "$POC03_CODE_ECRAN" >/dev/null 2>&1 || true
fi

mkdir -p "$JOURNAL"
dart run tool/poc03/serveur_hote.dart "$PORT" >"$JOURNAL/serveur.log" 2>&1 &
readonly SERVEUR_PID=$!
trap 'kill -INT "$SERVEUR_PID" 2>/dev/null || true' EXIT
for _ in $(seq 1 60); do
  grep -q "serveur factice prêt" "$JOURNAL/serveur.log" && break
  sleep 1
done

flutter test integration_test/poc03_test.dart -d "$APPAREIL" \
  --dart-define=POC03_SERVEUR="$SERVEUR" --dart-define=POC03_HEURE="$HEURE" \
  2>&1 | tee "$JOURNAL/essais.log"
grep "RAPPORT |" "$JOURNAL/essais.log" || true

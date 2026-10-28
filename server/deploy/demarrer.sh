#!/bin/sh
# Démarre l'API (interne) puis l'application web (publique) ; si l'une s'arrête, le conteneur s'arrête
# et la plateforme le relance. Configuration et secrets : variables d'environnement (jamais dans l'image).
set -eu

# Chaque processus part de son dossier : racine de contenu = wwwroot (styles) et appsettings.json.
(cd /app/api && ASPNETCORE_URLS=http://127.0.0.1:8081 exec dotnet GestionSite.Api.dll) &
API=$!

# L’application web appelle l’API en interne (elle réessaie tant que l’API démarre). Derrière un relais TLS,
# le schéma d’origine n’est repris que si Web__RelaisHttps=true (fourni par le relais, jamais par défaut).
(cd /app/web && ASPNETCORE_URLS=http://0.0.0.0:8080 Web__ApiUrl=http://localhost:8081 exec dotnet GestionSite.Web.dll) &
WEB=$!

# Le shell est le processus 1 : il transmet l'arrêt (mise en veille, redéploiement) aux deux processus,
# qui terminent leurs requêtes en cours.
arreter() {
  kill -TERM "$WEB" "$API" 2>/dev/null || true
  wait "$WEB" "$API" 2>/dev/null || true
  exit 0
}
trap arreter TERM INT

# Premier arrêté = conteneur arrêté (l'autre processus est terminé avec lui).
while kill -0 "$API" 2>/dev/null && kill -0 "$WEB" 2>/dev/null; do
  sleep 2 &
  wait $! || true
done
kill -TERM "$API" "$WEB" 2>/dev/null || true
exit 1

#!/bin/sh
# Démarre l'API (interne) puis l'application web (publique) ; si l'une s'arrête, le conteneur s'arrête
# et la plateforme le relance. Configuration et secrets : variables d'environnement (jamais dans l'image).
set -eu

ASPNETCORE_URLS=http://127.0.0.1:8081 dotnet /app/api/GestionSite.Api.dll &
API=$!

# L'application web appelle l'API en interne ; derrière le proxy TLS de la plateforme.
ASPNETCORE_URLS=http://0.0.0.0:8080 \
Web__ApiUrl=http://localhost:8081 \
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
  dotnet /app/web/GestionSite.Web.dll &
WEB=$!

# Premier arrêté = conteneur arrêté (l'autre processus est terminé avec lui).
while kill -0 "$API" 2>/dev/null && kill -0 "$WEB" 2>/dev/null; do
  sleep 2
done
kill "$API" "$WEB" 2>/dev/null || true
exit 1

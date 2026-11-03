#!/usr/bin/env bash
# Vérification de l'image du conteneur (SOC-05) : processus de rendu PDF isolé de l'API, rendu par le lanceur.
# Usage : server/eng/verify-image.sh [plateforme]   (linux/amd64 par défaut, celle de Cloudflare ; Docker requis)
#  1. Image livrée construite, puis image d'essai où la sonde (isolation-rendu-pdf/) remplace le programme de rendu.
#  2. Conteneur démarré comme en préproduction : API + web sous le compte « app », secrets fictifs en variables
#     d'environnement (aucun service joint).
#  3. Témoin : sous le compte de l'API, la sonde lit l'environnement et la configuration de l'API (exposition de S21-05).
#  4. Par le lanceur, la sonde a le compte du rendu : environnement, mémoire, descripteurs et signaux de l'API, de
#     l'application web et du processus 1, environnement, mémoire et descripteurs du lanceur, configuration de l'API
#     et du web, canal de diagnostic .NET, création d'un processus, désarmement de l'arrêt avec le lanceur, ouverture
#     d'une connexion réseau (seccomp, sauf image émulée) : tout est refusé.
#  5. L'arrêt du lanceur, seul geste permis à l'API, arrête l'enfant, que l'API ne peut pas tuer elle-même ; un processus
#     du compte du rendu encore en vie est tué avant le rendu suivant (ménage), sans toucher à l'API ni au web.
#     API et web créent leurs fichiers temporaires (formulaires reçus) en accès propriétaire seul.
#  6. Image livrée : rendu d'une page par le lanceur, page absente, fichier qui n'est pas un PDF, appels refusés.
set -euo pipefail
cd "$(dirname "$0")/../.."

readonly PLATEFORME=${1:-linux/amd64}
readonly IMAGE="gestionsite-verification:$(cut -d/ -f2 <<<"$PLATEFORME")"
readonly ESSAI="gestionsite-verification-sonde:$(cut -d/ -f2 <<<"$PLATEFORME")"
readonly LANCEUR=/usr/local/libexec/gestionsite/lanceur-rendu-pdf
readonly SECRET="secret-fictif-soc05-$$"
conteneurs=()

nettoyer() {
  if [[ ${#conteneurs[@]} -gt 0 ]]; then docker rm -f "${conteneurs[@]}" >/dev/null 2>&1 || true; fi
}
trap nettoyer EXIT

echec() {
  echo "ÉCHEC : $*" >&2
  exit 1
}

ok() { echo "OK : $*"; }

# Conteneur comme en préproduction (variables de deploy/cloudflare/src/index.ts), valeurs fictives.
demarrer() {
  local cle
  cle=$(head -c 48 /dev/urandom | base64)
  docker run -d --platform "$PLATEFORME" \
    -e ConnectionStrings__Main="Host=base.invalid;Database=gestionsite;Username=gestionsite;Password=$SECRET" \
    -e Authentification__CleSignature="$cle" \
    -e ObjectStorage__ServiceUrl=https://stockage.invalid -e ObjectStorage__AccessKey=acces \
    -e ObjectStorage__SecretKey="$SECRET" -e ObjectStorage__Bucket=fonds -e ObjectStorage__Region=auto \
    -e Web__UrlPublique=https://localhost:8080 \
    -e Web__Cles__ServiceUrl=https://stockage.invalid -e Web__Cles__AccessKey=acces \
    -e Web__Cles__SecretKey="$SECRET" -e Web__Cles__Compartiment=cles -e Web__Cles__Region=auto \
    -e Turnstile__CleSecrete="$SECRET" "${@:2}" "$1"
}

# PID d'un processus du conteneur d'après sa ligne de commande (motif « [x]… » : jamais la recherche elle-même).
pid() {
  docker exec "$1" sh -c "grep -l -s -a '$2' /proc/[0-9]*/cmdline | head -n 1 | cut -d/ -f3"
}

# Attend l'API et l'application web (60 s au plus), puis donne leurs PID.
attendre() {
  local api="" web=""
  for _ in $(seq 60); do
    api=$(pid "$1" '[G]estionSite.Api.dll')
    web=$(pid "$1" '[G]estionSite.Web.dll')
    if [[ -n "$api" ]] && [[ -n "$web" ]] && docker exec "$1" sh -c "ls /tmp/dotnet-diagnostic-$api-*-socket" >/dev/null 2>&1; then
      echo "$api $web"
      return
    fi
    sleep 1
  done
  docker logs "$1" >&2 || true
  echec "API ou application web non démarrée dans $1"
}

docker build --platform "$PLATEFORME" -f server/Dockerfile -t "$IMAGE" .
docker build --platform "$PLATEFORME" --build-arg IMAGE="$IMAGE" -t "$ESSAI" server/eng/isolation-rendu-pdf
UID_RENDU=$(docker run --rm --platform "$PLATEFORME" --entrypoint id "$IMAGE" -u rendupdf)
readonly UID_RENDU
# Image émulée (amd64 sur un poste arm64) : l'émulateur Rosetta n'accepte pas les filtres seccomp ; le lanceur le signale
# et continue (le ménage avant chaque rendu reste). Sur la plateforme native (CI, Cloudflare), seccomp est exigé.
case "$(docker info --format '{{.Architecture}}')/$(cut -d/ -f2 <<<"$PLATEFORME")" in
  x86_64/amd64 | aarch64/arm64 | arm64/arm64) EMULEE=false ;;
  *) EMULEE=true ;;
esac
readonly EMULEE

# --- Isolation : la sonde à la place du programme de rendu --------------------------------------------------------------
# Sans le profil seccomp de Docker : seul le filtre du lanceur peut refuser (io_uring, réseau, processus).
essai=$(demarrer "$ESSAI" --security-opt seccomp=unconfined)
conteneurs+=("$essai")
pids=$(attendre "$essai")
read -r API WEB <<<"$pids"
echo "API $API, application web $WEB"

temoin=$(docker exec -u app "$essai" dotnet /app/rendu-pdf/GestionSite.Modules.Patrimoine.RenduPdf.dll "$API" "$WEB" 1 1)
echo "$temoin"
for attendu in "ACCES environ-api" "ACCES config-api" "ACCES reseau"; do
  grep -qx "$attendu" <<<"$temoin" || echec "témoin : « $attendu » attendu sous le compte de l'API"
done
docker exec -u app "$essai" sh -c "tr '\0' '\n' < /proc/$API/environ" | grep -q "$SECRET" \
  || echec "témoin : secret fictif absent de l'environnement de l'API"
ok "témoin : sous le compte de l'API, l'environnement (secret fictif), la configuration de l'API et le réseau sont accessibles"

sonde=$(docker exec -u app "$essai" "$LANCEUR" "$API" "$WEB" 1 1 2>&1)
echo "$sonde"
attendus=24
if grep -qx "lanceur-rendu-pdf : seccomp indisponible, création de processus et réseau permis" <<<"$sonde"; then
  $EMULEE || echec "seccomp indisponible sur la plateforme native"
  echo "AVERTISSEMENT : image émulée, seccomp indisponible : création de processus, PR_SET_PDEATHSIG et réseau non vérifiés (vérifiés en natif)."
  sonde=$(grep -v -e "^ACCES processus$" -e "^ACCES pdeathsig$" -e "^ACCES reseau$" -e "^[A-Z]* io_uring" -e "^lanceur-rendu-pdf : seccomp" <<<"$sonde")
  attendus=20
else
  # Refus par seccomp (EPERM), et non par un autre module de sécurité (EACCES) ou faute de prise en charge.
  for refus in "REFUS reseau IOException errno=1" "REFUS io_uring IOException errno=1"; do
    grep -qx "$refus" <<<"$sonde" || echec "« $refus » attendu (filtre seccomp de l'enfant)"
  done
fi
grep -qx "ENVIRONNEMENT DOTNET_EnableDiagnostics DOTNET_gcServer" <<<"$sonde" || echec "environnement de l'enfant non fixé"
t=$'\t'
quatre="$UID_RENDU$t$UID_RENDU$t$UID_RENDU$t$UID_RENDU"
grep -qxF "COMPTE uid=$quatre gid=$quatre groupes=[] capacites=0000000000000000/0000000000000000 nnp=1" <<<"$sonde" \
  || echec "compte de l'enfant inattendu"
if grep -q "^ACCES" <<<"$sonde"; then echec "accès obtenu par l'enfant"; fi
[[ "$(grep -c "^REFUS" <<<"$sonde")" -eq "$attendus" ]] || echec "$attendus essais refusés attendus"
if grep -q "$SECRET" <<<"$sonde"; then echec "secret fictif lu par l'enfant"; fi
ok "enfant sous le compte rendupdf (aucun groupe, aucune capacité, aucun nouveau privilège) : $attendus accès refusés"

# --- Arrêt : l'API tue le lanceur, le noyau tue l'enfant ------------------------------------------------------------------
docker exec -d -u app "$essai" "$LANCEUR" "$API" "$WEB" 1 0
enfant=""
for _ in $(seq 60); do
  enfant=$(pid "$essai" '[r]endu-pdf/GestionSite')
  [[ -n "$enfant" ]] && break
  sleep 1
done
[[ -n "$enfant" ]] || echec "enfant non démarré"
lanceur=$(pid "$essai" '[l]anceur-rendu-pdf')
if refus=$(docker exec -u app "$essai" kill -KILL "$enfant" 2>&1); then echec "l'API a pu tuer l'enfant directement"; fi
grep -qi "not permitted" <<<"$refus" || echec "signal refusé pour une autre raison que les droits : $refus"
docker exec "$essai" test -d "/proc/$enfant" || echec "enfant $enfant absent avant l'arrêt du lanceur"
docker exec -u app "$essai" kill -KILL "$lanceur"
for _ in $(seq 10); do
  etat=$(docker exec "$essai" sh -c "grep -s '^State:' /proc/$enfant/status || true")
  case "$etat" in "" | *zombie*) break ;; esac
  sleep 1
done
case "$etat" in "" | *zombie*) ;; *) echec "enfant $enfant encore en vie après l'arrêt du lanceur ($etat)" ;; esac
ok "l'API ne peut pas signaler l'enfant ; l'arrêt du lanceur l'arrête"

# --- Fichiers temporaires de l'API et du web (formulaires reçus) : jamais lisibles par le compte du rendu -------------------
for processus in "$API" "$WEB"; do
  masque=$(docker exec "$essai" sh -c "grep '^Umask:' /proc/$processus/status | cut -f2")
  [[ "$masque" = 0077 ]] || echec "masque de création $masque pour le processus $processus (0077 attendu)"
done
ok "API et web créent leurs fichiers (temporaires compris) en accès propriétaire seul (umask 0077)"

# --- Ménage : un processus du compte du rendu laissé par un rendu précédent est tué avant le rendu suivant ---------------
docker exec -d -u rendupdf "$essai" sleep 6000
echappe=""
for _ in $(seq 30); do
  echappe=$(pid "$essai" '[s]leep.6000')
  [[ -n "$echappe" ]] && break
  sleep 1
done
[[ -n "$echappe" ]] || echec "processus échappé non démarré"
docker exec -u app "$essai" "$LANCEUR" "$API" "$WEB" 1 1 >/dev/null 2>&1 || echec "rendu après le processus échappé"
etat=$(docker exec "$essai" sh -c "grep -s '^State:' /proc/$echappe/status || true")
case "$etat" in "" | *zombie*) ;; *) echec "processus échappé $echappe encore en vie après le rendu suivant ($etat)" ;; esac
docker exec "$essai" test -d "/proc/$API" || echec "API arrêtée par le ménage"
docker exec "$essai" test -d "/proc/$WEB" || echec "application web arrêtée par le ménage"
ok "ménage : processus du compte du rendu laissé en vie tué avant le rendu suivant ; API et web intacts"

# --- Image livrée : rendu par le lanceur -----------------------------------------------------------------------------------
livree=$(demarrer "$IMAGE")
conteneurs+=("$livree")
attendre "$livree" >/dev/null
pdf=$'%PDF-1.4\n1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n2 0 obj <</Type/Pages/Kids[3 0 R 4 0 R]/Count 2>> endobj\n3 0 obj <</Type/Page/Parent 2 0 R/MediaBox[0 0 200 100]>> endobj\n4 0 obj <</Type/Page/Parent 2 0 R/MediaBox[0 0 100 300]>> endobj\ntrailer <</Root 1 0 R>>\n%%EOF\n'
rendu=$(mktemp)
printf '%s' "$pdf" | docker exec -i -u app "$livree" "$LANCEUR" 2 300 90000 1073741824 >"$rendu"
entete=$(head -n 1 "$rendu" | tr -d '\0')
png=$(tail -c +$(($(head -n 1 "$rendu" | wc -c) + 1)) "$rendu" | head -c 8 | od -An -tx1 | tr -d ' \n')
taille=$(($(wc -c <"$rendu") - $(head -n 1 "$rendu" | wc -c)))
rm -f "$rendu"
[[ "$entete" == "OK 2 100 300 100 300 $taille" ]] || echec "rendu de la page 2 : « $entete » ($taille octets d'image)"
[[ "$png" = "89504e470d0a1a0a" ]] || echec "rendu de la page 2 : signature PNG absente"
entete=$(printf '%s' "$pdf" | docker exec -i -u app "$livree" "$LANCEUR" 3 300 90000 1073741824)
[[ "$entete" = "PAGE 2" ]] || echec "page absente : « $entete »"
entete=$(printf 'GIF89a' | docker exec -i -u app "$livree" "$LANCEUR" 1 300 90000 1073741824)
[[ "$entete" = "ILLISIBLE" ]] || echec "fichier qui n'est pas un PDF : « $entete »"
ok "rendu par le lanceur : page 2 de 2 rendue (100 × 300), page 3 absente, GIF illisible"

for arguments in "1 2 3" "1 2 3 4 5" "1 2 3 x" "1 2 3 -4" "1 2 3 12345678901234567890"; do
  # shellcheck disable=SC2086 # arguments séparés volontairement
  if docker exec -u app "$livree" "$LANCEUR" $arguments </dev/null >/dev/null 2>&1; then echec "arguments acceptés : $arguments"; fi
done
if docker exec -u root "$livree" "$LANCEUR" 1 2 3 4 </dev/null >/dev/null 2>&1; then echec "lanceur accepté pour root"; fi
if docker exec -u rendupdf "$livree" "$LANCEUR" 1 2 3 4 </dev/null >/dev/null 2>&1; then echec "lanceur exécutable par rendupdf"; fi
ok "lanceur refusé hors des 4 arguments numériques, et pour tout autre compte que celui de l'API"

echo "Image $PLATEFORME : isolation du rendu PDF vérifiée."

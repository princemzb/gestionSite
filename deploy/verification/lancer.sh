#!/bin/zsh
# Vérification ou recette visible d'une US en préproduction, sur une organisation fictive ouverte pour l'occasion (OUT-02).
# Usage, depuis le poste du porteur (Trousseau gestionsite-preprod-*, .NET, Node) :
#   deploy/verification/lancer.sh us08             vérification rapide, Chromium sans interface (captures dans
#                                                  deploy/verification/recettes/verification-us08-<date>, ignoré par git)
#   RECETTE=1 deploy/verification/lancer.sh us08   recette visible : écran, vitesse humaine, vidéos, rapport HTML
# Variables facultatives : SITE (nom du site créé, « Site centre » par défaut), SANS_COMPILATION=1 (API déjà compilée),
# RECETTE_DOSSIER, RECETTE_LENTEUR, RECETTE_PAUSE (voir recette.mjs).
# Prérequis : « npm ci » dans deploy/verification (Playwright 1.55.0 et son Chromium : « npx playwright install chromium »).
# Aucun secret n'est affiché ni écrit : chaînes Neon (chaines.py) et jeton d'activation passent par l'environnement des
# processus ; la chaîne applicative est transmise au script (VERIF_BASE) pour les vérifications en base (us08).
set -u
V=${0:A:h}; R=${V:h:h}; n=${1:-}; t=$(date +%Y%m%d%H%M%S)
[[ $n == (us|doc)[0-9][0-9] && -f $V/$n.mjs ]] || { echo "usage : lancer.sh usXX|docXX (script usXX.mjs ou docXX.mjs de deploy/verification)"; exit 2; }
[[ -d $V/node_modules/playwright ]] || { echo "dépendances absentes : cd deploy/verification && npm ci"; exit 2; }
chaines=("${(@f)$(python3 -I $V/chaines.py migrator app)}") && (( ${#chaines} == 2 )) || { echo "Trousseau : chaînes Neon illisibles"; exit 1; }
migrator=${chaines[1]}; app=${chaines[2]}
url=$(security find-generic-password -s gestionsite-preprod-url -w) || { echo "Trousseau : adresse de préproduction illisible"; exit 1; }
compilation=(); [[ ${SANS_COMPILATION:-0} == 1 ]] && compilation=(--no-build)
sortie=$(cd $R && ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__Migrations=$migrator ConnectionStrings__Main=$app \
  Authentification__CleSignature=$(openssl rand -hex 32) dotnet run $compilation --no-launch-profile --project server/src/GestionSite.Api -- \
  ouvrir-organisation --nom "Vérification $n $t" --admin-email "admin-$n-$t@verif.gestionsite.test" --admin-nom "Admin $n" \
  --site "${SITE:-Site centre}" 2>/dev/null)
org=$(print -r -- "$sortie" | sed -n 's/^Organisation ouverte : //p')
jeton=$(print -r -- "$sortie" | sed -n 's/^Jeton d.activation.*) : //p')
[[ -z $org || -z $jeton ]] && { echo "ouverture impossible"; print -r -- "$sortie" | grep -v -i jeton | tail -5; exit 1; }
echo "organisation fictive : $org"
# En recette, recette.mjs range tout dans le rapport (RECETTE_DOSSIER rendu absolu) ; sinon, captures de la vérification
# rapide dans un dossier ignoré par git.
if [[ ${RECETTE:-} == 1 ]]; then
  [[ -n ${RECETTE_DOSSIER:-} ]] && export RECETTE_DOSSIER=${RECETTE_DOSSIER:a}
  dossier=$V
else
  dossier=$V/recettes/verification-$n-$t; mkdir -p $dossier
fi
cd $dossier && VERIF_URL=$url VERIF_ORG=$org VERIF_JETON=$jeton VERIF_BASE=$app node $V/$n.mjs

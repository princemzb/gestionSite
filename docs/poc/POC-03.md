# POC-03 — Tablette Android hors connexion (S04-01, expérimental)

Statut : **expérimental**, exécuté du 30 au 31 octobre 2026 par l'agent sur l'**émulateur Android** (Android 14, API 34, image `google_apis` arm64, profil « Pixel Tablet », 4 Go de RAM) et en tests unitaires sur macOS. **Aucun essai n'a encore eu lieu sur la tablette réelle (Lenovo Tab M11)** : son protocole est en fin de document, à exécuter quand elle sera branchée. La recette par le porteur est distincte.

Code jetable : `mobile/lib/poc/tablette/`, greffon natif `mobile/packages/coffre_appareil/` (Kotlin), tests `mobile/test/poc/tablette/`, essais Android `mobile/integration_test/poc03_test.dart`, serveur factice `mobile/test/poc/tablette/serveur_factice.dart` piloté depuis le Mac par `mobile/tool/poc03/`. Il complète PREP-06 (file de commandes idempotente) et sera remplacé par US-13/US-14.

Décisions appliquées (REGLES « Tablette et synchronisation — US-13, US-14 », 30/10/2026) : Android seul, Android 11 minimum (`minSdk = 30`), 24 h comptées depuis le dernier contact réussi, PIN à 6 chiffres par profil, révocation selon Q8.

## Résultat en bref
RESULTATS_EN_BREF

## Conception démontrée
### Clés et bases (T-01, T-02)
| Élément | Protection | Pourquoi |
| --- | --- | --- |
| Base de consultation, une par organisation du profil (`consultation_<org>.db`) | SQLCipher 4.19 (édition communautaire), clé aléatoire de 32 octets du profil | Données téléchargées, jetables |
| Clé de consultation | Scellée par AES-256-GCM avec une clé dérivée du PIN (Argon2id, m = 19 Mio, t = 2, p = 1, sel de 16 octets), puis chiffrée par une clé AES du Keystore **non extractible**, utilisable **tablette déverrouillée seulement** (`setUnlockedDeviceRequired`), en StrongBox si l'appareil en a une | Ni la copie des fichiers ni le Keystore seul ne suffisent : il faut l'appareil ET le PIN |
| File d'envoi, jetons, état du profil (`envoi.db`) | SQLCipher, clé aléatoire chiffrée par une autre clé Keystore du profil, **sans PIN** et utilisable écran verrouillé | Envoi en arrière-plan sans PIN ; un PIN oublié ou un blocage ne fait perdre aucune saisie |

- Un profil par compte, dans `no_backup/poc03/profils/<compte>/` (stockage privé exclu des sauvegardes, `allowBackup=false`).
- PIN faux : l'étiquette AES-GCM ne correspond pas. Le compteur d'échecs est enregistré **avant** l'essai (arrêter l'application pendant la vérification ne rend pas d'essai). 5 échecs : profil bloqué, rien n'est effacé.
- Reprise en ligne (PIN oublié, blocage, révocation) : connexion par mot de passe, clé Keystore de consultation détruite puis fichiers supprimés, nouvelle clé scellée par le nouveau PIN, instantané retéléchargé ; la file d'envoi n'est pas touchée.
- Organisation coupée par son administrateur : sa base est **supprimée au contact**, même profil verrouillé (une base par organisation) ; la session du compte reste ; ses saisies en attente sont marquées `acces_coupe`, gardées et jamais envoyées.
- Session de l'appareil révoquée par le titulaire : au contact, clé de consultation détruite puis fichiers, jetons supprimés, plus aucun essai sous la session révoquée ; les saisies restent et partent après reconnexion du **même** compte.
- Le Keystore **refuse** de créer une clé « tablette déverrouillée exigée » sans verrouillage d'écran (constaté sur l'émulateur, `ProviderException`) : la règle « aucun profil sans verrouillage d'écran » (T-02) est donc aussi imposée par Android ; le harnais la vérifie avant la préparation.

### Délai de 24 h (R13-04, R14-06)
- Départ : dernier contact réussi (renouvellement accepté **et** adhésions relues). Mesure du temps écoulé sur l'horloge monotone `elapsedRealtime` (sommeil compris), non modifiable par l'utilisateur.
- L'heure de la tablette ne peut que **raccourcir** le délai : temps consommé = max(monotone, différence d'heure murale, dernier pointage).
- Redémarrage hors réseau : le monotone repart de zéro ; on reprend le dernier pointage enregistré (à chaque évaluation et à chaque passage d'arrière-plan) + le temps depuis le démarrage ; l'heure est marquée « non garantie ». Limite : le temps tablette **éteinte** n'est compté que par l'heure murale (risque ci-dessous).
- Heure de saisie = heure du serveur au contact + temps monotone écoulé, avec l'indicateur « garantie ».

### Révocation et contact
- Contact = `POST /auth/refresh` accepté puis `GET /me?appareil=` (organisations accessibles **depuis cet appareil**). 401 `session_revoquee` : révocation locale ; 401 `jeton_invalide` ou expiration : connexion requise sans effacement ; 401 `jeton_expire` (accès) : passager.
- Fenêtre de grâce du renouvellement (T-03) simulée : réponse de rotation perdue puis rejeu de l'ancien jeton dans les 60 s → même couple ; au-delà → famille révoquée. Nécessaire aussi parce que premier plan et WorkManager peuvent renouveler en même temps.

### Photos (T-05)
- Réduction **native** (Kotlin, `BitmapFactory` + `Bitmap.compress`) : réorientée selon l'EXIF, 2560 px au plus, JPEG qualité 85, **sans aucune métadonnée** (comme `TraitementPhotos` côté serveur).
- Photo et commande enregistrées dans la **même transaction** de la file avant toute annonce de succès.
- Envoi photo par photo, par morceaux de 256 Kio : `GET /sync/files/{id}` donne les octets reçus, `PUT /sync/files/{id}` avec `Content-Range` et empreinte SHA-256 ; seule la suite repart. La commande n'est envoyée qu'après ses photos ; le serveur répond `fichiersManquants` si une photo manque (elle repart, jamais d'application à moitié).

### Arrière-plan (T-07)
- WorkManager (`workmanager` 0.10.10) : tâche unique « réseau connecté » à chaque saisie, et périodique toutes les 15 min. Le moteur Flutter d'arrière-plan n'a pas d'activité : le Keystore est donc fourni par un greffon local (`coffre_appareil`), enregistré dans tous les moteurs, et non par un canal dans `MainActivity`.
- Chaque profil part sous **sa** session, sans PIN ni affichage.

### Verrouillage automatique (R13-05)
5 min sans activité, extinction de l'écran (diffusion `ACTION_SCREEN_OFF`), 30 s hors de l'application ; vérifié aussi au retour d'après l'horloge monotone (processus gelé). Verrouiller ferme les bases et remet la clé à zéro en mémoire.

## Mesures sur l'émulateur
MESURES

## Preuves automatiques
- `mobile/eng/verify.sh` (CI « Application Flutter ») : TESTS_UNITAIRES. Ils utilisent la vraie bibliothèque SQLCipher (fichiers illisibles sans clé, clé d'un autre profil refusée), un Keystore logiciel de substitution et un serveur factice HTTP.
- `mobile/tool/poc03/lancer_essais.sh emulator-5554` : 8 scénarios sur Android avec le vrai Keystore, SQLCipher, WorkManager et de vraies coupures (mode avion par adb). Lignes « RAPPORT | » ci-dessus.

## Défauts trouvés par les essais sur Android (corrigés)
- Le canal de méthode rend des octets en lecture seule : la remise à zéro de la clé en mémoire échouait (`Cannot modify an unmodifiable list`). Copie modifiable avant usage ; test ajouté.
- Le Keystore refuse la clé de consultation sans verrouillage d'écran (voir plus haut) : condition désormais documentée et vérifiée.

## Routes serveur nécessaires à US-13/US-14
Absentes de l'API aujourd'hui (simulées par le serveur factice) :

| Route | Rôle |
| --- | --- |
| `POST /devices` | Enregistrer l'appareil (identifiant, clé publique créée dans le Keystore) |
| `POST /auth/login` (+ `appareilId`) | Session liée à l'appareil |
| `POST /auth/refresh` (+ `appareilId`, preuve de possession) | Rotation avec fenêtre de grâce ; codes distincts `session_revoquee`, `jeton_invalide`, `expire` |
| `GET /me?appareil=` | Adhésions relues, organisations accessibles depuis cet appareil, heure du serveur |
| `DELETE /me/devices/{appareilId}` | Le titulaire révoque sa session sur l'appareil (toutes organisations) |
| `DELETE /organizations/{orgId}/devices/{appareilId}` | L'administrateur coupe son organisation sur l'appareil (jamais la session) |
| `GET /organizations/{orgId}/devices` | Écran web « Appareils » |
| `GET /organizations/{orgId}/sync/snapshot` (+ curseur) | Instantané de consultation |
| `POST /organizations/{orgId}/sync/commands` | Une commande par appel, idempotente (`applied`, `alreadyApplied`, `conflict`, `rejected`, `fichiersManquants`) ; 403 `acces_coupe` |
| `GET`/`PUT /sync/files/{fileId}` | Octets reçus ; envoi par morceaux `Content-Range` + SHA-256, renvoyable sans effet |

## Dépendances ajoutées (versions exactes, `pubspec.lock`)
| Paquet | Version | Licence | Usage |
| --- | --- | --- | --- |
| `sqlite3` (Simon Binder) | 3.8.0 | MIT | Accès SQLite ; option `source: sqlcipher` : binaires SQLCipher téléchargés depuis les versions GitHub du paquet, vérifiés par empreinte SHA-256 |
| SQLCipher édition communautaire (Zetetic, via `sqlite3`) | 4.19.0 | BSD 3 clauses ; lie OpenSSL (Apache 2.0) sur Android | Chiffrement des bases |
| `pointycastle` (Bouncy Castle) | 4.0.0 | MIT | Argon2id, AES-GCM, SHA-256 en Dart |
| `workmanager` / `workmanager_android` (Flutter Community) | 0.10.10 / 0.10.9 | MIT | Envoi en arrière-plan |
| `integration_test` | SDK Flutter | BSD 3 clauses | Essais sur appareil (développement) |

Écartés : `sqlcipher_flutter_libs` (fin de vie, remplacé par l'option de `sqlite3`) ; `flutter_secure_storage` (n'indique pas le niveau StrongBox/TEE et ne permet pas d'exiger la tablette déverrouillée) : greffon local de ~250 lignes de Kotlin, sans dépendance. Aucun service payant, aucune télémétrie. Points d'attention : `workmanager` ajoute les permissions `POST_NOTIFICATIONS` et `FOREGROUND_SERVICE_SHORT_SERVICE`, écrit un fichier témoin dans le cache de l'application à chaque tâche, et applique encore l'ancien greffon Kotlin Gradle (avertissement de Flutter) ; drift (prévu par T-01) reste possible au-dessus du même `sqlite3`.

## Risques et limites
1. **Émulateur ≠ tablette.** Le Keystore de l'émulateur est **logiciel** (niveau `logiciel` mesuré) : la protection matérielle (TEE, StrongBox) et ses temps ne sont prouvés que sur la M11. Mesures faites en mode débogage (JIT), plus lentes qu'une version installée (AOT) ; voir le protocole pour la mesure « profile ».
2. **Coût d'Argon2id en Dart pur** (pointycastle) : le déverrouillage peut dépasser la cible (environ 300 ms, cadrage T-01) sur la M11. Les paramètres sont enregistrés avec chaque clé scellée : on peut les ajuster sans casser les profils. Si la M11 dépasse ~1 s en version « profile », passer à une implémentation native (libsodium, ou Argon2 côté Kotlin) ou réduire la mémoire (m = 8 Mio).
3. **Limite de 5 PIN faux appliquée par l'application** (compteur dans la file chiffrée par le Keystore). Sur une tablette rootée, un attaquant qui contrôle l'application peut remettre le compteur à zéro et essayer les 10⁶ PIN, à raison d'un appel Keystore + Argon2id par essai (environ 3 à 30 jours selon le temps mesuré). Sans root, aucune attaque hors de l'appareil n'est possible (clé non extractible). Risque à consigner dans SECURITE, avec la limite de 24 h et la révocation comme parades.
4. **File d'envoi lisible sans PIN** par l'application (T-02, accepté au cadrage) : les saisies en attente d'un collègue ne sont pas affichées, mais l'application y a accès techniquement.
5. **Temps tablette éteinte** : après un redémarrage hors réseau, il n'est compté que par l'heure de la tablette ; éteindre la tablette ET reculer l'heure prolonge la consultation de la durée d'extinction. Parade possible en US-13 : refuser la consultation après un redémarrage tant qu'aucun contact n'a eu lieu (plus strict), à trancher par le porteur si nécessaire.
6. **Clé en mémoire Dart** : remise à zéro après usage, mais le ramasse-miettes peut en garder des copies ; SQLCipher efface sa propre mémoire (`cipher_memory_security`).
7. **Réseau de l'émulateur** : après un mode avion, il revient en 30 à 40 s (réseau cellulaire simulé) ; la M11 Wi-Fi doit être mesurée.
8. **Arrière-plan Lenovo** : la gestion de batterie du fabricant peut différer ou bloquer WorkManager ; seule la tablette peut le dire (protocole, étape 7).
9. **Saisies d'une organisation coupée** : gardées sur la tablette (`acces_coupe`) et jamais envoyées ; leur passage en quarantaine (Q7) est à décider en US-14.

## Essais sur l'appareil réel à faire (Lenovo Tab M11)
**Non exécutés à ce jour.** À faire par l'agent (ou le développeur) quand la tablette est branchée en USB, débogage USB activé ; la recette du porteur reste distincte. Aucune donnée réelle : comptes de démonstration du serveur factice. Prérequis : tablette avec un verrouillage d'écran (PIN, schéma ou mot de passe), Mac et tablette sur le même Wi-Fi, Flutter 3.47.1.

1. **Identifier l'appareil** : `adb devices` (noter le numéro), `adb shell getprop ro.build.version.sdk` (attendu 35), `adb shell pm list features | grep -i strongbox` (présence ou non de `android.hardware.strongbox_keystore`).
2. **Essais automatiques** : depuis `mobile/`, `tool/poc03/lancer_essais.sh <numéro> http://<IP-du-Mac>:8765` (IP : `ipconfig getifaddr en0` ; autoriser les connexions entrantes de `dart` si macOS le demande). Attendu : 8 scénarios réussis, lignes `RAPPORT |` dans `build/poc03/essais.log`, et :
   - niveau de clé `strongbox` ou `tee` (jamais `logiciel`) ; noter les temps ;
   - fichiers `consultation_*.db` et `envoi.db` : en-tête SQLite `false`, marqueur en clair `false`, lisibles sans clé / avec une autre clé `false` ;
   - photo : réduite en 2560×1920, EXIF restant `false`, reprise après mode avion avec commande appliquée **1 fois** et octets renvoyés < taille ;
   - WorkManager : commande reçue avec l'origine `arriere-plan`.
   Sans `adb root`, l'essai d'heure est sauté (`POC03_HEURE=false`) : voir l'étape 5. Au réveil de l'écran, **déverrouiller la tablette à la main** quand l'essai le demande (le code de la tablette n'est jamais donné à l'outil).
3. **Mesures en version installée** (AOT, représentatives) : `flutter build apk --profile -t lib/poc/tablette/main_poc03.dart --dart-define=POC03_MESURES=true`, puis `adb install -r build/app/outputs/flutter-apk/app-profile.apk`, ouvrir l'application, et `adb logcat -d -s flutter | grep RAPPORT`. Attendu : niveau de clé, Argon2id (cible ≤ 1 s, idéal ~300 ms), déverrouillage complet, PIN faux. Si Argon2id > 1 s : voir le risque 2.
4. **Lecture du fichier sans clé, à la main** : `adb shell run-as fr.gestionsite.terrain ls no_backup/poc03/profils/` puis `adb shell run-as fr.gestionsite.terrain head -c 64 no_backup/poc03/profils/<compte>/envoi.db | xxd` (version de débogage seulement) : attendu, octets aléatoires, pas de `SQLite format 3`.
5. **Heure modifiée (manuel)** : dans l'écran technique (`flutter run -t lib/poc/tablette/main_poc03.dart -d <numéro>`, serveur `tool/poc03/serveur_hote.dart` lancé à part, comptes créés par `curl -X POST http://localhost:8765/controle/compte -d '{"id":"<uuid>","email":"a@demo.invalid","motDePasse":"demo-demo-demo","organisations":["org-x"]}'`) : préparer un profil, couper le Wi-Fi, reculer la date de 2 jours dans Paramètres (heure automatique désactivée), « État » : le temps consommé ne diminue pas ; avancer de 2 jours : « Déverrouiller » répond `expire` ; remettre l'heure automatique.
6. **Redémarrage hors réseau** : profil prêt, mode avion, saisie, redémarrer la tablette, rouvrir : la saisie est là, « État » indique `heure garantie : false` et un temps consommé qui reprend le dernier pointage.
7. **Arrière-plan et batterie Lenovo (1 h)** : écran technique, profil prêt, mode avion, « Saisie avec photo » ×3, « Programmer l'envoi en arrière-plan », quitter l'application (sans la forcer à s'arrêter), éteindre l'écran, désactiver le mode avion, laisser 1 h sur batterie. Attendu : les 3 commandes dans `curl http://localhost:8765/controle/etat` avec `"origine":"arriere-plan"` ; noter l'heure de réception (`recuLe`). Refaire avec l'optimisation de batterie Lenovo active et inactive ; `adb shell dumpsys jobscheduler | grep -A5 fr.gestionsite` montre les tâches.
8. **Arrêt forcé** : saisie hors réseau, `adb shell am force-stop fr.gestionsite.terrain`, rouvrir : file intacte (« État »).
9. **Verrouillage automatique** : profil ouvert ; écran éteint par le bouton → verrouillé au retour ; application quittée 31 s → verrouillé ; 5 min sans toucher → verrouillé (journal de l'écran technique).
10. **Coupure Wi-Fi réelle pendant 3 photos** : « Saisie avec photo » ×3, « Synchroniser » et couper le Wi-Fi pendant l'envoi, le rétablir, « Synchroniser » : `controle/etat` montre chaque fichier complet une fois et chaque commande appliquée une fois.

Consigner les résultats dans ce document (section « Mesures sur la tablette », à créer), avec la date et la version d'Android.

## Recommandation pour ADR-03
RECOMMANDATION

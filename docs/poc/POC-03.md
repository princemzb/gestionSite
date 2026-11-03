# POC-03 — Tablette Android hors connexion (S04-01, expérimental)

Statut : **expérimental**. Écrit et essayé du 30 au 31 octobre 2026 sur l'**émulateur Android** (Android 14, API 34, image `google_apis` arm64, profil « Pixel Tablet », 4 Go de RAM) et en tests unitaires sur macOS ; essais automatiques terminés le **3 novembre 2026 sur la tablette réelle** : Lenovo Tab M11 TB330FU, **Android 15 (API 35)**, version `TB330FU_ROW_OPEN_USER_V5_V_ZUI_17.0.487_ST_260705`, reliée en USB au Mac, Wi-Fi. Les essais manuels restants (fin de document) sont à faire **avec le porteur** ; sa recette est distincte.

Code jetable : `mobile/lib/poc/tablette/`, greffon natif `mobile/packages/coffre_appareil/` (Kotlin), tests `mobile/test/poc/tablette/`, essais Android `mobile/integration_test/poc03_test.dart`, serveur factice `mobile/test/poc/tablette/serveur_factice.dart` piloté depuis le Mac par `mobile/tool/poc03/`. Il complète PREP-06 (file de commandes idempotente) et sera remplacé par US-13/US-14.

Décisions appliquées (REGLES « Tablette et synchronisation — US-13, US-14 », 30/10/2026) : Android seul, Android 11 minimum (`minSdk = 30`), 24 h comptées depuis le dernier contact réussi, PIN à 6 chiffres par profil, révocation selon Q8. Décision du porteur du 03/11/2026 (R13-05 révisée) : grâce de 5 minutes après l'écran éteint ou la sortie de l'application, contrôlée au retour, contenu masqué hors de l'application.

## Résultat en bref
**8 scénarios automatiques sur 8 réussis sur la Tab M11** (3/11/2026, Android 15), avec le vrai Keystore, SQLCipher, WorkManager et de vraies coupures réseau (mode avion par adb). La faisabilité d'ADR-03 (client Flutter installé, stockage local protégé, profils séparés) est démontrée sur l'appareil de référence ; restent des essais manuels avec le porteur (fin de document).

- **Clés** : Keystore matériel de niveau **TEE** (pas de StrongBox sur la M11) ; chiffrer + déchiffrer 17 à 37 ms.
- **Déverrouillage par PIN** (Keystore + Argon2id m = 19 Mio + ouverture SQLCipher) : médiane **736 ms** en mode débogage ; PIN faux détecté en 759 ms. Au-dessus de la cible d'environ 300 ms, sous le seuil de 1 s ; la mesure en version « profile » reste à faire.
- **Bases illisibles** sans la clé ou avec la clé d'un autre profil ; aucune donnée en clair dans les fichiers ; enveloppe d'un profil refusée par la clé Keystore d'un autre.
- **5 PIN faux** : profil bloqué, même après redémarrage de l'application, file d'envoi intacte ; reprise en ligne avec un nouveau PIN.
- **24 h** (raccourcies à 8 s) : expiration constatée, saisie envoyée au contact suivant, consultation rouverte.
- **Révocations** : organisation coupée → sa base supprimée au contact, profil verrouillé, session gardée ; session de l'appareil révoquée → fichiers de consultation et clé détruits, saisies gardées.
- **Photo** 4000×3000 réduite nativement en 2560×1920 en 1,2 s, sans EXIF ; envoi coupé par le mode avion puis repris là où il s'était arrêté, commande appliquée **une seule fois**.
- **Verrouillage** (R13-05 révisée) : contenu masqué (`FLAG_SECURE`) ; aucun verrouillage pendant la grâce, verrouillage au-delà (après l'écran éteint comme après la sortie de l'application) ; extinction de l'écran reçue dans Dart en 18 à 36 ms.
- **WorkManager** : commande envoyée **en arrière-plan**, application quittée et écran éteint, environ 1 min après le retour du réseau.

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

### Verrouillage automatique (R13-05, révisée le 03/11/2026)
- 5 min sans activité dans l'application ; **grâce de 5 min** après l'extinction de l'écran (diffusion `ACTION_SCREEN_OFF`) ou la sortie de l'application (`hidden`/`paused`). La grâce part de la **première** sortie : Android met l'application en pause environ 0,6 s avant de diffuser l'extinction (mesuré sur la M11) ; un écran éteint pendant la sortie ne repousse pas l'échéance. L'inactivité est suspendue hors de l'application ; revenir avant la fin de la grâce compte comme une activité.
- Contrôle **au retour** d'après l'horloge monotone (`elapsedRealtime`), fiable même si Android gèle le processus ou si l'heure change ; une minuterie verrouille aussi au plus tôt quand le processus n'est pas gelé.
- **Contenu masqué** : `FLAG_SECURE` posé par `MainActivity` avant le premier affichage (aperçu vide dans les applications récentes, captures et enregistrements d'écran refusés). Le poser seulement à la sortie serait trop tard : l'aperçu est pris au passage en arrière-plan.
- Bouton « Verrouiller / changer d'utilisateur » dans l'écran technique. Verrouiller ferme les bases et remet la clé à zéro en mémoire.
- **Photos prises dans l'application** (décision du 03/11) : hors du périmètre du POC, qui génère une photo d'essai ; exigence reportée sur **US-13** (appareil photo intégré, par exemple le paquet `camera`, sans passer par l'application photo du système ni par la galerie).

## Mesures sur la tablette (Lenovo Tab M11, 3 novembre 2026)
Appareil : Lenovo TB330FU, Android 15 (API 35), verrouillage d'écran configuré, StrongBox non déclarée. Essais : `mobile/tool/poc03/lancer_essais.sh HVA7N02J http://192.168.1.40:8765` (Flutter 3.47.1, **version de débogage**, JIT : temps majorés par rapport à une version installée). Passage complet du 3/11 vers 8 h 20 : **8 scénarios sur 8 réussis** (`All tests passed`, 8 min).

| Mesure | Tablette (3/11, Android 15) | Émulateur (31/10, Android 14) |
| --- | --- | --- |
| Niveau des clés Keystore | **tee** (deux clés) | logiciel |
| Création d'une clé (tablette déverrouillée exigée / non) | 203 ms / 11 ms (143 / 19 ms au passage précédent) | 5 873 / 1 036 ms |
| Chiffrer + déchiffrer 200 o (médiane) | 25 ms / 17 ms | 91 / 103 ms |
| Argon2id m = 19 Mio, t = 2, p = 1 (médiane de 5) | **722 ms** (658 à 854) ; 719 ms au passage précédent | 3 455 ms |
| Préparation d'un profil (connexion, clés, bases) | 1 336 ms | 7 189 ms |
| Déverrouillage par PIN complet (médiane de 5) | **736 ms** (694 à 790) ; 741 ms au passage précédent | 4 851 ms |
| PIN faux détecté | 759 ms | 8 126 ms |
| Fichiers `consultation_org-x.db` (12 Kio) et `envoi.db` (32 Kio) | en-tête SQLite absent, marqueur en clair absent, illisibles sans clé et avec une autre clé | idem |
| Délai de 24 h raccourci à 8 s | expiré après 9 s réelles (9 781 ms consommées), heure garantie | idem (15 777 ms) |
| Photo d'essai 4000×3000 (41 Mo) : génération / réduction native | 9,5 s / **1,2 s** → 2560×1920, 2,99 Mo | 35,7 s / 30,0 s |
| Retour du réseau après le mode avion (serveur joignable) | 18 s et 29 s (deux passages) | 30 à 40 s |
| Envoi photo coupé puis repris | 512 Kio reçus avant la coupure, 2,46 Mo renvoyés, fichier complet (12 morceaux), commande appliquée 1 fois | échec (émulateur trop lent) |
| Extinction de l'écran → réception dans Dart | 18 ms (30 et 36 ms aux passages précédents) | non mesuré |
| WorkManager, application quittée et écran éteint | commande reçue avec l'origine `arriere-plan` 83 s après la programmation (≈ 64 s après le retour du réseau) | échec |

Lignes `RAPPORT |` du passage complet (extrait) :
```
RAPPORT | Clé Keystore (tablette déverrouillée exigée : true) : niveau tee, création 203 ms, chiffrer + déchiffrer médiane 25 ms
RAPPORT | Argon2id m=19 Mio t=2 p=1 : médiane 722 ms ([854, 730, 658, 722, 706])
RAPPORT | Déverrouillage par PIN (Keystore + Argon2id + SQLCipher) : médiane 736 ms ([736, 770, 695, 790, 694])
RAPPORT | Organisation coupée sur l'appareil (profil verrouillé) : base org-coupee présente false, org-gardee présente true, saisie org-coupee rejetee/acces_coupe, session gardée true
RAPPORT | Session de l'appareil révoquée par le titulaire : session_revoquee, fichiers de consultation restants [], saisies gardées 1
RAPPORT | Photo de 2986445 o : reçus avant reprise 524288, reprise … appliquees=1 … octets=2462157, fichier complet true (2986445 o, 12 morceaux), commande appliquée 1 fois
RAPPORT | Contenu masqué (FLAG_SECURE : aperçu des applications récentes vide, captures refusées) : true
RAPPORT | Sortie de 4 s (grâce 20 s) : verrouillage «  »
RAPPORT | Hors de l'application 30 s : verrouillage « hors_application » (minuterie, 21208 ms après la sortie)
RAPPORT | Écran éteint 30 s : diffusion reçue dans Dart en [18] ms (pause de l'application 612 ms avant la diffusion), aucun verrouillage pendant la grâce true, verrouillage « ecran_eteint » (minuterie, 20009 ms après la sortie)
RAPPORT | WorkManager : commande reçue true (origine arriere-plan), reçue par le Mac 83 s après la programmation, retour au premier plan après 120 s
```

### Ce que les essais sur la tablette ont appris
- **Premier passage (3/11, 7 h) : 6/8.** (1) Hors de l'application, aucun verrouillage 94 s après la sortie avec une minuterie de 30 s ; la cause n'a pas été isolée (processus gelé par Android/ZUI, ou minuterie non déclenchée). La règle révisée ne dépend plus de la minuterie : contrôle au retour par l'horloge monotone (prouvé par les tests unitaires), minuterie en appoint. Aux passages suivants, la minuterie s'est déclenchée à l'heure (20 et 21 s pour une grâce de 20 s). (2) WorkManager : l'**essai** interrogeait le Mac pendant le mode avion (`Network is unreachable`) ; l'application, elle, avait bien envoyé la commande.
- **Android 15 coupe le réseau d'une application en arrière-plan** (constaté : aucune connexion possible au Mac tant que l'application n'est pas au premier plan, alors que le Wi-Fi est revenu). La tâche WorkManager, elle, a le réseau. Conséquence pour l'essai : toutes les demandes au Mac (accueil, rallumage, retour) partent avant la sortie et sont différées par le Mac. Conséquence pour US-13/US-14 : **tout envoi hors du premier plan passe par WorkManager**, jamais par une boucle Dart de l'application.
- **Tablette verrouillée = clés de consultation inutilisables** : avec l'écran de verrouillage affiché, le Keystore refuse les clés « tablette déverrouillée exigée » (`InvalidKeyException`, constaté) ; c'est voulu. Le lanceur attend désormais que la tablette soit déverrouillée.
- Le Wi-Fi de la M11 revient en 18 à 29 s après le mode avion ; WorkManager a envoyé environ 1 min après (écran éteint), sans intervention.

## Mesures sur l'émulateur (31 octobre 2026, avant corrections)
Android 14 (API 34), image `google_apis` arm64, 4 Go de RAM, Keystore **logiciel**. Dernier passage complet conservé : **4 scénarios sur 8** (Keystore et Argon2id, profils chiffrés, limite de 24 h avec heure reculée puis avancée de 2 jours par `adb root`, révocations). Échecs : 5 PIN faux (`Cannot modify an unmodifiable list`, corrigé depuis, voir plus bas), photo et WorkManager (émulateur trop lent : réduction en 30 s, réseau revenu en 30 à 40 s), verrouillage. Temps dans le tableau ci-dessus : 5 à 10 fois ceux de la tablette, non représentatifs. Seul l'émulateur a permis l'essai d'**heure modifiée** (`adb root`) : reculer l'heure de 2 jours ne prolonge pas le délai, l'avancer de 2 jours le fait expirer. L'émulateur n'a pas été relancé après les corrections : la tablette fait foi.

## Preuves automatiques
- `mobile/eng/verify.sh` (CI « Application Flutter ») : analyse stricte sans remarque, **75 tests réussis** dont **58 pour POC-03** (`mobile/test/poc/tablette/`, dont 12 pour le verrouillage révisé : grâce après écran éteint et sortie, retour avant et après la grâce, processus gelé, durées raccourcies), couverture des lignes Dart 95,7 % (3/11/2026). Ils utilisent la vraie bibliothèque SQLCipher (fichiers illisibles sans clé, clé d'un autre profil refusée), un Keystore logiciel de substitution et un serveur factice HTTP.
- `mobile/tool/poc03/lancer_essais.sh <numéro> [adresse du Mac]` : 8 scénarios sur Android avec le vrai Keystore, SQLCipher, WorkManager et de vraies coupures (mode avion par adb). **8/8 sur la Tab M11 le 3/11/2026** ; lignes « RAPPORT | » ci-dessus. La personne présente déverrouille la tablette quand l'écran se rallume (deux fois par passage) : l'outil ne connaît jamais le code de la tablette.

## Défauts trouvés par les essais sur Android (corrigés)
- Le canal de méthode rend des octets en lecture seule : la remise à zéro de la clé en mémoire échouait (`Cannot modify an unmodifiable list`). Copie modifiable avant usage ; test ajouté.
- Le Keystore refuse la clé de consultation sans verrouillage d'écran (voir plus haut) : condition désormais documentée et vérifiée.
- Tablette (3/11) : l'essai WorkManager et l'essai de verrouillage interrogeaient le Mac pendant le mode avion ou application en arrière-plan ; demandes désormais envoyées avant la sortie. La grâce de l'essai d'écran éteint était mesurée depuis la diffusion de l'extinction, alors qu'elle part de la pause de l'application, 0,6 s plus tôt : mesure corrigée.

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
1. **Pas de StrongBox sur la M11** : clés dans le TEE (protection matérielle, non extractibles), ce qui suffit au modèle de menace du cadrage (copie des fichiers, tablette perdue). Mesures faites en mode débogage (JIT) ; la mesure en version « profile » (AOT) reste à faire avec le porteur.
2. **Coût d'Argon2id en Dart pur** (pointycastle) : 722 ms sur la M11 en débogage, déverrouillage complet 736 ms, au-dessus de la cible d'environ 300 ms (cadrage T-01) mais sous 1 s. Les paramètres sont enregistrés avec chaque clé scellée : on peut les ajuster sans casser les profils. Si la version « profile » dépasse ~1 s, passer à une implémentation native (libsodium, ou Argon2 côté Kotlin) ou réduire la mémoire (m = 8 Mio).
3. **Limite de 5 PIN faux appliquée par l'application** (compteur dans la file chiffrée par le Keystore). Sur une tablette rootée, un attaquant qui contrôle l'application peut remettre le compteur à zéro et essayer les 10⁶ PIN, à raison d'un appel Keystore + Argon2id par essai (environ 3 à 30 jours selon le temps mesuré). Sans root, aucune attaque hors de l'appareil n'est possible (clé non extractible). Risque à consigner dans SECURITE, avec la limite de 24 h et la révocation comme parades.
4. **File d'envoi lisible sans PIN** par l'application (T-02, accepté au cadrage) : les saisies en attente d'un collègue ne sont pas affichées, mais l'application y a accès techniquement.
5. **Temps tablette éteinte** : après un redémarrage hors réseau, il n'est compté que par l'heure de la tablette ; éteindre la tablette ET reculer l'heure prolonge la consultation de la durée d'extinction. Parade possible en US-13 : refuser la consultation après un redémarrage tant qu'aucun contact n'a eu lieu (plus strict), à trancher par le porteur si nécessaire.
6. **Clé en mémoire Dart** : remise à zéro après usage, mais le ramasse-miettes peut en garder des copies ; SQLCipher efface sa propre mémoire (`cipher_memory_security`).
7. **Réseau** : la M11 retrouve le Wi-Fi 18 à 29 s après un mode avion ; Android 15 coupe le réseau d'une application en arrière-plan, seule une tâche WorkManager (ou un service de premier plan) peut alors envoyer.
8. **Arrière-plan Lenovo** : WorkManager a envoyé en ≈ 1 min, écran éteint depuis 2 min ; le comportement après une longue veille (Doze, gestion de batterie ZUI) n'est pas mesuré : essai d'1 h sur batterie à faire avec le porteur.
9. **Saisies d'une organisation coupée** : gardées sur la tablette (`acces_coupe`) et jamais envoyées ; leur passage en quarantaine (Q7) est à décider en US-14.
10. **Grâce de 5 min** : pendant la grâce, quelqu'un qui récupère la tablette déverrouillée retrouve le profil ouvert ; parades : verrouillage d'écran de la tablette (obligatoire, T-02), contenu masqué hors de l'application, bouton « Verrouiller / changer d'utilisateur ».

## Essais sur la tablette
### Faits automatiquement (3/11/2026)
Étapes 1, 2 et 4 du protocole d'origine : identification (`adb devices`, SDK 35, pas de StrongBox), les 8 scénarios automatiques (`tool/poc03/lancer_essais.sh <numéro> http://<IP-du-Mac>:8765`), fichiers illisibles sans clé (vérifiés par l'essai). Verrouillage (étape 9) : fait automatiquement avec une grâce raccourcie à 20 s.

### À faire avec le porteur
Prérequis : tablette avec un verrouillage d'écran, Mac et tablette sur le même Wi-Fi, Flutter 3.47.1, serveur factice `dart run tool/poc03/serveur_hote.dart` lancé à part pour l'écran technique (`flutter run -t lib/poc/tablette/main_poc03.dart -d <numéro>`), comptes de démonstration créés par `curl -X POST http://localhost:8765/controle/compte -d '{"id":"<uuid>","email":"a@demo.invalid","motDePasse":"demo-demo-demo","organisations":["org-x"]}'`. Aucune donnée réelle.

1. **Mesure en version « profile »** (AOT, représentative) : `flutter build apk --profile -t lib/poc/tablette/main_poc03.dart`, `adb install -r build/app/outputs/flutter-apk/app-profile.apk`, ouvrir l'application, bouton « Mesures », puis `adb logcat -d -s flutter | grep RAPPORT`. Attendu : Argon2id et déverrouillage complet sous 1 s (idéal ~300 ms).
2. **Heure modifiée** (la tablette n'a pas `adb root`) : préparer un profil, couper le Wi-Fi, reculer la date de 2 jours dans Paramètres (heure automatique désactivée), « État » : le temps consommé ne diminue pas ; avancer de 2 jours : « Déverrouiller » répond `expire` ; remettre l'heure automatique.
3. **Redémarrage hors réseau** : profil prêt, mode avion, saisie, redémarrer la tablette, rouvrir : la saisie est là, « État » indique `heure garantie : false` et un temps consommé qui reprend le dernier pointage.
4. **Arrêt forcé** : saisie hors réseau, `adb shell am force-stop fr.gestionsite.terrain`, rouvrir : file intacte (« État »).
5. **Arrière-plan et batterie Lenovo (1 h)** : profil prêt, mode avion, « Saisie avec photo » ×3, « Programmer l'envoi en arrière-plan », quitter l'application (sans la forcer à s'arrêter), éteindre l'écran, désactiver le mode avion, laisser 1 h sur batterie. Attendu : les 3 commandes dans `curl http://localhost:8765/controle/etat` avec `"origine":"arriere-plan"` ; noter `recuLe`. Refaire avec l'optimisation de batterie Lenovo active et inactive ; `adb shell dumpsys jobscheduler | grep -A5 fr.gestionsite` montre les tâches.
6. **Coupure Wi-Fi réelle pendant 3 photos** : « Saisie avec photo » ×3, « Synchroniser » et couper le Wi-Fi à la main pendant l'envoi, le rétablir, « Synchroniser » : `controle/etat` montre chaque fichier complet une fois et chaque commande appliquée une fois.
7. **Verrouillage avec les vraies durées** : profil ouvert ; écran éteint 4 min puis retour → ouvert ; 6 min → verrouillé ; application quittée 6 min → verrouillée ; aperçu des applications récentes vide ; capture d'écran refusée ; 5 min sans toucher → verrouillé.

Consigner les résultats dans la section « Mesures sur la tablette », avec la date.

## Recommandation pour ADR-03
**Adopter pour US-13/US-14 la pile essayée, conditionnellement à la mesure « profile » :**
- **SQLCipher** (édition communautaire 4.19, via l'option `source: sqlcipher` du paquet `sqlite3`) pour chaque base locale : une base de consultation par organisation et par profil, une file d'envoi par profil ; drift possible au-dessus.
- **Clés Android Keystore, niveau TEE** (StrongBox si l'appareil en a une), non extractibles, par un greffon Kotlin local : clé de consultation exigeant la tablette déverrouillée, clé de file d'envoi utilisable écran verrouillé pour WorkManager.
- **Argon2id en Dart** (pointycastle, m = 19 Mio, t = 2, p = 1) pour sceller la clé de consultation par le PIN : 722 ms, déverrouillage complet 736 ms sur la M11 en débogage. Si la version « profile » dépasse 1 s : Argon2 natif (libsodium ou Kotlin) ou m = 8 Mio, sans casser les profils (paramètres enregistrés avec chaque clé).
- **WorkManager** pour tout envoi hors du premier plan (Android 15 coupe le réseau des applications en arrière-plan) ; envoi photo par morceaux reprenables et commandes idempotentes.
- **Verrouillage** selon R13-05 révisée : grâce contrôlée au retour par l'horloge monotone, `FLAG_SECURE` sur toute l'activité.
ADR-03 peut passer de « proposée » à « adoptée » côté stockage local et hors connexion une fois la mesure « profile » et l'essai d'1 h sur batterie faits avec le porteur ; POC-01 (capteurs) reste une condition distincte.

# État courant et journal de reprise

**À lire en premier par tout agent ou développeur qui reprend le travail**, avant AGENTS.md et les cartes. Ce fichier est mis à jour à chaque étape significative : US ou sous-carte terminée, décision prise, PR ouverte ou fusionnée, blocage. Il doit permettre de reprendre sans aucun historique de conversation.

Dernière mise à jour : 28 octobre 2026 (heure de Paris), par l'agent IA.

## Où en est le projet
- Cadrage v0.2 terminé. Décisions DEC-01 à DEC-05 tranchées (REGLES, PERMISSIONS, PROCESSUS) ; 50 cas de test spécifiés, non exécutés (RECETTE).
- Stack adoptée : ADR-08 (ASP.NET Core .NET 10, PostgreSQL, Flutter, Blazor ; préproduction Cloudflare Containers + Neon ; R2, Turnstile, Tunnel).
- Qualité adoptée : ADR-09 et **opérationnelle** : CI GitHub Actions + SonarQube Cloud obligatoires sur `main` (6 vérifications requises, liste dans docs/dev/GUIDE.md). L'analyse couvre tout le dépôt (C# et scripts Python).
- Trello : https://trello.com/b/NsEhG4sm ; l'agent déplace les cartes avec `scripts/trello/card.py` (ou l'API Trello). Livrées le 28/10 : S05-01, US-05, S06-01 à S06-06, US-06, WEB-01, DEP-01.
- **Préproduction en service** depuis le 28/10 (docs/exploitation/DEPLOIEMENT.md) : redéploiement `cd deploy/cloudflare && npx wrangler deploy`, puis `deploy/verification/parcours.py`.
- Application terrain Flutter fusionnée (PR #15) : `mobile/`, écran technique provisoire sans design, porte `mobile/eng/verify.sh`, Sonar `princemzb_gestionSite_mobile` (Dart + natif), version finale protégée (signature obligatoire, obfuscation, HTTP interdit, sauvegarde désactivée).
- Socle serveur fusionné sur `main` (PR #5, #14) : solution .NET 10, 6 modules isolés ; environnement local Docker (PostgreSQL 17.6 port 55433, SeaweedFS S3 port 58333) ; `/health` (vivacité) et `/health/ready` (base + stockage, cache 10 s, délai 5 s) ; 40 tests dont tests d'intégration Testcontainers ; couverture ≈ 99 %.

## État Sonar de `main` (27/10, 16:00)
Porte OK ; 838 lignes ; couverture globale 77,9 % (C# 100 %, scripts Python 76 %) ; 0 bug ; 0 code smell ; 0 vulnérabilité ; note de sécurité A. S2083 sur `publish_backlog.py` (`save()`) qualifié « False Positive » avec justification le 27/10 (chemin d'écriture constant). Toute modification future des scripts doit avoir ≥ 80 % de couverture sur le nouveau code.

## Travail en cours
| Élément | État | Prochaine action |
| --- | --- | --- |
| **Sprint SP-003 : US-08 Dépôt public et QR** (choix du porteur 28/10 : US-08 puis US-07) | **Le porteur délègue à l'agent la validation des choix jusqu'à la fin du sprint (29/10)**. S08-01 à S08-05 fusionnées (PR #44 à #48, c95e9c7) et **déployées en préproduction le 29/10** (migrations Neon appliquées : codes publics des 3 sites existants, schéma maintenance ; secrets TURNSTILE_SECRET et TURNSTILE_CLE_SITE posés) ; `deploy/verification/us08.mjs` 9/9 ; widget Turnstile « gestionsite-preprod » créé par l'agent. | **Porteur** : recette sur téléphone (QR → signalement avec photo → Dépôts reçus via un technicien invité) ; puis Livré d'US-08 ; ensuite US-07 (inventaire). |
| **US-07 Inventaire** (suite du sprint SP-003) | **Livrée le 29/10** (Trello : US-07, S07-01..03 en Livré, preuve sur chaque carte). PR #51 à #54 fusionnées ; préproduction redéployée (main 234cdb3) ; `us07.mjs` 12/12, captures Jour/Nuit conformes à la maquette A. Choix métier de l'agent révisables (REGLES « Inventaire »). | Porteur : recette d'US-07 à sa convenance (lien de démonstration) ; recette téléphone d'US-08 en attente. Reportés : SOC-01, type d'ERP du site (US-12), photos de composants (US-11). |
| **Sprint SP-004** (ouvert le 29/10 par l'agent ; le porteur, occupé, demande de poursuivre) | **Terminé de mon côté.** SOC-01 livrée (PR #56). **US-09 livrée le 29/10** (Trello : US-09, S09-01..05 en Livré, preuve sur chaque carte) : PR #58 à #64 ; préproduction redéployée (main 586c0fd), migrations Maintenance sur Neon ; `us09.mjs` 10/10, non-régression `us07.mjs` 12/12 et `us08.mjs` 9/9. Décisions du porteur du 29/10 dans REGLES « Traitement des dossiers — US-09 ». | Porteur : recettes US-07, US-08 (téléphone), US-09 à sa convenance. Prochaine story au choix du porteur : US-11 (évaluations et score, dépendances livrées) ou US-10 (danger, relances). Reportés : SOC-02 ; photos orphelines si l'enregistrement échoue (le stockage n'a pas de suppression). |
| US-06 Comptes et droits | **Livrée le 28/10** (S06-01 à S06-06) : en service en préproduction | — |
| PREP-06 POC commandes hors connexion | Terminée (PR #32, 3b2e968) : protocole idempotent + file par profil, rapport exécutable 4 × OK (docs/poc/PREP-06.md) ; conception à reprendre dans US-14 | PREP-07 (essais de localisation sur site) et PREP-08 (cas IA, service externe) : décision et actions du porteur nécessaires. |
| PREP-03 / PREP-04 préparation POC-01 et POC-02 | Protocoles proposés : docs/poc/PREP-03-protocole-localisation.md, docs/poc/PREP-04-cas-ia.md ; en Recette (validation du porteur) | Porteur : site et dates d'essai, relecture des 30 cas par le terrain, fournisseur et budget IA, politique d'images. Puis PREP-07 / PREP-08. |
| S05-02 Docker local (PostgreSQL, SeaweedFS S3) | Fusionné (PR #14) : `server/compose.yaml`, `/health/ready` | Livré (environnement local utilisable). |
| Maquettes (carte UX-01) | **Validées le 28/10** : charte (Jour + Nuit) et parcours du 1er incrément (docs/ux/CHARTE.md) | Les autres écrans seront maquettés et validés avec leurs US, avant tout code d'écran. |
| Maquettes du 1er écran testable (carte UX-02) | **Validées le 28/10** : connexion B (bandeau) + membres C (par site), Jour et Nuit | Maquette « Activer mon compte » à faire valider ; puis application web Blazor et première mise en préproduction. |
| WEB-01 Application web (1er écran testable) | **Livrée le 28/10** : en service en préproduction, parcours vérifié 10/10 | Recette du porteur (retours = nouvelles cartes). Écrans suivants (sites, révision/révocation, journal) : maquettes à valider d'abord. |
| DEP-01 Préproduction | **Livrée le 28/10** (PR #41, 77cc843) : Cloudflare (adresse workers.dev du compte, Trousseau `gestionsite-preprod-url`), Neon Francfort PG 17, R2 UE ; `deploy/verification/parcours.py` 10/10 sur une organisation fictive de vérification ; revues code + sécurité, Sonar OK. Organisation de démonstration du porteur : admin fictif `admin@demo.gestionsite.test`, lien d'activation valable jusqu'au 31/10. | Porteur : recette dans son navigateur ; alerte de facturation Cloudflare. |
| S05-03 Projet Flutter | Livré : APK installé et lancé sur la Lenovo Tab M11 le 27/10 | — |
| S05-05 Guide d'installation | Livré (PR #16) | — |
| S05-01 / US-05 | **Livrées le 28/10** (premier déploiement préprod) | — |

## Blocages et actions du porteur
1. **Maquettes** : aucun visuel ni écran designé avant validation par le porteur de plusieurs propositions de maquettes ; s'arrêter et attendre sa décision.
2. **iPad** non disponible pour les essais (POC-01, POC-03).
3. **Décisions ouvertes rencontrées** : iPhone encore ciblé et iOS 15 minimum dans le projet Flutter (valeurs du gabarit) — à trancher avec DEC-11 (usage téléphone) et DEC-06 (versions minimales).

## Accès outillés
- Préproduction : `neonctl` (OAuth, organisation Neon du porteur) et `wrangler` (OAuth, compte Cloudflare du porteur) connectés sur le poste ; secrets dans le Trousseau `gestionsite-preprod-*` ; fusion de PR autorisée à l'agent (`Bash(gh pr merge:*)`, accord du porteur le 28/10).
- Trello : clé et jeton dans le Trousseau (`gestionsite-trello-key`, `gestionsite-trello-token`).
- Sonar : jeton CI en secret GitHub `SONAR_TOKEN` ; jeton utilisateur pour l'API Sonar dans le Trousseau (`gestionsite-sonar-token`).

## Règles de travail en vigueur (rappel)
- Tout passe par branche et PR ; `main` est protégée. Revue `/code-review` high (max pour budget, stock, synchronisation, droits, isolation ; `/security-review` pour auth, fichiers, formulaire public) jusqu'à zéro constat confirmé.
- Fusion par l'agent autorisée (`gh pr merge`) seulement quand revue, CI et Sonar sont au vert.
- Carte Trello mise à jour au début (En cours) et à chaque étape, avec preuve pour Revue, Recette, Prêt à livrer, Livré : `python3 scripts/trello/card.py move <ID> "<Liste>" --proof "…" --apply`.
- Le porteur préfère des questions en QCM, une décision à la fois.
- Décision du porteur (28/10) : essais PREP-07 (localisation) et PREP-08 (IA) reportés ; prochaine étape : maquettes du premier écran testable (connexion + gestion des membres, web admin), plusieurs propositions puis validation avant tout code d'écran.
- Décision du porteur (28/10) : « Relancer » — le technicien n'a pas ce droit ; le pasteur relance en interne seulement (PERMISSIONS.md corrigé).
- Décision du porteur (28/10) : la préproduction (Neon + Cloudflare, ADR-08) sera mise en place quand un utilisateur pourra tester quelque chose (premier écran maquetté et validé) ; d'ici là, les cartes fusionnées restent en « Prêt à livrer ».
- Décision du porteur (28/10, révisée le même jour) : revue allégée par PR. Chaque PR : une revue `/code-review high`, plus `/security-review` si auth, fichiers ou formulaire public ; `max` seulement pour budget et stock. Défauts avérés corrigés avant fusion (CI + porte Sonar verts), suggestions en « Points reportés ». Cartes Trello déplacées à chaque étape.

## Points reportés (issus des revues de S06-01)
- DEP-01 (revue PR #41) : vérifier en usage réel que `setInactivityTimeout` compte depuis la dernière activité (sinon renouveler à chaque requête) ; cas « image précédente » non encore observé en vrai (seul le redémarrage sur veille l'a été).
- Hébergement de production (ADR à venir) : réveil ≈ 20 s et mode `durable_object` en bêta publique ; mesurer la mémoire réelle puis comparer conteneur Cloudflare toujours actif (≈ 28 $/mois en `standard-1`) et hébergeur UE classique.
- Option Hyperdrive mentionnée par l'ADR-08 non utilisée (connexion directe à Neon) : à réévaluer avec la latence mesurée.
- Revue PR #47 (S08-04), reporté : corps > 34 Mo sur le formulaire public → page d'erreur générique (lecture du formulaire avant la page) ; dérivation des rôles de l'appelant répétée dans les pages (Sites, Membres, navigation).
- Revue PR #46 (S08-03), reporté : le corps (photos) est lu avant la vérification anti-robot (mémoire par requête) ; atténué par la limite par adresse de l'application web (S08-04) et le décodage borné.
- Revue PR #45 (S08-02), non vérifié : double rotation possible d'une photo HEIC portrait (libheif applique déjà la rotation, AutoOrient pourrait la refaire) ; HEIC lisible mais pas encodable ici, donc pas de test : à vérifier avec une vraie photo portrait d'iPhone en recette. Fichiers orphelins possibles (envoi concurrent perdant, échec de la base après écriture) : nettoyage à prévoir.
- Socle (revue PR #51) : le 3e module (Patrimoine) recopie la garde d'écriture, la persistance et la traduction des refus de Maintenance : carte SOC-01 créée (contexte EF de base et filtre d'erreurs communs dans le noyau).
- Revue PR #44 (S08-01), reporté : contexte EF de base commun (filtres, garde d'écriture, aide SQL RLS des migrations) au lieu de leur copie par module ; à faire au 3e module.
- Aide de persistance commune aux modules (migrateur générique) : les intercepteurs et marqueurs sont dans le noyau depuis S08-01 ; le migrateur reste dupliqué (Identité, Maintenance), à factoriser au 3e module.
- Règles de nom et de date extraites en S06-02 (`NomAffichable`, `Horodatage`) ; une convention EF globale reste possible si d'autres modules en ont besoin.
- Noms des rôles PostgreSQL figés (`gestionsite_app` cité dans les GRANT des migrations) : contrat pour la préproduction.
- Sonde /health/ready sur une base migrée seulement jusqu'à S06-01 : « invérifiable » (droits absents) au lieu de « en attente » ; sans effet, aucune base partagée dans cet état.
- Écritures : BEGIN + SET LOCAL + DML + COMMIT (3 allers-retours) ; regrouper SET LOCAL avec la première instruction si la latence Neon le justifie.
- `AutoTransactionBehavior.Always` posé dans SaveChanges ; Organisation traitée à part (filtre et contrôle) : à unifier si une 2e entité racine apparaît.
- Authentification (S06-03) : pas de fenêtre de grâce sur un jeton de renouvellement déjà remplacé (une réponse perdue puis rejouée révoque la session de l'appareil) — à arbitrer avec le hors connexion (US-09) ; pas de durée absolue de session (renouvellement glissant) ; jeton d'accès valable jusqu'à 15 min après déconnexion (ADR-10) ; 423 « verrouillé » révèle l'existence d'un compte après 5 échecs (accepté par CONTRATS) ; limitation de débit sur /auth à ajouter avant exposition publique.
- Journal des habilitations (RG-31) en place (S06-06). Une organisation dont l'unique administrateur invité refuse doit être rouverte par la plateforme.
- Décision du porteur (28/10) : adhésion par invitation à accepter (ADR-10 complété, CONTRATS, RISQUES : préemption d'adresse jusqu'à US-18). Fusionnée (PR #28).
- Décision du porteur (28/10) : révocation d'une adhésion = accès à cette organisation coupé dès la requête suivante ; la personne reste connectée pour ses autres organisations (CONTRATS).
- T-01.10 à T-01.18 : décisions automatisées en S06-05 ; à rejouer de bout en bout avec les modules Maintenance, Budget, Patrimoine (dossiers, budgets, grilles, plans). Droit nominatif « plans » attribuable par A : US-21.
- Application web (WEB-01) : stockage durable des clés de chiffrement des cookies (`Web:DossierCles`) à choisir au déploiement (disque éphémère des conteneurs) ; partage des renouvellements de jetons en mémoire d'une seule instance web ; bascule de thème Jour/Nuit par compte, écrans sites, révision/révocation et journal à maquetter.
- `Membre.Accorder` suppose les rôles chargés (Include) : à garantir par le service de S06-04.
- Préproduction : créer les rôles `gestionsite_migrator` / `gestionsite_app` (sans le script local, mots de passe hors dépôt) au premier déploiement.
- Image publiée : Microsoft.EntityFrameworkCore.Design est copié à la publication ; l'exclure au premier déploiement (S05-01/OUT).
- Sonde /health/ready : 1 + 2×N requêtes sur les réponses 503 non mises en cache ; acceptable à ce stade, à revoir si Neon le facture.
- Tests d'intégration : supposent un démon Docker local (hôte de boucle locale) à cause du garde-fou de migration.

## Comment reprendre
1. Lire ce fichier, puis AGENTS.md, puis la carte concernée.
2. `git fetch && gh pr list` ; `python3 scripts/trello/card.py status`.
3. Vérifier le socle : `server/eng/verify.sh` (build, format, tests, couverture ≥ 80 %, dépendances) ; SDK fixé par `global.json` à la racine.
4. Reprendre la « Prochaine action » du tableau ci-dessus.

## Journal
| Date | Étape | Références |
| --- | --- | --- |
| 27/10/2026 | Dépôt créé, cadrage versionné, rôles du porteur consignés | 1430879, 9b76950 |
| 27/10/2026 | Trello : clé et jeton dans le Trousseau ; 39 cartes publiées | 0bd762d, fd2358a |
| 27/10/2026 | ADR-08 stack adoptée | 5e5b1c5 |
| 27/10/2026 | DEC-01 à DEC-05 tranchées ; 50 cas de test | 8906d16 → 6a2bdf9, PR #2 |
| 27/10/2026 | ADR-09 qualité ; dépôt public ; protection de `main` | PR #1 |
| 27/10/2026 | Outil `card.py` (7 passages de revue, 22 tests) ; OUT-01 Livré | PR #3 |
| 27/10/2026 | US-05 découpée (S05-01 à S05-05) | PR #4 |
| 27/10/2026 | S05-01 solution .NET (PR #5) et S05-04 CI (PR #6) ouvertes, en attente de Sonar | PR #5, #6 |
| 27/10/2026 | Journal de reprise ETAT.md créé | PR #7 |
| 27/10/2026 | Incident : PR #7 a inclus 1785 fichiers bin/obj (aucun source ni secret) ; retirés et `.gitignore` racine renforcé. Précaution : vérifier `git status` avant `git add -A` après un changement de branche | PR #8 |
| 27/10/2026 | Sonar connecté (analyse automatique) : 3 vulnérabilités dans l'outillage Trello ; correctif en PR, à confirmer par une nouvelle analyse Sonar | PR #10 |
| 27/10/2026 | Sonar : jeton CI en secret, analyse automatique désactivée ; PR #5 (socle) et #6 (CI) fusionnées ; vérifications obligatoires sur `main` ; analyse du dépôt entier | PR #5, #6, #11 |
| 27/10/2026 | S2083 qualifié faux positif par l'API Sonar ; `main` : 0 vulnérabilité, note A | Sonar |
| 27/10/2026 | S05-02 : environnement local Docker et disponibilité base/stockage (3 passages de revue) | PR #14 |
| 27/10/2026 | S05-03 : projet Flutter ; Sonar mobile séparé (le scanner .NET n'analyse pas le Dart) | PR #15 |
| 27/10/2026 | S05-03 fusionné (Sonar mobile séparé, 2 passages de revue) ; 6 vérifications obligatoires sur `main` ; S05-05 guide d'installation | PR #15, #16 |
| 27/10/2026 | US-05 socle : S05-02/03/04/05 livrés ; S05-01 attend le premier déploiement préprod. Prochaine étape à choisir par le porteur : POC hors connexion (PREP-06), propositions de maquettes (arrêt pour validation) ou US-06 | Trello |
| 27/10/2026 | Propositions de maquettes publiées (3 directions × 3 écrans), en attente de validation | canevas Design |
| 27/10/2026 | 2e série de maquettes (charte du site de référence + 3 versions), en attente de validation | canevas Design |
| 27/10/2026 | Charte validée : V1 Jour (défaut) + V2 Nuit, bascule utilisateur | docs/ux/CHARTE.md |
| 28/10/2026 | Parcours 1er incrément maquetté en Jour et Nuit (8 écrans), en attente de validation | canevas Design |
| 28/10/2026 | Maquettes du parcours du 1er incrément validées par le porteur | docs/ux/CHARTE.md |
| 28/10/2026 | US-06 cadrée : ADR-10, contrats, 6 sous-cartes | PR cadrage US-06 |
| 28/10/2026 | S06-01 persistance EF Core en revue (2 passages /code-review traités) | PR #24 |
| 28/10/2026 | S06-02 fusionnée après 3 passages de revue (max puis 2 high) ; organisation posée par SET LOCAL (compatible pooler) | PR #25 |
| 28/10/2026 | S06-01 : 11 passages de revue (EF 10.0.4/10.0.14 unifié, garde-fou migration locale, dates, noms) ; revue bornée à 3 passages désormais, après remarque du porteur sur la lenteur | PR #24 |

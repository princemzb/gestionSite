# État courant et journal de reprise

**À lire en premier par tout agent ou développeur qui reprend le travail**, avant AGENTS.md et les cartes. Ce fichier est mis à jour à chaque étape significative : US ou sous-carte terminée, décision prise, PR ouverte ou fusionnée, blocage. Il doit permettre de reprendre sans aucun historique de conversation.

Dernière mise à jour : 27 octobre 2026, 18:45 (heure de Paris), par l'agent IA.

## Où en est le projet
- Cadrage v0.2 terminé. Décisions DEC-01 à DEC-05 tranchées (REGLES, PERMISSIONS, PROCESSUS) ; 50 cas de test spécifiés, non exécutés (RECETTE).
- Stack adoptée : ADR-08 (ASP.NET Core .NET 10, PostgreSQL, Flutter, Blazor ; préproduction Cloudflare Containers + Neon ; R2, Turnstile, Tunnel).
- Qualité adoptée : ADR-09 et **opérationnelle** : CI GitHub Actions + SonarQube Cloud obligatoires sur `main` (6 vérifications requises, liste dans docs/dev/GUIDE.md). L'analyse couvre tout le dépôt (C# et scripts Python).
- Trello : https://trello.com/b/NsEhG4sm ; l'agent déplace les cartes avec `scripts/trello/card.py`.
- Application terrain Flutter fusionnée (PR #15) : `mobile/`, écran technique provisoire sans design, porte `mobile/eng/verify.sh`, Sonar `princemzb_gestionSite_mobile` (Dart + natif), version finale protégée (signature obligatoire, obfuscation, HTTP interdit, sauvegarde désactivée).
- Socle serveur fusionné sur `main` (PR #5, #14) : solution .NET 10, 6 modules isolés ; environnement local Docker (PostgreSQL 17.6 port 55433, SeaweedFS S3 port 58333) ; `/health` (vivacité) et `/health/ready` (base + stockage, cache 10 s, délai 5 s) ; 40 tests dont tests d'intégration Testcontainers ; couverture ≈ 99 %.

## État Sonar de `main` (27/10, 16:00)
Porte OK ; 838 lignes ; couverture globale 77,9 % (C# 100 %, scripts Python 76 %) ; 0 bug ; 0 code smell ; 0 vulnérabilité ; note de sécurité A. S2083 sur `publish_backlog.py` (`save()`) qualifié « False Positive » avec justification le 27/10 (chemin d'écriture constant). Toute modification future des scripts doit avoir ≥ 80 % de couverture sur le nouveau code.

## Travail en cours
| Élément | État | Prochaine action |
| --- | --- | --- |
| S05-02 Docker local (PostgreSQL, SeaweedFS S3) | Fusionné (PR #14) : `server/compose.yaml`, `/health/ready` | Livré (environnement local utilisable). |
| S05-03 Projet Flutter | Fusionné (PR #15), Prêt à livrer | Installer l'APK debug sur la tablette Lenovo (débogage USB) puis passer en Livré. |
| S05-05 Guide d'installation | En revue (README racine et GUIDE réorganisé) | Fusionner ; US-05 alors terminé (sauf livraison préprod de S05-01 et installation de S05-03). |
| S05-01 | Prêt à livrer | Livré au premier déploiement préprod. |

## Blocages et actions du porteur
1. **Maquettes** : aucun visuel ni écran designé avant validation par le porteur de plusieurs propositions de maquettes ; s'arrêter et attendre sa décision.
2. **iPad** non disponible pour les essais (POC-01, POC-03).
3. **Décisions ouvertes rencontrées** : iPhone encore ciblé et iOS 15 minimum dans le projet Flutter (valeurs du gabarit) — à trancher avec DEC-11 (usage téléphone) et DEC-06 (versions minimales).

## Accès outillés
- Trello : clé et jeton dans le Trousseau (`gestionsite-trello-key`, `gestionsite-trello-token`).
- Sonar : jeton CI en secret GitHub `SONAR_TOKEN` ; jeton utilisateur pour l'API Sonar dans le Trousseau (`gestionsite-sonar-token`).

## Règles de travail en vigueur (rappel)
- Tout passe par branche et PR ; `main` est protégée. Revue `/code-review` high (max pour budget, stock, synchronisation, droits, isolation ; `/security-review` pour auth, fichiers, formulaire public) jusqu'à zéro constat confirmé.
- Fusion par l'agent autorisée (`gh pr merge`) seulement quand revue, CI et Sonar sont au vert.
- Carte Trello mise à jour au début (En cours) et à chaque étape, avec preuve pour Revue, Recette, Prêt à livrer, Livré : `python3 scripts/trello/card.py move <ID> "<Liste>" --proof "…" --apply`.
- Le porteur préfère des questions en QCM, une décision à la fois.

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

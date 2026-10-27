# État courant et journal de reprise

**À lire en premier par tout agent ou développeur qui reprend le travail**, avant AGENTS.md et les cartes. Ce fichier est mis à jour à chaque étape significative : US ou sous-carte terminée, décision prise, PR ouverte ou fusionnée, blocage. Il doit permettre de reprendre sans aucun historique de conversation.

Dernière mise à jour : 27 octobre 2026, 15:10 (heure de Paris), par l'agent IA.

## Où en est le projet
- Cadrage v0.2 terminé. Décisions DEC-01 à DEC-05 tranchées (REGLES, PERMISSIONS, PROCESSUS) ; 50 cas de test spécifiés, non exécutés (RECETTE).
- Stack adoptée : ADR-08 (ASP.NET Core .NET 10, PostgreSQL, Flutter, Blazor ; préproduction Cloudflare Containers + Neon ; R2, Turnstile, Tunnel).
- Qualité adoptée : ADR-09 (dépôt public, SonarQube Cloud, porte stricte, revue `/code-review` graduée, protection de `main`).
- Trello publié : https://trello.com/b/NsEhG4sm. L'agent déplace les cartes (délégation du porteur) avec `scripts/trello/card.py`.
- Socle US-05 en cours, découpé en S05-01 à S05-05.

## Travail en cours
| Élément | État | Prochaine action |
| --- | --- | --- |
| PR #5 — S05-01 solution .NET modulaire (branche `feat/s05-01-solution-dotnet`) | Revue terminée (3 passages, 30 constats traités), 32 tests verts, couverture 100 % | **Ne pas fusionner avant Sonar** (ADR-09 : CI + Sonar avant le premier code applicatif). Fusionner #5 puis #6 dès que la porte Sonar passe. |
| PR #6 — S05-04 CI GitHub Actions (branche `ci/s05-04-github-actions`, empilée sur #5) | CI verte (run 40511755543) ; job Sonar présent mais non exécuté faute de jeton | Après création du jeton : relancer la CI, vérifier la porte Sonar, rendre les vérifications obligatoires sur `main`, puis fusionner #5 et #6. |
| PR #10 — vulnérabilités Sonar dans l'outillage Trello (S2083, S7044) | Corrigées : chemins d'API sur liste blanche exacte, backlog validé au chargement ; 28 tests | Revue `/code-review`, puis fusion ; vérifier que Sonar ne signale plus rien. |
| S05-02 Docker local (PostgreSQL, stockage S3) | Prêt | Peut démarrer sur une branche empilée sur #5. |
| S05-03 Projet Flutter | Prêt | Projet sans écran designé (maquettes non validées). |
| S05-05 Guide d'installation | Prêt | Après S05-02 et S05-04. |

## Blocages et actions du porteur
1. **SonarQube Cloud** : organisation `princemzb` et projet `princemzb_gestionSite` créés par le porteur le 27/10 (vérifié par l'API publique) ; porte « Sonar way ». Restent à faire par le porteur : désactiver l'« analyse automatique » (Administration → Analysis Method) et ajouter le secret de dépôt GitHub `SONAR_TOKEN` (jeton créé dans Sonar : My Account → Security).
2. **Maquettes** : aucun visuel ni écran designé avant que le porteur ait validé des maquettes. Présenter plusieurs propositions, puis s'arrêter et attendre sa décision.
3. **iPad** non disponible pour les essais (POC-01, POC-03).

## Règles de travail en vigueur (rappel)
- Tout passe par branche et PR ; `main` est protégée. Revue `/code-review` high (max pour budget, stock, synchronisation, droits, isolation ; `/security-review` pour auth, fichiers, formulaire public) jusqu'à zéro constat confirmé.
- Fusion par l'agent autorisée (`gh pr merge`) seulement quand revue, CI et Sonar sont au vert.
- Carte Trello mise à jour au début (En cours) et à chaque étape, avec preuve pour Revue, Recette, Prêt à livrer, Livré : `python3 scripts/trello/card.py move <ID> "<Liste>" --proof "…" --apply`.
- Le porteur préfère des questions en QCM, une décision à la fois.

## Comment reprendre
1. Lire ce fichier, puis AGENTS.md, puis la carte concernée.
2. `git fetch && gh pr list` ; `python3 scripts/trello/card.py status`.
3. Vérifier le socle : `server/eng/verify.sh` (build, format, tests, couverture ≥ 80 %, dépendances).
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
| 27/10/2026 | Sonar connecté (analyse automatique) : 3 vulnérabilités dans l'outillage Trello, corrigées | PR #10 |

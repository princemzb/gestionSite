# Instructions de travail pour développeurs et agents IA

Ce dépôt contient actuellement le cadrage, pas une application. Lire docs/README.md puis la carte affectée et ses références. La stack n’est pas encore adoptée.

## Sources de vérité
- Besoin : docs/PRD.md.
- Comportements : docs/metier/REGLES.md et PERMISSIONS.md.
- Choix techniques : docs/architecture/ADR.md ; toutes les entrées initiales sont proposées.
- Interfaces : docs/dev/CONTRATS.md et SYNCHRONISATION.md, encore préliminaires.
- Pilotage : docs/pilotage/TRELLO.md et BACKLOG.md ; Trello pour état, responsable et sprint après publication.

## Exécution
1. Prendre uniquement une carte assignée et Prête, avec décisions bloquantes résolues. Une instruction explicite de l’utilisateur peut autoriser un travail de cadrage ou d’expérimentation avant cette étape.
2. Ne pas inventer les réponses métier, validations, résultats de tests, URLs Trello, commandes de build ou preuves de faisabilité.
3. Avant code applicatif, confirmer stack, contrats et critères d’acceptation. Les POC restent explicitement expérimentaux.
4. Préserver isolation des organisations/sites, auteurs des opérations offline, transactions budgétaires et absence de trajet enregistré.
5. Tester le comportement, mettre à jour les documents liés et fournir une preuve réelle. Ne pas marquer Livré après simple génération de code ou fusion : il faut un déploiement réel vérifié. Par délégation du porteur, l’agent déplace les cartes Trello, Livré compris, preuves à l’appui (docs/pilotage/TRELLO.md).
6. Qualité (ADR-09) : branche + PR, porte Sonar stricte, revue `/code-review` graduée et `/security-review` selon le module, corrections jusqu’à zéro constat confirmé avant fusion.
7. Demander une décision ciblée si une ambiguïté change les droits, l’argent, les données personnelles ou le comportement métier. Continuer les travaux indépendants autorisés.
8. Aucun secret ni donnée personnelle réelle dans le dépôt ou Trello. Ne pas acheter, publier ou modifier un service externe sans autorisation applicable.

## Reprise
Terminer avec carte, fichiers/commit, travail réalisé, tests exécutés, limites, blocages et prochaine action. Ne pas attribuer une recette humaine à l’agent lui-même.

Les commandes applicatives seront documentées dans docs/dev/GUIDE.md lors du socle technique. Aucune n’est prescrite fictivement ici.

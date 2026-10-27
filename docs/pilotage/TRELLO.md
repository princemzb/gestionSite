# Pilotage Trello et exécution par agents
Statut : tableau publié le 27 octobre 2026 : [https://trello.com/b/NsEhG4sm](https://trello.com/b/NsEhG4sm) (espace de travail gestionSite, visible par ses membres). Les règles de passage ci-dessous restent proposées.

## Tableau
Nom proposé : « Patrimoine Églises — Produit & Livraison ».
Listes : À affiner → Prêt → Sprint sélectionné → En cours → Revue → Recette → Prêt à livrer → Livré. Ajouter « Bloqué » pour les attentes empêchant réellement la progression. Conserver la liste précédente dans la carte bloquée.
Labels publiés : modules du backlog (BA, POC, Préparation, Socle, Identité, Patrimoine, Maintenance, Terrain, Stock, Budget, IA, Spatial, Pilotage, SaaS, Exploitation, Livraison), sprints SP-001 à SP-003, Incident critique, Décision requise.
Champs (ou section structurée dans description si indisponibles) : identifiant, lot, sprint, estimation basse/haute, reste à faire, responsable, priorité, risque, dépendances, version cible.

## Carte exécutable obligatoire
Titre : [US-xx.yy] verbe + résultat observable.
Description : objectif utilisateur, inclus/exclus, références RG/UI/ADR, contrat et données concernés, préconditions, dépendances, critères Given/When/Then, tests et commandes, risques, migrations et retour arrière.
Checklist : préparation approuvée ; code ; tests ; revue humaine ; documentation ; recette ; preuve de déploiement.
Ajouter liens de branche/PR, exécution CI, captures utiles et version livrée. Une capture seule ne prouve pas l’isolation ou la synchronisation.

## Conditions de passage
- À affiner → Prêt : aucun arbitrage bloquant, contrat accepté, critères testables, dépendances prêtes, charge 1–5 j-p, commandes de test connues.
- En cours : responsable unique ; proposition de limite 2 cartes actives par développeur.
- Revue : PR disponible, résultats CI, documentation mise à jour.
- Recette : revue technique acceptée, environnement accessible, scénarios fournis.
- Prêt à livrer : recette approuvée par responsable habilité, déploiement/retour arrière préparés.
- Livré : version effectivement déployée, vérification post-déploiement, site et date enregistrés. Fusionner du code ne suffit pas.

## Sprints
SP-001, SP-002... sont des champs/labels, pas les listes du workflow. La planification suit ESTIMATIONS et la capacité réelle.
Une macro-story reste dans À affiner avec liens vers ses sous-cartes ; ne pas la dupliquer comme carte exécutable. Un sprint se ferme avec réalisé, reporté, raisons, défauts et réestimation.

## Agents IA
Un agent prend uniquement une carte Prêt explicitement assignée. Il lit AGENTS.md, les règles et contrats liés. Il ne marque pas sa propre carte Livré ni ne s’accorde une validation métier.
À chaque arrêt : indiquer résultat, fichiers modifiés, tests exécutés/non exécutés, blocage et prochaine action exacte. Ne jamais inventer une approbation ou changer le périmètre pour passer un test.
Le registre Git contient la spécification ; Trello porte état, affectation, sprint et preuves. Si un choix change dans Trello, le reporter dans les documents lors de la même livraison.

## Publication
Le fichier trello-backlog.json est un échange neutre, **pas un format d’import natif Trello**. Il est publié par `python3 scripts/trello/publish_backlog.py` (simulation par défaut, `--apply` pour écrire), qui enregistre les identifiants Trello dans le JSON. Une relance met à jour titre, description et labels des cartes existantes ; elle ne déplace aucune carte, car Trello fait foi pour la liste, les membres et les checklists. Avant publication : vérifier les droits, le tableau et les membres, puis associer identifiant métier ↔ identifiant Trello. Une relance de publication doit mettre à jour la carte existante, pas créer un doublon.
Ne publier aucune donnée personnelle des futurs déclarants dans les cartes de développement.

## Accès API
Application Trello « gestionSite sync » (sans fonctionnalités Power-Up), authentification par clé d’API et jeton. Les identifiants sont dans le Trousseau macOS du poste du porteur, services `gestionsite-trello-key` et `gestionsite-trello-token` ; jamais dans le dépôt. Lecture : `security find-generic-password -s <service> -w`. Accès vérifié et première publication effectuée le 27 octobre 2026.


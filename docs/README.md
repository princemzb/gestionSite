# Dossier de référence du projet
Version 0.2 — 27 octobre 2026. Responsable de consolidation : assistant en rôles BA, architecte et tech lead ; validation métier : responsable logistique ; validation technique finale : tech lead de réalisation.

## Lire le dossier
- [PRD](PRD.md) : besoin et décisions prises.
- [Vision et gouvernance](produit/VISION.md) : objectifs, responsabilités et changements.
- [Glossaire et données métier](metier/GLOSSAIRE-DONNEES.md).
- [Règles métier](metier/REGLES.md), [permissions](metier/PERMISSIONS.md), [processus](metier/PROCESSUS.md).
- [APPFLOW et écrans annotés](ux/APPFLOW.md).
- [Architecture métier et logicielle](architecture/ARCHITECTURE.md).
- [TRD](architecture/TRD.md), [données](architecture/DONNEES.md), [sécurité](architecture/SECURITE.md).
- [Décisions d’architecture](architecture/ADR.md), [études de faisabilité](architecture/FAISABILITE.md).
- [Contrats API et intégrations](dev/CONTRATS.md), [hors connexion](dev/SYNCHRONISATION.md).
- [Guide développeur et conventions](dev/GUIDE.md), [fiches d’implémentation](dev/FICHES.md).
- [Backlog et traçabilité](pilotage/BACKLOG.md).
- [Organisation Trello](pilotage/TRELLO.md) et [cartes préparées](pilotage/trello-backlog.json).
- [Estimation tech lead et sprints](pilotage/ESTIMATIONS.md).
- [Risques et arbitrages](pilotage/RISQUES.md).
- [Tests et recette](qualite/RECETTE.md).
- [Exploitation](exploitation/RUNBOOK.md), [guide utilisateurs](exploitation/GUIDE-UTILISATEURS.md).
- [Suivi des versions et validations](pilotage/SUIVI.md).

## Statuts et sources de vérité
**V** : décision validée dans les échanges. **P** : proposition de conception de cette version, à faire approuver. **O** : arbitrage ouvert. Une proposition technique n’est pas une validation utilisateur.
Le PRD fait foi sur le besoin ; REGLES précise les comportements ; les contrats précisent les échanges ; le backlog suit la réalisation. En cas de contradiction, ouvrir un arbitrage, ne pas appliquer silencieusement le document le plus récent.

Les documents sont renseignés, mais aucune implémentation ni recette n’a été exécutée. Les schémas API sont préliminaires, les maquettes sont textuelles ; aucun prototype visuel ni contrat OpenAPI exécutable n’est livré à ce stade.

Le [guide des agents](../AGENTS.md) encadre la reprise. La préparation Trello contient 30 macro-stories et 9 cartes initiales ; aucune carte n’a été publiée dans un tableau externe. Les cartes de préparation sont incluses dans la charge des macro-stories.

## Maintenance
Chaque modification de règle doit mettre à jour ses tickets et tests liés. Conserver les identifiants RG, US, T et DEC ; ajouter plutôt que renuméroter. En revue de lot, vérifier permissions, erreurs, hors connexion, sécurité et documentation. Les commandes du dépôt seront renseignées quand la stack sera choisie.


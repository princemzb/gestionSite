# Backlog, critères et traçabilité
Version 0.2 — proposition tech lead. Chaque ligne est une story de niveau macro à découper avant exécution ; aucune n’est annoncée terminée.
Les identifiants Lxx indiquent des lots logiques, pas des sprints calendaires. Un lot peut occuper plusieurs sprints.
Charge en jours-personne : réalisation et tests développeur inclus ; coordination, recette indépendante et réserve sont dans ESTIMATIONS.

| ID | Story | Lot logique | Charge basse–haute | Dépendances | Règles | Tests |
| --- | --- | --- | --- | --- | --- | --- |
| US-01 | Cadrer les règles et la recette | L01 | 4–7 | Aucune | RG-01,RG-11,RG-16 | T-01,T-05 |
| US-02 | POC localisation Android et iPad | L01-L02 | 12–25 | Aucune | RG-25,RG-26 | T-12 |
| US-03 | POC analyse IA et produits | L01-L02 | 8–15 | Aucune | RG-13,RG-14 | T-10 |
| US-04 | POC tablette et synchronisation | L01-L02 | 8–13 | Aucune | RG-23,RG-24 | T-07 |
| US-05 | Socle dépôt, CI et environnements | L03 | 6–10 | US-01, US-04 | RG-01 | T-01 |
| US-06 | Organisations, comptes et habilitations | L03-L04 | 10–16 | US-05 | RG-01,RG-28,RG-29 | T-01 |
| US-07 | Inventaire individuel et par lots | L04-L05 | 10–16 | US-06 | RG-10 | T-05 |
| US-08 | Dépôt public et QR de site | L04 | 6–10 | US-06 | RG-02,RG-03 | T-02 |
| US-09 | Qualification, affectation et interventions | L05-L06 | 12–20 | US-07, US-08 | RG-04,RG-06 | T-03 |
| US-10 | Danger, attentes et relances | L06-L07 | 8–13 | US-09 | RG-05,RG-07,RG-08 | T-04 |
| US-11 | Grilles, évaluations et score | L06-L07 | 10–17 | US-07, US-01 | RG-11,RG-12 | T-05 |
| US-12 | Préventif calendaire et inspections | L07-L08 | 9–15 | US-09, US-11 | RG-09 | T-06 |
| US-13 | Tablette et profils partagés | L08-L09 | 12–20 | US-04, US-06, US-09 | RG-23 | T-07 |
| US-14 | Synchronisation et arbitrage | L09-L10 | 16–28 | US-13 | RG-24 | T-07 |
| US-15 | Stocks, consommations et transferts | L10-L11 | 12–20 | US-07, US-14 | RG-20,RG-21,RG-22 | T-08 |
| US-16 | Besoins matériels et réception | L11 | 6–10 | US-09, US-15 | RG-19 | T-08 |
| US-17 | Enveloppes et réservations atomiques | L11-L12 | 12–20 | US-09, US-01 | RG-16,RG-17 | T-09 |
| US-18 | Dépenses, décisions et PDF finance | L12-L13 | 10–16 | US-17 | RG-15,RG-18 | T-09 |
| US-19 | Analyse IA et estimation révisable | L13-L14 | 14–24 | US-03, US-09, US-18 | RG-13,RG-15,RG-32 | T-10 |
| US-20 | Recherche de produits et alternatives | L14-L15 | 10–20 | US-03, US-19 | RG-14 | T-10 |
| US-21 | Plans, import et éditeur 2D | L15-L17 | 20–35 | US-07, US-04 | RG-25 | T-11 |
| US-22 | Modèle 3D fonctionnel | L17-L18 | 12–22 | US-21 | RG-25 | T-11 |
| US-23 | Localisation et recalage hors ligne | L18-L21 | 22–45 | US-02, US-14, US-21 | RG-25 | T-12 |
| US-24 | Partage temporaire de position | L21-L22 | 7–12 | US-23, US-06 | RG-26 | T-12 |
| US-25 | Indicateurs et rapports PDF | L13-L14 | 8–14 | US-10, US-11, US-12 | RG-27 | T-13 |
| US-26 | Administration SaaS et support | L14-L15 | 8–14 | US-06 | RG-28,RG-30 | T-14 |
| US-27 | MFA et connexion fédérée | Après pilote | 10–18 | US-06 | RG-29 | T-01 |
| US-28 | Paiement récurrent | Après pilote | 8–15 | US-26 | RG-30 | T-14 |
| US-29 | Exploitation, restauration et sécurité | Transverse / L22-L23 | 12–20 | US-05 | RG-01,RG-28 | T-15 |
| US-30 | Validation terrain et extension sites | L23-L24 | 10–18 | US-12, US-14, US-18, US-20, US-22, US-24, US-25, US-26, US-29 | RG-01,RG-06,RG-11 | T-15 |

## Critères d’acceptation des stories
### US-01 — Cadrer les règles et la recette
Faire approuver DEC-01 à DEC-05 et les états ; publier les versions des règles et leurs cas de test.

Responsable à affecter : BA. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-02 — POC localisation Android et iPad
Exécuter POC-01 ; livrer mesures, limites, coûts et décision motivée.

Responsable à affecter : POC. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-03 — POC analyse IA et produits
Exécuter POC-02 sur 30 cas ; tracer sources, abstention, coût et corrections humaines.

Responsable à affecter : POC. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-04 — POC tablette et synchronisation
Exécuter POC-03 ; démontrer profils séparés, reprise et conflits sans perte.

Responsable à affecter : POC. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-05 — Socle dépôt, CI et environnements
Démarrage reproductible, données synthétiques, build et tests CI ; secrets distincts par environnement.

Responsable à affecter : Socle. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-06 — Organisations, comptes et habilitations
Créer comptes et sites, limiter toutes les lectures/écritures ; tester refus interclient et administrateur sans rôle métier.

Responsable à affecter : Identité. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-07 — Inventaire individuel et par lots
Créer zones et composants ; conserver historique ; vérifier somme des quantités par état.

Responsable à affecter : Patrimoine. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-08 — Dépôt public et QR de site
Dépôt obligatoire sans vérification ni suivi ; QR général et photo facultative ; reprises sans doublon.

Responsable à affecter : Maintenance. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-09 — Qualification, affectation et interventions
R/D affecte ; technicien clôture avec preuves ; réouverture tracée ; permissions testées.

Responsable à affecter : Maintenance. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-10 — Danger, attentes et relances
Alerte immédiate ; attente datée suspend délai ; réexamen et escalade sans doublon ; push et centre.

Responsable à affecter : Maintenance. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-11 — Grilles, évaluations et score
Évaluer sur grille versionnée ; score hiérarchique /10 avec couverture et fraîcheur ; cas vide et lots validés.

Responsable à affecter : Patrimoine. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-12 — Préventif calendaire et inspections
Occurrences ponctuelles/récurrentes ; grille terrain ; anomalie reliée à un signalement ; absence de doublons.

Responsable à affecter : Maintenance. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-13 — Tablette et profils partagés
Android et iPad installables ; profils préparés, déverrouillage personnel et expiration 24 h sans effacement.

Responsable à affecter : Terrain. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-14 — Synchronisation et arbitrage
Reprises idempotentes, photos interrompues, conflits présentés à R/D et révocation testée.

Responsable à affecter : Terrain. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-15 — Stocks, consommations et transferts
Départ/transit/réception ; consommations offline ; écart durable, régularisation et seuils.

Responsable à affecter : Stock. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-16 — Besoins matériels et réception
Suivre achat externe ou fourniture en stock jusqu’à remise/installation ; clôture prouvée.

Responsable à affecter : Stock. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-17 — Enveloppes et réservations atomiques
Réservation concurrence sûre ; exception limitée au dossier ; dépassement refusé même urgent.

Responsable à affecter : Budget. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-18 — Dépenses, décisions et PDF finance
Justificatifs requis, total réel ajusté, PDF versionné et envoi confirmé/tracé avec reprise.

Responsable à affecter : Budget. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-19 — Analyse IA et estimation révisable
Lancement habilité ; compléments si insuffisant ; main-d’œuvre séparée ; validation humaine et révisions.

Responsable à affecter : IA. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-20 — Recherche de produits et alternatives
Sources datées, compatibilité, France/euros, alternative principale et deux options max ; panne gérée.

Responsable à affecter : IA. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-21 — Plans, import et éditeur 2D
Créer/importer un plan, calibrer dimensions, éditer niveaux/pièces/ouvertures ; déclaration par zone.

Responsable à affecter : Spatial. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-22 — Modèle 3D fonctionnel
Afficher géométrie cohérente aux cotes et niveaux ; repères ; naviguer et sélectionner une pièce.

Responsable à affecter : Spatial. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-23 — Localisation et recalage hors ligne
Sur deux plateformes, position et étage hors Internet selon seuils validés ; recalage et repli manuel.

Responsable à affecter : Spatial. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-24 — Partage temporaire de position
Activation/suspension/arrêt, équipe habilitée seulement, position périmée visible ; aucun trajet.

Responsable à affecter : Spatial. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-25 — Indicateurs et rapports PDF
Indicateurs calculés sur dates et attentes ; filtres selon droits ; PDF cohérent, sans diffusion automatique.

Responsable à affecter : Pilotage. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-26 — Administration SaaS et support
Demande/activation, abonnement manuel, autorisation support temporaire et révocable, actions tracées.

Responsable à affecter : SaaS. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-27 — MFA et connexion fédérée
MFA selon rôles convenus et connexions Microsoft/Google après choix fournisseur ; récupération testée.

Responsable à affecter : Identité. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-28 — Paiement récurrent
Souscription, événements rejouables et rapprochement d’état ; politique impayés approuvée avant code.

Responsable à affecter : SaaS. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-29 — Exploitation, restauration et sécurité
Instrumentation, restauration isolée, contrôle fichiers et secrets ; revue sécurité avant pilote.

Responsable à affecter : Exploitation. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.

### US-30 — Validation terrain et extension sites
Recette documentée premier site, traitement anomalies, décision métier/technique puis extension.

Responsable à affecter : Livraison. Statut initial : à affiner ; exécution conditionnée par les dépendances et arbitrages associés.


## Couverture
Les exigences RG-01 à RG-30 sont reliées ci-dessus. RG-31 (audit) est transversale à US-06/09/15/17/26 ; RG-32 est dans US-19. Les tests décrivent des suites, pas encore des résultats.
Ajouter les liens de cartes Trello, PR et résultats de recette dans le registre SUIVI, sans remplacer les identifiants stables.


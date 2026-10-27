# PRD — SaaS de gestion du patrimoine et de la maintenance des églises

Version : 0.2 — Consolidation du cadrage au 27 octobre 2026.

Statut : document de travail. Les décisions métier ci-dessous proviennent des échanges de cadrage. Les propositions de réalisation, questions ouvertes et exigences soumises à faisabilité sont distinguées. Ce document ne constitue pas encore une spécification prête à implémenter intégralement.

## 1. Vision et objectifs

Permettre à une église ou à un réseau d’églises de connaître l’état de ses bâtiments et équipements, centraliser les incidents et besoins matériels, organiser les interventions et l’entretien préventif, et suivre les décisions et dépenses associées.

La cible comprend les plans 2D, une représentation 3D fonctionnelle, la localisation intérieure et une assistance IA pour analyser les constats et préparer des estimations. Toutes ces capacités restent dans la trajectoire du produit : leur livraison sera progressive, avec des tests approfondis. Leur présence dans la cible ne vaut pas démonstration de faisabilité technique.

**Objectif prioritaire du pilote :** chaque incident ou besoin reçu est enregistré, attribué et suivi jusqu’à sa clôture. Les demandes reçues hors outil devront aussi pouvoir être saisies par une personne habilitée.

Objectifs complémentaires : fiabiliser la connaissance du patrimoine, mesurer son état, suivre les délais, anticiper les dégradations et rendre les dépenses traçables.

## 2. Contexte et périmètre

- Pilote : une organisation, 2 à 3 sites géographiques, jusqu’à 20 utilisateurs avec compte.
- Bâtiments à plusieurs niveaux ; aucun plan actuellement exploitable.
- Pratiques actuelles : messages et échanges oraux, sans registre partagé fiable.
- Des interlocuteurs terrain et des bâtiments sont identifiés ; leur disponibilité précise reste à organiser.
- Réalisation : un tech lead et 1 à 2 développeurs, assistés par des agents IA.
- La qualité conditionne la mise en service ; aucune date impérative n’est fixée.
- Dépenses minimales souhaitées en développement et préproduction ; aucun plafond chiffré ni budget de production validé.
- Aucune technologie ni aucun hébergeur imposé.

Les demandes logistiques couvrent les besoins matériels, achats, remplacements et mises à disposition. La réservation, les prêts avec retours et l’organisation d’événements ne font pas partie du périmètre retenu. Les transferts entre sites et les stocks font en revanche partie de la cible.

## 3. Organisations, utilisateurs et droits

Une organisation cliente représente une église ou un réseau et comprend plusieurs sites. Les données des organisations sont séparées. Les droits internes sont attribués par site ; certaines personnes habilitées disposent d’une vue transversale.

| Acteur | Capacités métier retenues |
| --- | --- |
| Responsable logistique et délégués | Qualifier, prioriser, affecter, superviser, traiter les conflits, valider les propositions, autoriser les dépenses selon délégation, gérer les mouvements de stock réservés à ce rôle. |
| Technicien | Prendre en charge ses interventions, documenter les constats et mesures, utiliser l’IA, déclarer ses consommations, clôturer avec compte rendu. |
| Pasteur | Consulter les opérations de ses sites autorisés, signaler, demander et relancer. |
| Coordination | Consulter, signaler, demander et relancer ; pas de pilotage opérationnel des interventions. |
| Déclarant sans compte | Déposer un incident ou besoin via lien ou QR code ; pas d’espace de suivi. |
| Prestataire | Aucun compte ; un référent interne renseigne devis, interventions et comptes rendus. |
| Service finance | Hors application ; reçoit les documents et transmet sa décision, enregistrée par un responsable habilité. |
| Administrateur d’organisation | Gérer utilisateurs, habilitations et paramètres ; responsable logistique initial et suppléants désignés. |
| Administrateur de plateforme | Gérer les organisations et abonnements ; accès de support aux données métier uniquement sur autorisation temporaire et tracée. |

Un délégué opérationnel n’est pas automatiquement administrateur. La matrice exhaustive des permissions reste à établir.

## 4. Patrimoine, évaluations et score d’état

### 4.1 Inventaire

Représenter les sites, bâtiments, niveaux, pièces et zones, les éléments du bâti (murs, plafond, sol, etc.), ainsi que les équipements et matériels. Prévoir photos, caractéristiques, état, emplacement et historique.

L’inventaire combine fiches individuelles pour certains équipements et lots pour les objets similaires. Les quantités d’un lot doivent pouvoir être réparties par état. Les catégories et le niveau de granularité seront validés avec le terrain.

Les évaluations reposent sur une échelle commune d’état et des grilles propres aux catégories. Des mesures manuelles avec unité peuvent compléter les constats : humidité, dimensions, heures d’utilisation, compteurs, etc. Elles ne déclenchent pas automatiquement les interventions dans le périmètre retenu.

### 4.2 Score permanent sur 10 — décision ajoutée au cadrage

Le site présente une note sur 10 accompagnée d’un libellé d’état, avec accès au détail par pièce ou zone et par composant.

- Le score est une moyenne par niveaux : composants vers pièce ou zone, puis pièces ou zones vers site.
- Un composant ne doit pas être compté une seconde fois en ajoutant sa note à celle de sa pièce dans la moyenne du site.
- La dernière évaluation connue est conservée, même ancienne. La fraîcheur et la proportion de composants évalués sont affichées.
- Une évolution des évaluations doit se répercuter sur les scores concernés.
- En l’absence de toute évaluation, afficher « non évalué » ; ne pas fabriquer une note.
- Les alertes de danger restent visibles indépendamment de la moyenne générale.

**Interprétation de calcul proposée, à confirmer dans la spécification détaillée :** moyenne arithmétique des composants notés d’une pièce, puis moyenne arithmétique des pièces notées du site. Les composants et pièces sans évaluation sont exclus du calcul, mais restent dans les dénominateurs de couverture. Une petite pièce et une grande pièce ont donc le même poids dans la moyenne du site. Les bâtiments et étages servent au regroupement sans introduire implicitement de pondération supplémentaire.

**À préciser :** seuils et intitulés des libellés, nombre de décimales, conversion des critères en note, note des lots et rôle des quantités, éléments communs à plusieurs pièces, délai signalant une évaluation ancienne, évolution historique du score et effet des entrées/sorties d’inventaire. Aucun barème définitif n’a encore été approuvé.

## 5. Signalements et interventions

### 5.1 Dépôt

- Un QR code général par site et un lien public permettent de déposer un incident ou une demande.
- Nom, contact non vérifié, site, emplacement et description obligatoires ; photo facultative.
- Aucun compte, aucune vérification du contact, aucune notification de suivi ni espace de suivi pour le déclarant public.
- Le contact permet de demander des précisions hors application.
- Le déclarant peut indiquer un danger ou une activité bloquée ; la logistique détermine la priorité opérationnelle.
- Le formulaire public ne donne accès ni aux plans internes ni à l’inventaire.
- Les utilisateurs avec compte peuvent aussi déclarer depuis les plans selon leurs droits.

### 5.2 Traitement

Le responsable ou un délégué qualifie, priorise et affecte le dossier. L’équipe interne intervient, ou un référent suit un prestataire extérieur. Le technicien affecté peut clôturer sans validation préalable systématique du responsable.

La clôture exige les actions réalisées et l’état final. Une photo après intervention est obligatoire pour les catégories configurées. Le responsable conserve la possibilité de rouvrir le dossier.

En cas d’attente de financement, livraison ou prestataire, suspendre le délai de résolution avec motif et date de réexamen. Conserver l’ancienneté totale et relancer à la date prévue. Les motifs précis et transitions de statuts restent à spécifier.

En cas de danger : alerte immédiate aux responsables du site avant qualification définitive, consigne de contact direct au déclarant, suivi de la mise en sécurité distinct de la réparation. Contacts et consignes seront configurés avec l’organisation.

### 5.3 Notifications et indicateurs

Centre de notifications et push sur les tablettes. Rappels automatiques selon la priorité, puis escalade au responsable ou aux délégués. Relances manuelles conservées.

Priorité des tableaux de bord : demandes en attente, ancienneté, délais de prise en charge et résolution, retards, récurrence. Distinguer temps total, temps d’attente et temps hors attente : ce dernier ne représente pas nécessairement des heures de travail effectif.

Les durées cibles par priorité, horaires de calcul et règles de récurrence restent à définir.

## 6. Préventif et rapports

- Inspections et entretiens récurrents selon calendrier, et contrôles ponctuels programmés.
- Grilles adaptées aux catégories, relevés manuels, photos et historique.
- Un résultat non conforme crée automatiquement un signalement lié au contrôle, à qualifier par la logistique. Un danger déclenche immédiatement l’alerte.
- Rapports générés à la demande, filtrés par sites autorisés et période, consultables et exportables en PDF.
- Pas de génération ni diffusion périodique automatique retenue.

La gestion des anomalies déjà ouvertes et la prévention des doublons de signalement restent à spécifier.

## 7. Demandes de matériel, stocks et transferts

Suivre un besoin approuvé jusqu’à la remise ou à l’installation. L’achat s’effectue hors application ; sa progression, la réception et la satisfaction du besoin sont enregistrées. La création de bons de commande et le rapprochement comptable ne sont pas retenus à ce stade.

Stocks de consommables et pièces de rechange : entrées, sorties liées aux interventions, quantités disponibles par site, seuils d’alerte.

- Responsable et délégués : réceptions, ajustements après comptage et mises au rebut.
- Techniciens : consommations liées à leurs interventions.
- Transferts entre sites : confirmation du départ, état « en transit », confirmation de réception.
- Si les consommations hors connexion dépassent le stock connu : conserver les déclarations, signaler l’écart, vérifier physiquement et faire régulariser par un responsable. Ne pas effacer les consommations pour masquer l’écart.

## 8. IA et recherche de produits

Analyse lancée à la demande d’un technicien, responsable ou délégué après examen du signalement. Exploiter les photos et la description pour préparer une solution, les travaux et fournitures, quantités, prix sourcés et une estimation distincte de la main-d’œuvre.

En cas d’informations insuffisantes : demander des précisions ou une inspection, conserver une proposition partielle sans total tant que les éléments indispensables manquent.

Recherche sur le marché français, en euros, avec livraison ou retrait en France. Fournisseurs ouverts : les enseignes citées dans le besoin sont des exemples et non une liste obligatoire. Proposer une solution principale et jusqu’à deux alternatives, comparées selon compatibilité, prix et disponibilité.

Le technicien peut corriger la proposition. Le responsable ou un délégué valide la solution et le chiffrage avant transmission éventuelle à la finance. Séparer prix trouvés, estimations et informations à confirmer.

Le PDF produit est une estimation interne validée, distincte d’un devis commercial émis par un fournisseur ou prestataire. Aucun achat automatique n’est prévu.

L’accès réel aux catalogues, la fiabilité de la disponibilité et des prix, le calcul de la main-d’œuvre, les données transmises aux services IA et les coûts d’usage nécessitent une étude dédiée.

## 9. Budget et circuit financier

- Enveloppe par site et par période ; pas de plafond par dossier.
- Autorisation de dépense par le responsable ou un délégué dans la limite du disponible.
- Réservation du montant dès l’autorisation, avant achat ; ajustement au montant réel.
- Tout dépassement exige un accord financier préalable, y compris en cas de danger. Les actions sans dépense peuvent commencer.
- Un accord exceptionnel est limité à un dossier et à un montant, sans augmenter le disponible des autres dossiers.
- Les décisions de finance prises hors application sont enregistrées avec auteur, date et justificatif obligatoires.
- Envoi du PDF depuis l’application par un utilisateur habilité après vérification des destinataires ; envoi tracé et téléchargement également possible.
- Dépense réelle : montant, date, fournisseur, justificatif et rattachement au dossier obligatoires.

À préciser : base TTC/HT, définition des périodes, attribution des enveloppes, clôture de période, annulation et libération d’engagements, dépassement du montant déjà autorisé, responsabilités de saisie et correction des dépenses.

## 10. Plans, 3D et localisation

Prévoir import de plans et éditeur intégré pour créer ou modifier murs, ouvertures, pièces et dimensions. Les sites pilotes nécessitent des relevés initiaux. La 3D cible est fonctionnelle, aux dimensions relevées, avec étages et équipements repères ; aucun photoréalisme demandé.

Résultat attendu : voir sa position évoluer sur le plan et reconnaître la pièce et l’étage. La localisation personnelle automatique doit aussi fonctionner sans Internet. Ces exigences nécessitent une preuve de faisabilité sur Android et iPad dans les bâtiments pilotes.

Des équipements supplémentaires sont acceptables après comparaison de la précision, du coût et de l’entretien. Une modélisation exacte ne suffit pas à déterminer la position réelle d’un appareil.

Si la position est incertaine : proposer un recalage sur un point connu ; si celui-ci échoue, permettre la sélection manuelle de la pièce. La méthode de recalage reste à définir sans présupposer des QR codes par pièce.

Le technicien active explicitement le partage de sa position pour une intervention, voit un indicateur et peut le suspendre. Le partage s’arrête à la fin de l’intervention. Responsables et techniciens habilités du site peuvent voir cette position lorsque la connexion le permet. Une ancienne position doit être distinguée d’une position en direct.

Aucun trajet conservé après intervention. Les lieux des constats et les comptes rendus restent dans l’historique métier. La durée de validité d’une dernière position et son effacement à l’arrêt du partage restent à formaliser.

## 11. Applications, comptes et fonctionnement hors connexion

- Application installée sur tablettes Android et iPad pour le terrain ; interface web sur PC pour l’administration.
- Téléphone confirmé pour le formulaire public ; usage métier sur téléphone encore à décider.
- Parc mixte de tablettes personnelles et partagées ; compte individuel par utilisateur.
- Connexion initiale par email et mot de passe. Microsoft/Google dans un lot ultérieur.
- Seconde vérification non imposée au lancement ; prévue ensuite comme obligatoire pour administrateurs, responsables et délégués autorisant des dépenses, facultative pour les autres.
- Consultation et saisie hors connexion pendant 24 heures. À expiration, conserver les saisies, mais exiger une reconnexion pour reprendre l’accès.
- Changement hors connexion possible entre utilisateurs déjà authentifiés et préparés sur la tablette, avec déverrouillage personnel et attribution correcte des actions.
- Préparer les interventions affectées et les plans, équipements et grilles nécessaires ; téléchargement complémentaire à la demande.
- IA et recherche de produits attendent le retour de la connexion.
- Synchronisation : conserver les ajouts compatibles, notamment photos et commentaires ; soumettre les modifications incompatibles au responsable ou à un délégué, sans écrasement silencieux.

À spécifier : séparation locale des données entre comptes, révocation d’accès, perte d’appareil, expiration des habilitations hors connexion, changements de compte avec saisies en attente et sécurité du stockage local.

## 12. Exploitation du SaaS

Ouverture d’une organisation sur demande puis activation par la plateforme. Abonnement par organisation. Facturation manuelle initiale, avec état de l’abonnement enregistré ; paiement automatisé prévu ultérieurement.

Support métier uniquement sur autorisation temporaire de l’organisation, avec traçabilité des actions. Durée, périmètre, révocation et droit de modification du support restent à définir.

À cadrer : tarification, quotas, fin d’abonnement, export et suppression des données, hébergement, sauvegardes, disponibilité, support et reprise après incident.

## 13. Livraison et validation

Le responsable logistique prononce la validation métier après essais par des techniciens et représentants des rôles concernés. Le tech lead porte la validation technique.

Chaque lot passe par la préproduction, puis un premier site, puis les autres sites après validation terrain. Les tests sont une condition de livraison ; aucun calendrier de sprints n’est encore engagé.

**Ordre de travail proposé, non encore validé :**

1. Consolider règles métier, modèle de données, permissions et critères d’acceptation ; expérimenter tôt localisation et recherche de produits.
2. Livrer organisations, sites, comptes, patrimoine minimal, signalements, affectations et clôture.
3. Étendre évaluations et scores, préventif, notifications, indicateurs et rapports.
4. Livrer application terrain et hors connexion, stocks, demandes matérielles et circuit budgétaire selon dépendances.
5. Intégrer IA et estimations, plans 2D/3D et localisation selon les résultats des expérimentations.
6. Préparer l’ouverture commerciale et les capacités différées d’authentification et de paiement.

Ce séquencement est indicatif : il ne remplace ni l’estimation de l’équipe ni le backlog en sprints.

## 14. Scénarios d’acceptation à détailler

- Une personne sans compte dépose un incident avec contact non vérifié ; le dossier arrive au bon site et aucun accès de suivi ne lui est ouvert.
- Les permissions empêchent l’accès à une autre organisation ou à un site non autorisé, y compris pour les documents et exports.
- Un technicien clôture avec les éléments exigés ; une absence de photo bloque uniquement les catégories qui l’imposent.
- Un danger déclenche une alerte et permet de suivre séparément la mise en sécurité.
- Une attente suspend le délai concerné, conserve l’ancienneté totale et déclenche la relance à la date de réexamen.
- Une anomalie d’inspection génère un signalement relié au contrôle.
- Le score respecte la hiérarchie, ne double-compte pas les composants, conserve les dernières notes et indique les lacunes et leur ancienneté.
- Une tablette partagée conserve les auteurs des saisies hors connexion ; les conflits arrivent à l’arbitrage sans perte de données.
- Des consommations concurrentes incohérentes déclenchent une régularisation sans disparition des déclarations.
- Deux autorisations concurrentes ne peuvent pas réserver abusivement le même solde ; une exception finance reste limitée à son dossier.
- L’IA s’abstient de total en cas d’informations insuffisantes ; une proposition corrigée doit être validée avant transmission.
- Un partage de position est explicite, limité aux personnes habilitées et cesse sans conserver de trajet.
- Une localisation incertaine permet le recalage puis la sélection manuelle ; la faisabilité hors connexion est mesurée sur les appareils cibles.

## 15. Points restant à arbitrer et livrables suivants

### Décisions produit

Barèmes d’état et scoring détaillé ; statuts et transitions ; doublons et rejets ; délégations et budgets détaillés ; délais par priorité ; catégories et grilles initiales ; usages mobiles des rôles non techniques ; langues ; critères chiffrés de réussite du pilote.

### Études et exigences transversales

Protection et conservation des identités, photos et documents ; conditions de géolocalisation ; données envoyées à l’IA ; hébergement et exigences applicables ; restauration et sécurité ; objectifs de performance et disponibilité ; coût dev/préproduction/production ; faisabilité de localisation et d’accès aux données marchandes.

Ces points ne sont pas réputés validés. Les choix techniques et les conclusions réglementaires devront s’appuyer sur des recherches vérifiées, des échanges avec les parties responsables et des essais adaptés.

### Dossier cible

Après validation du PRD : glossaire et modèle métier, matrice des permissions, APPFLOW, maquettes, TRD, architecture métier et logicielle avec décisions motivées, contrats d’interface, modèle de données, stratégie de tests, backlog estimé et regroupé en sprints, procédures de déploiement et exploitation.

Une première version des documents BA, architecture, développement, pilotage et tests est disponible dans [l’index documentaire](README.md). Elle comprend un backlog tracé, une estimation initiale du tech lead et une préparation de tableau Trello. Les propositions techniques ne sont pas encore adoptées ; les maquettes sont textuelles, les contrats préliminaires et aucune implémentation ni recette applicative n’a été réalisée.

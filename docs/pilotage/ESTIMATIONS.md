# Estimation tech lead et plan de sprints
Date : 27 octobre 2026. Estimation initiale, non engagement de délai. Aucun historique de vélocité et aucun POC exécuté.

## Méthode
Estimation par macro-story dans BACKLOG ; 1 jour-personne = 7 heures de travail. Fourchette issue de la complexité, de deux plateformes mobiles, du hors connexion et des intégrations non éprouvées. Hypothèse : équipe compétente, décision produit disponible sous 2 jours ouvrés et accès au terrain.
Les temps de génération par IA ne constituent pas la charge entière : revue, intégration, tests, sécurité et corrections restent nécessaires. Aucun gain automatique arbitraire n’est retranché.

| Poste | Bas | Haut |
| --- | ---: | ---: |
| Réalisation, études et tests développeur (30 stories) | 312 j-p | 548 j-p |
| Architecture détaillée, revues et coordination tech lead, +20 % | 62.4 | 109.6 |
| Recette indépendante, régressions et accompagnement, +15 % | 46.8 | 82.2 |
| Réserve d’incertitude, +25 % du bas / +40 % du haut | 78.0 | 219.2 |
| **Total arrondi de la cible complète** | **500 j-p** | **959 j-p** |

Les compléments sont calculés sur la base, pas composés entre eux. US-29 couvre la construction des moyens d’exploitation et US-30 l’assistance technique au déploiement ; les 15 % couvrent la vérification indépendante, pas une seconde implémentation. L’effort de validation du responsable métier est à organiser séparément dans son agenda.

## Délai calendaire indicatif
Hypothèse : tech lead disponible 0,5 ETP en plus des développeurs, 20 jours ouvrés/mois, 75 % de capacité planifiable. Les tâches de recette peuvent être assurées par l’équipe avec revue croisée, sans supposer un QA à temps plein gratuit.
- 1 développeur + tech lead à mi-temps : 22,5 j-p/mois → **23 à 43 mois**.
- 2 développeurs + tech lead à mi-temps : 37,5 j-p/mois → **14 à 26 mois**.
Ces quotients sont des ordres de grandeur de capacité, pas un ordonnancement optimisé. Achats, accès bâtiments, validations externes et complexité découverte peuvent prolonger le calendrier. Les premières mises en service précèdent largement l’achèvement de la cible.

## Premier incrément utilisable
US-01/05/06/07/08/09/10/11/12 : centralisation, patrimoine, scoring et préventif, avec une part adaptée de US-29/30. La version web peut servir à valider les processus, sans la présenter comme la livraison de l’application terrain complète.
Ne pas chiffrer ce premier incrément en additionnant seulement les écrans : préciser stack, barèmes, droits et critères de recette avant engagement. Les trois POC sont lancés tôt pour éviter une impasse tardive.

## Sprints proposés
Durée candidate : 2 semaines. Capacité par sprint, avec facteur de planification déjà inclus : 11,25 j-p pour 1 développeur + TL 0,5 ; 18,75 j-p pour 2 développeurs + TL 0,5.
La cible représente environ **27–52 sprints** avec deux développeurs, ou **45–86** avec un seul. Ne pas réduire artificiellement cette charge à 24 sprints en confondant lots et itérations.

| Sprint initial | Objectif proposé | Budget si 2 dev + TL 0,5 |
| --- | --- | --- |
| SP-001 | Arbitrages prioritaires, protocole localisation, sélection des cas IA et des appareils | 15 j-p prévus + 3,75 de marge |
| SP-002 | Démonstrateur hors connexion et premiers essais terrain/IA | 15 j-p prévus + 3,75 de marge |
| SP-003 | Clore les preuves réalisables, revue de stack et réestimation ; poursuivre les POC restants si nécessaire | 15 j-p prévus + 3,75 de marge |
| SP-004 et suivants | Tirer les stories dépendantes validées selon capacité et chemin critique | Affectation après bilan SP-003 |

Les 45 j-p prévus ne garantissent pas la clôture des études dont la borne haute dépasse ce budget. Les 3 premiers sprints sont des allocations proposées, pas une date de disponibilité. Avec un seul développeur, étaler ces travaux au lieu de conserver le même contenu.

Le fichier Trello détaille 35 j-p de préparation : 15 dans SP-001, 15 dans SP-002 et 5 dans SP-003. Les 10 j-p restants proposés pour SP-003 sont une allocation de poursuite des POC, à transformer en cartes selon les résultats de SP-002 ; ils ne constituent pas du travail déjà spécifié. Les marges de 3,75 j-p par sprint sont distinctes de cette allocation.

## Règle d’estimation continue
Découper chaque macro-story en cartes de 1–5 j-p avant entrée en sprint. Réestimer après les POC, puis après deux sprints de réalisation. Comparer charge restante et capacité réelle, sans assimiler une carte terminée à une autre. Revoir chaque mois les réserves restantes.

## Coûts
Budget développement = jours-personne par profil × taux journalier de ce profil ; les taux ne sont pas fournis, aucun montant commercial n’est inventé. Hébergement, services IA, magasins d’applications éventuels, appareils, équipements de localisation et relevés bâtiment sont exclus et à chiffrer après études.


# Stratégie de tests et cahier de recette
Statut : plan proposé ; toutes les suites sont NON EXÉCUTÉES.

| Suite | Scénarios et résultats attendus |
| --- | --- |
| T-01 | Deux organisations, sites limités, rôles cumulés : aucun accès croisé dans API, fichiers, exports, caches ; MFA différé testé lorsqu’introduit. |
| T-02 | Dépôt sans compte/contact non vérifié/photo optionnelle ; champs obligatoires ; rejeu unique ; aucune consultation publique. |
| T-03 | Qualification par R/D ; clôture par affecté avec preuves ; refus acteur non habilité ; réouverture justifiée. |
| T-04 | Danger avant qualification ; attente/reprise ; calcul sans double pause ; rappel et escalade à heure contrôlée. |
| T-05 | Hiérarchie du score, null versus zéro, anciennes notes, couverture, grilles versionnées et lots selon barème adopté. |
| T-06 | Récurrence/ponctuel ; changement de calendrier ; non-conformité et danger ; exécution/rejeu sans duplication. |
| T-07 | Offline 24 h, deux profils, deux appareils, panne avant/après commit, médias interrompus, révocation, conflit et arbitrage. |
| T-08 | Stock et transit sans double disponibilité ; autorisations de mouvement ; surconsommation offline conservée ; remise finale. |
| T-09 | Réservations concurrentes, exception par dossier, dépassement urgent refusé, dépenses justifiées, révisions PDF et email. |
| T-10 | IA incomplète sans total, variantes incompatibles, sources périmées, contenu hostile, indisponibilité fournisseur et coûts suivis. |
| T-11 | Import calibré, édition de dimensions/niveaux, cohérence 2D/3D, version du plan et référence de pièce. |
| T-12 | Position sur deux plateformes, offline, étage, recalage/repli, activation/arrêt, droits équipe, aucune trace de trajet persistée. |
| T-13 | Dates/attentes exactes, filtres des sites, export PDF conforme aux données et droits. |
| T-14 | Activation client, abonnement manuel, support temporaire/révoqué ; paiement futur avec événements rejoués. |
| T-15 | Déploiement et retour arrière, restauration isolée, smoke tests, recette pilote et validation avant extension. |

## Niveaux
Unitaires pour calculs et transitions ; intégration réelle pour transactions/permissions ; contrats pour clients et adaptateurs ; bout en bout pour parcours ; tests physiques pour localisation et tablette ; recette humaine pour utilité métier.
Utiliser horloge contrôlée pour délais, fixtures synthétiques et doubles fournisseurs pour tests courants. Quelques essais réels bornés en préproduction restent nécessaires.

## Porte de sortie proposée
Aucun défaut critique ouvert, critères de la carte satisfaits, tests pertinents réussis, revue indépendante, documentation cohérente. Un défaut critique inclut fuite interclient, perte de données ou autorisation financière incorrecte.
Les risques restants sont explicitement acceptés par les responsables compétents ; l’agent ne se les accepte pas lui-même.

## Procès-verbal de lot
Version/commit, site, environnement, scénarios, exécutant/date, résultats, preuves, anomalies, décisions métier/technique, autorisation de déploiement et vérification après livraison. Aucun résultat fictif prérempli.


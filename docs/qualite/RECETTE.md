# Stratégie de tests et cahier de recette
Statut : plan proposé ; toutes les suites sont NON EXÉCUTÉES.

| Suite | Scénarios et résultats attendus |
| --- | --- |
| T-01 | Deux organisations, sites limités, rôles cumulés : aucun accès croisé dans API, fichiers, exports, caches ; MFA différé testé lorsqu’introduit. |
| T-02 | Dépôt sans compte/contact non vérifié/photo optionnelle ; champs obligatoires ; rejeu unique ; aucune consultation publique. |
| T-03 | Qualification par R/D ; clôture par affecté avec preuves ; refus acteur non habilité ; réouverture justifiée. |
| T-04 | Danger avant qualification ; attente/reprise ; calcul sans double pause ; rappel et escalade à heure contrôlée. |
| T-05 | Hiérarchie du score, null versus zéro, anciennes notes, couverture, grilles versionnées et lots selon barème adopté. Cas détaillés T-05.01 à T-05.11 ci-dessous. |
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


## Cas de test des règles tranchées le 27 octobre 2026
Statut : spécifiés, **NON EXÉCUTÉS**. Ils traduisent DEC-01 à DEC-05 (REGLES, PERMISSIONS, PROCESSUS) et serviront de base aux tests automatisés des stories concernées. Données synthétiques ; R/D = responsable ou délégué, T = technicien, P/C = pasteur ou coordination. Les délais utilisent une horloge contrôlée.

### T-05 — Score d’état (DEC-01)
| ID | Étant donné | Quand | Alors |
| --- | --- | --- | --- |
| T-05.01 | Pièce A : un composant Bon (9) et un Dégradé (5) | On calcule le score de A | 7,0 — Satisfaisant |
| T-05.02 | Lot de 40 chaises : 30 Bon, 8 Dégradé, 2 Critique | On calcule la note du lot | 7,8 ; le lot compte pour un seul composant de sa pièce |
| T-05.03 | Pièce B ne contient que le lot de T-05.02 ; bâtiment avec zone « Parties communes » contenant une toiture Mauvais (3) ; pièce C sans évaluation | On calcule le score du site | (7,0 + 7,8 + 3,0) / 3 = 5,93 → affiché « 5,9 — Dégradé » ; C exclue du calcul, couverture des zones = 3/4 |
| T-05.04 | Aucun composant évalué sur le site | On affiche le site | « Non évalué », aucune note ni libellé ; jamais 0 |
| T-05.05 | Score exact 7,96 | On affiche | « 8,0 — Bon » : libellé calculé sur la valeur arrondie ; le calcul interne conserve 7,96 |
| T-05.06 | Score exact 7,94 | On affiche | « 7,9 — Satisfaisant » |
| T-05.07 | Évaluation d’un composant datée d’il y a 6 mois et 1 jour, les autres récentes | On affiche la pièce | Évaluation marquée « ancienne », toujours comptée ; part du score sur évaluations anciennes affichée |
| T-05.08 | Composant évalué Bon, puis mis au rebut | On recalcule | Il sort immédiatement du calcul ; son historique reste consultable |
| T-05.09 | Nouveau composant ajouté à une pièce notée 7,0 | On recalcule | Note inchangée (7,0) ; couverture diminuée ; composant « non évalué » |
| T-05.10 | Fin de mois | La tâche d’instantané s’exécute deux fois (rejeu) | Un seul instantané par site/zone et par mois |
| T-05.11 | Une alerte danger ouverte dans une pièce notée 8,5 | On affiche le site | Alerte visible indépendamment du score |

### T-01 — Droits de lecture et d’édition (DEC-04, DEC-02)
| ID | Étant donné | Quand | Alors |
| --- | --- | --- | --- |
| T-01.10 | Dossier public sur le site S1 | Un T non affecté, un pasteur et une coordination de S1 l’ouvrent | Nom et contact du déclarant visibles pour les trois |
| T-01.11 | Même dossier | Un compte habilité seulement sur S2 l’ouvre par URL directe | Refus, sans révéler l’existence du dossier |
| T-01.12 | Enveloppe S1 de 10 000 € | P/C de S1 consultent le budget | Enveloppe, engagé, dépensé et disponible visibles, justificatifs compris ; aucune modification possible |
| T-01.13 | T affecté au dossier D1 avec estimation 800 € | T consulte D1 puis l’API budget de S1 | Estimation de D1 visible et modifiable ; enveloppe, solde et dépenses des autres refusés |
| T-01.14 | T a saisi une dépense sur D1, R/D une autre | T liste les dépenses de D1 | Seule sa propre dépense et son statut apparaissent |
| T-01.15 | T de S1 | T ajoute un composant puis corrige son emplacement | Accepté ; auteur, date et valeur précédente tracés |
| T-01.16 | T de S1 | T modifie une grille d’évaluation | Refus |
| T-01.17 | R/D modifie une grille | La modification est enregistrée | Nouvelle version ; les évaluations passées restent liées à l’ancienne |
| T-01.18 | T sans droit « plans », puis T2 avec droit attribué par A | Chacun modifie le plan 2D | T refusé ; T2 accepté et nouvelle version du plan créée |

### T-03 — Statuts et interventions (DEC-03)
| ID | Étant donné | Quand | Alors |
| --- | --- | --- | --- |
| T-03.10 | Dossier avec deux interventions ouvertes | T clôture la première | Dossier toujours « En cours » |
| T-03.11 | Même dossier | T clôture la seconde avec actions et état final | Dossier « Clôturée » automatiquement ; R/D notifiés |
| T-03.12 | Dossier clôturé | R/D rouvre sans motif, puis avec motif | Refus sans motif ; avec motif → « À qualifier », historique conservé |
| T-03.13 | Dossier ouvert sur S1, emplacement « Salle 2 » | Nouveau signalement même site et même emplacement, en qualification | Le dossier ouvert est suggéré comme doublon ; aucun rattachement sans confirmation R/D |
| T-03.14 | R/D confirme le rattachement | On consulte les deux dossiers | Le dossier rattaché prend le statut « Rattachée à un doublon », conserve sa déclaration et pointe vers le principal ; le principal liste la déclaration rattachée |
| T-03.15 | R/D classe sans suite avec motif « autre » sans commentaire | Validation | Refus ; avec commentaire → accepté ; ne compte pas comme réparation |
| T-03.16 | T non affecté au dossier | T classe sans suite | Refus (R/D uniquement) |
| T-03.17 | T affecté | Mise en attente sans date de réexamen, puis avec motif « livraison » et date | Refus, puis accepté |
| T-03.18 | Pasteur de S1 | Il met le dossier en attente | Refus ; la relance reste possible |
| T-03.19 | Composant avec dossier ouvert pour le critère « fuite » | Une inspection relève une non-conformité « fuite » sur ce composant | Constat rattaché au dossier existant, aucun nouveau dossier ; R/D notifiés |
| T-03.20 | Même situation avec danger signalé | Inspection enregistrée | Rattachement au dossier existant et alerte danger déclenchée |

### T-08 — Demandes matérielles (DEC-03)
| ID | Étant donné | Quand | Alors |
| --- | --- | --- | --- |
| T-08.10 | Demande de 10 chaises approuvée | Réception de 6 | « Partiellement reçue », reliquat 4 |
| T-08.11 | Même demande | Réception des 4 restantes | Réception complète |
| T-08.12 | Reliquat 4 | R/D abandonne le reliquat sans motif, puis avec motif | Refus, puis accepté ; 6 reçues conservées |
| T-08.13 | Demande approuvée avec 500 € réservés, 120 € dépensés | R/D annule avec motif | 380 € libérés ; dépense de 120 € conservée |
| T-08.14 | Demande remise complètement | R/D tente l’annulation | Refus |

### T-04 — Priorités, délais et relances (DEC-05)
| ID | Étant donné | Quand | Alors |
| --- | --- | --- | --- |
| T-04.10 | Dossier qualifié « Urgente » le lundi à 10:00, non affecté | L’horloge atteint 13:00 puis 14:00 | Rappel à R/D à 13:00 (75 % de 4 h) ; escalade aux R/D du site à 14:00 |
| T-04.11 | Même dossier toujours non affecté | Mardi 14:00 | Un rappel quotidien ; aucun doublon pour un même seuil même si la tâche est rejouée |
| T-04.12 | Dossier « Normale » qualifié le vendredi 10:00 | Calcul de l’échéance de prise en charge | Lundi 10:00 (3 jours calendaires, week-end inclus) |
| T-04.13 | Dossier « Normale », résolution 30 j, mis en attente 5 jours | Calcul de l’échéance de résolution | Échéance reculée de 5 jours ; ancienneté totale inchangée |
| T-04.14 | Dossier affecté à T | 75 % du délai de résolution atteint | Rappel envoyé à T, pas à R/D |
| T-04.15 | 3 dossiers sur le même composant en 12 mois glissants, dont un classé sans suite | Calcul de récurrence | Non récurrent (2 dossiers comptés) ; avec un 3e dossier valide → récurrent |

### T-09 — Budget (DEC-02)
| ID | Étant donné | Quand | Alors |
| --- | --- | --- | --- |
| T-09.10 | Enveloppe S1 2026 : 10 000 € TTC, rien d’engagé | Deux autorisations simultanées de 6 000 € | Une seule acceptée ; l’autre refusée (disponible 4 000 €) |
| T-09.11 | 7 000 € engagés | R/D réduit l’enveloppe à 6 500 € | Refus (inférieur à l’engagé) ; réduction à 8 000 € avec motif et justificatif acceptée |
| T-09.12 | Autorisé 500 € sur D1 | Facture de 560 € saisie | Dépense enregistrée ; D1 « dépassement à régulariser » ; 60 € exigent une autorisation complémentaire ou un accord finance |
| T-09.13 | Engagement 1 000 € sur D1 | T saisit une dépense de 120 € | Statut « à valider » ; disponible de l’engagement 880 € immédiatement |
| T-09.14 | Même dépense | R/D la refuse | Disponible rétabli à 1 000 € ; dépense conservée avec statut refusé |
| T-09.15 | Dépense validée de 300 € | R/D corrige à 320 € avec motif, puis tente de la supprimer | Correction tracée (ancienne valeur, auteur, motif) ; suppression impossible, annulation avec motif seulement |
| T-09.16 | D1 clôturé, autorisé 500 €, dépensé 420 € | Avant puis après confirmation R/D | 80 € restent réservés, puis sont libérés vers le disponible du site |
| T-09.17 | 31/12/2026 : engagement 1 000 € dont 400 € dépensés | Clôture d’année | 600 € proposés au report ; reportés sur 2027 après confirmation R/D ; 2026 non modifiable ensuite |

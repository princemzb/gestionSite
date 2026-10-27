# TRD — exigences techniques
Statut : V pour contraintes issues du PRD ; P pour objectifs techniques proposés.

| ID | Exigence | Vérification |
| --- | --- | --- |
| NFR-01 V | Isolation organisation/site sur tous les accès | Tests négatifs API, fichiers, exports, cache local |
| NFR-02 V | Hors connexion 24 h sur Android/iPad ; profils préparés | Essais deux appareils et changement de profil |
| NFR-03 P | Rejeu sans double effet des commandes terrain | Coupure après commit avant accusé, puis renvoi |
| NFR-04 P | Réservations budgétaires atomiques | Deux autorisations simultanées sur même solde |
| NFR-05 P | Fichiers privés, contrôle à chaque accès | Accès direct sans session et après révocation refusé |
| NFR-06 P | Réponse API p95 < 1 s hors upload/IA/PDF | Jeu synthétique 10 000 composants, 20 sessions ; volume à confirmer |
| NFR-07 P | Ouverture d’un dossier local < 2 s | Appareils de référence à choisir |
| NFR-08 P | Disponibilité mensuelle cible 99,5 % | Mesure externe ; coût à valider avant engagement |
| NFR-09 P | RPO 24 h, RTO 8 h pour pilote | Restauration isolée chronométrée ; objectifs à valider |
| NFR-10 V | Pas de trajet conservé | Inspection stockage, logs, analytique et sauvegardes |
| NFR-11 P | Journaux techniques sans photos, contacts ni coordonnées de position | Revue automatique et inspection d’échantillons |
| NFR-12 P | Accessibilité : clavier, labels, contraste, erreurs textuelles | Recette web/tablette ; niveau formel à arrêter |

RPO = perte maximale de données visée après sinistre ; RTO = délai de rétablissement visé. Ce sont des objectifs proposés, pas des garanties commerciales.

## Préconditions techniques
Choisir stack, versions supportées et appareils de référence avant sprint de développement. Fixer tailles/nombres de photos, quotas, limites de concurrence et fenêtres de maintenance. Aucun chiffre non validé ne doit devenir une promesse contractuelle.


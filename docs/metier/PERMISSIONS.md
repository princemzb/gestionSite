# Matrice de permissions
Statut : formalisation P des rôles V. R = responsable, D = délégué, T = technicien, P/C = pasteur/coordination, A = administrateur d’organisation.

Toute permission est limitée à l’organisation et aux sites autorisés. Les rôles peuvent se cumuler. Les permissions de lecture des coûts et contacts restent à valider : aucun accès large implicite.

| Action | Public | T | R/D | P/C | A seul |
| --- | --- | --- | --- | --- | --- |
| Déposer incident/besoin | Oui | Oui | Oui | Oui | Selon rôle métier |
| Consulter opérations autorisées | Non | Oui | Oui | Oui | Non implicite |
| Qualifier, affecter, prioriser | Non | Non | Oui | Non | Non |
| Documenter intervention affectée | Non | Oui | Oui | Non | Non |
| Clôturer intervention affectée | Non | Oui | Oui | Non | Non |
| Rouvrir / arbitrer conflit | Non | Non | Oui | Non | Non |
| Relancer | Non | P : interne | Oui | Oui | Non |
| Lancer IA | Non | Oui | Oui | Non | Non |
| Valider estimation / engager budget | Non | Non | Oui selon délégation | Non | Non |
| Enregistrer accord finance | Non | Non | Oui | Non | Non |
| Consommer stock en intervention | Non | Oui | Oui | Non | Non |
| Réception / ajustement / rebut | Non | Non | Oui | Non | Non |
| Voir position partagée du site | Non | Oui | Oui | Non | Non |
| Gérer comptes et habilitations | Non | Non | Si A | Non | Oui |
| Autoriser support temporaire | Non | Non | Si A | Non | Oui |

## Compléments proposés
Seuls les acteurs autorisés voient contacts publics et justificatifs ; proposition : R/D et technicien affecté pour le contact, R/D pour justificatifs financiers. Lecture des indicateurs financiers par pasteurs à arbitrer.
Gestion des grilles, inventaires et plans : proposer R/D en écriture, techniciens en évaluation ; administration des paramètres sensibles limitée à A et compétences explicitement attribuées.

## Tests transversaux
Tester chaque permission côté serveur, sur listes, fiches, pièces jointes, exports, notifications et téléchargements hors connexion. Une URL connue n’accorde aucun droit. Le support doit satisfaire l’autorisation temporaire en plus de son rôle plateforme.


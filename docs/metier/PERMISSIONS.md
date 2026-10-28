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
| Relancer | Non | Non | Oui | Oui (P : relance interne seulement) | Non |
| Lancer IA | Non | Oui | Oui | Non | Non |
| Valider estimation / engager budget | Non | Non | Oui selon délégation | Non | Non |
| Enregistrer accord finance | Non | Non | Oui | Non | Non |
| Consommer stock en intervention | Non | Oui | Oui | Non | Non |
| Réception / ajustement / rebut | Non | Non | Oui | Non | Non |
| Voir position partagée du site | Non | Oui | Oui | Non | Non |
| Gérer comptes et habilitations | Non | Non | Si A | Non | Oui |
| Autoriser support temporaire | Non | Non | Si A | Non | Oui |

## Décisions DEC-04 — V, 27 octobre 2026 (porteur du projet)
| Donnée ou action | Lecture | Écriture |
| --- | --- | --- |
| Nom et contact du déclarant public | Tous les comptes internes habilités sur le site du dossier (T, R/D, P/C) | Saisie par le déclarant ; correction par R/D |
| Montants : estimations, dépenses, enveloppes, soldes | R/D, P/C des sites autorisés | R/D selon délégation |
| Estimation d’un dossier affecté | Technicien affecté, pour ce dossier uniquement ; jamais enveloppes ni soldes ; dépenses limitées à ses propres saisies (DEC-02) | Technicien affecté (correction de proposition) ; validation par R/D |
| Justificatifs financiers (factures, devis, décisions finance) | R/D, P/C des sites autorisés | R/D |
| Inventaire (bâtiments, niveaux, zones, composants, lots) | Comptes internes du site | R/D ; techniciens du site (ajout et correction tracés) |
| Grilles d’évaluation | Comptes internes du site | R/D ; chaque modification crée une version |
| Plans 2D | Comptes internes du site | R/D ; droit attribuable nominativement par A ; chaque modification crée une version |
| QR code et lien public d’un site (US-08, V 28/10) | A (tous les sites) ; R/D (leurs sites) | — (dérivés du site) |
| Dépôts publics reçus, liste en lecture (US-08, V 28/10) | T, R/D, P/C du site | — (traitement : US-09) |

Conséquence : les contacts des déclarants sont visibles par un cercle plus large que la proposition initiale ; à reprendre dans l’inventaire des données (SECURITE) et DEC-07. Administration des paramètres sensibles limitée à A et aux compétences explicitement attribuées.

## Tests transversaux
Tester chaque permission côté serveur, sur listes, fiches, pièces jointes, exports, notifications et téléchargements hors connexion. Une URL connue n’accorde aucun droit. Le support doit satisfaire l’autorisation temporaire en plus de son rôle plateforme.


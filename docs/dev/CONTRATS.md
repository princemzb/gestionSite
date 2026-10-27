# Contrats API et intégrations
Statut : esquisse P ; noms et formes non figés. Produire un contrat OpenAPI validé avant le développement des clients.

## Opérations proposées
| Domaine | Opération HTTP indicative | Entrées et sortie métier |
| --- | --- | --- |
| Public | POST /public/sites/{code}/reports | Nom, contact, emplacement texte, type, description, danger, références photos → accusé sans jeton de suivi |
| Dossiers | GET /sites/{id}/cases | Filtres autorisés, pagination → dossiers visibles |
| Affectation | POST /cases/{id}/assignment | Intervenant, priorité, version attendue → dossier révisé |
| Clôture | POST /interventions/{id}/closure | Actions, état final, photos, version → clôture ou validation refusée |
| Évaluation | POST /components/{id}/evaluations | Grille/version, réponses, mesures, note → évaluation et état du recalcul |
| Budget | POST /cases/{id}/authorizations | Estimation/version, montant, accord exceptionnel éventuel → engagement |
| Stock | POST /sites/{id}/stock-movements | Article, quantité, unité, type, dossier → mouvement et éventuel écart |
| IA | POST /cases/{id}/analyses | Constat/version → tâche ; consultation privée du résultat |
| Sync | POST /sync/operations | Opérations et versions → résultats individuels |
| Position | POST /interventions/{id}/position-session | Activation explicite → session limitée ; arrêt explicite |

## Principes de contrat proposés
Identifiants stables, horodatages avec fuseau, montants avec devise et précision définie, unités explicites.
Commande sensible : clé d’idempotence et version attendue. Même clé/même contenu → même résultat ; même clé/contenu différent → erreur.
Erreurs structurées : code stable, message utilisateur, champs invalides, identifiant de corrélation sans donnée sensible.
Codes indicatifs : 401 session absente, 403 droit insuffisant, 404 objet non visible, 409 conflit, 422 validation, 429 limite. Définir une politique cohérente empêchant l’énumération interclient.
Les événements internes proposés portent identifiant, organisation, type, instant, référence et version ; éviter d’y copier photos/contacts.

## Intégrations
IA : entrée minimisée, sortie structurée avec hypothèses, éléments manquants et sources ; validation serveur avant utilisation.
Produits : source, date, référence, variante, devise, prix, disponibilité et frais connus/inconnus.
Email : destinataires confirmés, PDF validé/versionné, état en file/envoyé/échec ; « accepté par fournisseur » ne signifie pas lu.
Push : indication minimale, lien privé soumis aux droits ; centre de notifications source de vérité.
Fichiers : envoi reprenable, empreinte, métadonnées privées ; contrôle avant rattachement.

Référence de formalisation : [OpenAPI Specification](https://spec.openapis.org/oas/latest.html). La version utilisée sera figée selon l’outillage choisi.


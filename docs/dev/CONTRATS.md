# Contrats API et intégrations
Statut : esquisse P ; noms et formes non figés. Produire un contrat OpenAPI validé avant le développement des clients.

## Opérations proposées
| Domaine | Opération HTTP indicative | Entrées et sortie métier |
| --- | --- | --- |
| Public | POST /public/sites/{code}/reports | Nom, contact, emplacement texte, type, description, danger, références photos → accusé sans jeton de suivi (détail : section « Dépôt public ») |
| Dossiers | GET /sites/{id}/cases | Filtres autorisés, pagination → dossiers visibles |
| Affectation | POST /cases/{id}/assignment | Intervenant, priorité, version attendue → dossier révisé |
| Clôture | POST /interventions/{id}/closure | Actions, état final, photos, version → clôture ou validation refusée |
| Évaluation | POST /components/{id}/evaluations | Grille/version, réponses, mesures, note → évaluation et état du recalcul |
| Budget | POST /cases/{id}/authorizations | Estimation/version, montant, accord exceptionnel éventuel → engagement |
| Stock | POST /sites/{id}/stock-movements | Article, quantité, unité, type, dossier → mouvement et éventuel écart |
| IA | POST /cases/{id}/analyses | Constat/version → tâche ; consultation privée du résultat |
| Sync | POST /sync/operations | Opérations et versions → résultats individuels |
| Position | POST /interventions/{id}/position-session | Activation explicite → session limitée ; arrêt explicite |

## Identité et habilitations (US-06, ADR-10)
| Opération | Accès | Entrées → sortie |
| --- | --- | --- |
| POST /auth/login | Public | e-mail, mot de passe → jeton d'accès (15 min) et jeton de renouvellement ; 401 identique que le compte existe ou non ; 423 si verrouillé |
| POST /auth/refresh | Jeton de renouvellement | jeton → nouveau couple (rotation, l'ancien devient invalide) ; 401 si révoqué ou expiré |
| POST /auth/logout | Authentifié | → révoque le jeton de renouvellement de l'appareil |
| POST /auth/activate | Public, lien à usage unique | jeton d'activation, mot de passe → compte actif, réponse : e-mail du compte (connexion immédiate) ; 410 si expiré ou déjà utilisé |
| GET /me | Authentifié | → identité, organisations (adhésions acceptées), rôles et sites autorisés, invitations en attente |
| POST /me/invitations/{memberId}/accept | Titulaire de l'invitation | → adhésion acceptée, droits effectifs ; 404 sinon |
| POST /me/invitations/{memberId}/decline | Titulaire de l'invitation | → invitation refusée (révoquée, historique conservé) ; 404 sinon |
| GET /organizations/{orgId}/sites | Membre de l'organisation | → sites autorisés pour l'appelant uniquement |
| POST /organizations/{orgId}/sites | Administrateur d'organisation | nom → site créé |
| GET /organizations/{orgId}/members | Administrateur d'organisation | → membres non révoqués : nom, e-mail, rôles par site, invitation en attente, version |
| POST /organizations/{orgId}/members | Administrateur d'organisation | nom, e-mail, rôles, sites → invitation créée ; lien d'activation (montré une seule fois) seulement si le compte est créé par cet appel ; 409 si compte en attente créé ailleurs |
| POST /organizations/{orgId}/members/{memberId}/activation | Administrateur d'organisation | → nouveau lien, si cette organisation a créé le compte et qu'il n'est pas activé ; 409 sinon |
| PUT /organizations/{orgId}/members/{memberId} | Administrateur d'organisation | rôles, sites, version attendue, motif (obligatoire) → habilitations révisées et tracées (RG-31) ; 409 si version dépassée ; sans changement : rien n'est écrit |
| DELETE /organizations/{orgId}/members/{memberId} | Administrateur d'organisation | corps : motif (obligatoire) → accès à l'organisation coupé dès la requête suivante, jeton d'accès en cours compris ; la session du compte reste ouverte pour ses autres organisations (décision du porteur, 28/10) ; historique conservé |
| GET /organizations/{orgId}/members/journal?limite=&avant= | Administrateur d'organisation | → journal des habilitations (RG-31) : nature, auteur, date, motif, rôles avant/après, du plus récent au plus ancien ; pages de 200 au plus, `avant` = identifiant de la dernière entrée reçue |

Rôles : administrateur d'organisation (A), responsable (R), délégué (D), technicien (T), pasteur (P), coordination (C) ; cumulables ; chacun accordé sur des sites précis (A : toute l'organisation). Toute ressource d'une autre organisation répond 404, jamais 403, pour empêcher l'énumération.

## Dépôt public (US-08, S08-01)
| Opération | Accès | Contenu et réponses |
| --- | --- | --- |
| GET /public/sites/{code} | Public | → nom du site et de l'organisation (en-tête du formulaire) ; 404 si code mal formé ou inconnu |
| POST /public/sites/{code}/reports | Public | `commandeId` (UUID choisi par le formulaire), `nature` (Incident ou Demande), `description` (≤ 2000), `emplacement` (≤ 200), `danger`, `activiteBloquee`, `nom` (≤ 200), `contact` (≤ 200, non vérifié) → 200 `{site, danger, numeroUrgence}` : aucun identifiant ni jeton ; `numeroUrgence` seulement si danger et numéro du site renseigné. Même `commandeId` rejoué ou envoyé en parallèle avec le même contenu : même accusé, un seul signalement ; contenu différent : 409 `commande_reutilisee`. 422 `donnee_invalide` + `champ` (corps illisible : `champ` = `corps` ; textes : une ligne visible sans caractère de contrôle ni inversion bidirectionnelle, description multiligne) ; 404 code inconnu. Danger : alerte enregistrée avec le signalement. |

Photos (S08-02) : même opération en `multipart/form-data` — partie `demande` (le JSON ci-dessus) et jusqu'à 3 fichiers `photos` de 10 Mo (JPEG, PNG ou HEIC, reconnus par leurs octets d'en-tête) ; sinon 422 `champ` = `photos`, sans signalement créé. Chaque photo est réorientée, débarrassée de ses métadonnées (position GPS comprise), réduite à 2560 px et réencodée en JPEG, puis stockée en privé sous `organisations/{organisation}/signalements/{signalement}/photo-{rang}.jpg` (clé propre au signalement enregistré ; un rejeu ne réécrit rien). Corps trop volumineux : 422 `photos` ; envoi tronqué : 422 `corps`.

Anti-abus (S08-03) : champ `verification` = jeton Turnstile produit par le navigateur, vérifié par l'API auprès de Cloudflare (clé secrète `Turnstile:CleSecrete` ; absente : 503 `depot_indisponible`, le reste de l'application fonctionne ; `Turnstile:Desactive` refusé hors développement) ; refus → 422 `champ` = `verification`. Un rejeu (même commande, même contenu) n'est pas revérifié : le jeton ne sert qu'une fois. Au plus `DepotPublic:LimiteParSite` signalements acceptés (20 par défaut) par site et par fenêtre de 10 minutes, comptés après l'anti-robot (requêtes invalides et rejeux non comptés) : au-delà, 429 `trop_de_depots` ; la limite par adresse du visiteur est appliquée par l'application web (S08-04).

Code de site : 10 caractères de `23456789ABCDEFGHJKMNPQRSTVWXYZ` (casse et tirets ignorés), un par site, créé avec le site (sites existants : attribué par la migration `SitesPublics`) ; filtré par organisation sauf pour la recherche par code (lecture RLS ouverte, le code étant la clé), écrit seulement par l'organisation du site (garde applicative + RLS).

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


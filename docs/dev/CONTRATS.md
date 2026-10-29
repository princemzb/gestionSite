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

Inventaire — structure (US-07, S07-01) : `/organizations/{orgId}/sites/{siteId}/inventory/…`
| Opération | Accès | Contenu et réponses |
| --- | --- | --- |
| GET structure | Comptes internes du site (`ConsulterSite`) | → `[{id, nom, niveaux: [{id, nom, ordre}], zones: [{id, nom, niveauId, partiesCommunes}]}]` (bâtiments par nom, niveaux par ordre, « Parties communes » en dernier) |
| POST buildings | T, R/D du site (`ModifierInventaire`) | `nom` → `{id}` ; crée aussi la zone « Parties communes » du bâtiment |
| POST buildings/{batimentId}/levels | Idem | `nom`, `ordre` (−10 à 100, sous-sols négatifs) → `{id}` |
| POST zones | Idem | `nom`, `batimentId`, `niveauId` facultatif (du même bâtiment) → `{id}` |
| PUT buildings/{id}, levels/{id}, zones/{id} | Idem | `nom` → 204 ; « Parties communes » ne se renomme pas (422) ; sans changement, rien n'est écrit |

Erreurs : 422 `donnee_invalide` + `champ` (champ absent compris) ; 409 `nom_existant` (casse ignorée : même nom dans le site pour un bâtiment, dans le bâtiment pour un niveau, sur le même niveau pour une zone — « Sanitaires » peut exister à chaque étage) ; 409 `modifie_entre_temps` (renommage concurrent) ; 404 hors périmètre ou élément absent ; 403 sans rôle de terrain (administrateur seul, pasteur, coordination). Chaque création et modification est tracée dans `patrimoine.journal_inventaire` (ajout seul : auteur, date, valeurs avant et après).


Inventaire — composants (US-07, S07-02) : `/organizations/{orgId}/sites/{siteId}/inventory/components…`
| Opération | Accès | Contenu et réponses |
| --- | --- | --- |
| GET components | `ConsulterSite` | filtres facultatifs `zoneId`, `batimentId`, `corpsEtat`, `verification` (true/false), `recherche` (nom, marque, modèle, n° de série, emplacement ; casse ignorée, % et _ littéraux), `retires` (false par défaut) → `{composants: [composant], tronquee}` par nom, 1000 au plus (`tronquee` si davantage) |
| GET components/{id} | `ConsulterSite` | → `{composant, historique: [{le, nature, auteurCompteId, avant, apres}]}` (plus récent d'abord ; retirés compris) |
| POST components | `ModifierInventaire` | fiche ci-dessous → `{id}` ; `soumisAVerification` absent : proposition du corps d'état |
| PUT components/{id} | `ModifierInventaire` | fiche complète + `version` lue + `soumisAVerification` exigé → 204 ; sans changement, rien n'est écrit |
| POST components/{id}/withdraw | `ModifierInventaire` | `motif` (≤ 500), `version` → 204 ; définitif, le composant reste consultable |

Fiche : `nom` (≤ 120), `corpsEtat` (nom exact : StructureGrosOeuvre, CouvertureEtancheite, MenuiseriesExterieures, MenuiseriesInterieures, RevetementsFinitions, CourantsForts, EclairageSecurite, SecuriteIncendie, CourantsFaibles, ChauffageVentilation, Plomberie, Gaz, Ascenseurs, Paratonnerre, Campanaire, Accessibilite, Mobilier, EquipementsCulte, ExterieursVoirie, Autre), `zoneId` (zone du site), facultatifs `emplacement` (≤ 200), `marque`, `modele`, `numeroSerie` (≤ 120), `anneeMiseEnService` (1000 à l'année en cours), `moisMiseEnService` (1–12, avec l'année, jamais futur), `remarques` (≤ 2000, multiligne), `soumisAVerification`, `lot` absent pour un composant individuel, sinon `{unite: Unite|MetreCarre|MetreLineaire|MetreCube|Ensemble, quantite: 1–100000, repartition?: {bon, satisfaisant, degrade, mauvais, critique}}` (état absent = 0, somme = quantité, RG-10). Composant lu : mêmes champs + `id`, `retireLe`, `motifRetrait`, `version`.

Erreurs : 422 `donnee_invalide` + `champ` (`lot.repartition`, `lot.quantite`, `lot.unite`, `zoneId` absente ou d'un autre site, `version` absente…) ; 409 `modifie_entre_temps` (version dépassée : relire puis refaire), 409 `composant_retire` ; 404 composant absent ou hors périmètre ; 403 sans rôle de terrain. Journal : création (valeurs saisies), modification (champs changés seulement, avant et après), retrait (motif).

Liens publics des sites (S08-04) :
| Opération | Accès | Contenu et réponses |
| --- | --- | --- |
| GET /organizations/{orgId}/sites/public-links | Administrateur (tous les sites) ; responsable et délégués (leurs sites, droit `AfficherLienPublic`) | → `[{siteId, nom, code, numeroUrgence}]` triés par nom ; 403 sans site autorisé ; 404 hors organisation |
| PUT /organizations/{orgId}/sites/{siteId}/emergency-number | Administrateur d'organisation | `numeroUrgence` (chiffres, espaces, + . - et parenthèses, ≤ 30 ; vide = aucun) → 204 ; 422 numéro invalide ; 403 autre rôle |

Dépôts reçus (S08-05), lecture seule :
| Opération | Accès | Contenu et réponses |
| --- | --- | --- |
| GET /organizations/{orgId}/sites/{siteId}/reports | Comptes internes du site (`ConsulterSite` et `LireContactsDeclarant` : T, R/D, P/C) | `limite` (≤ 100, 50 par défaut), `avant` (identifiant du dernier dépôt reçu) → du plus récent au plus ancien : `{id, recuLe, nature, danger, activiteBloquee, emplacement, description, nomDeclarant, contactDeclarant, photos: [rangs]}` ; 404 hors périmètre ; 403 sans rôle de site (administrateur seul) |
| GET …/reports/{reportId}/photos/{rang} | Idem (`ConsulterSite`) | → `image/jpeg` ; 404 si absente |

Application web (S08-05) : `/organisations/{org}/depots?site=…&depot=…` (onglets des sites où l'appelant a un rôle interne, tableau, fiche avec photos via `/organisations/{org}/sites/{site}/depots/{id}/photos/{rang}`, en-tête `Cache-Control: private, no-store`) ; l'accueil y mène les comptes internes.

Application web (S08-04) : formulaire public `/s/{code}` (choix : danger, incident, demande) puis `/s/{code}/signaler?choix=…` (multipart, widget Turnstile si `Web:CleSiteTurnstile`, limite `Web:LimiteDepotsParAdresse` envois par adresse et par 10 min, adresse lue dans `Web:EnteteAdresseClient` derrière le relais), accusé sans suivi ; écran `/organisations/{org}/sites` (QR SVG, lien, affiche A4 `/affiche`, PNG `/qr.png`, numéro d'urgence pour l'administrateur).

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


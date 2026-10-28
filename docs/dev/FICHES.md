# Fiches d’implémentation et préparation des cartes
Statut : exemples détaillés P ; les tickets applicatifs restent bloqués tant que stack et contrats ne sont pas adoptés.

## F-01 — Dépôt public (US-08)
Objectif : recevoir un constat sans compte sur le bon site. Références RG-02/03, UI-01, T-02.
Inclus : formulaire, photo facultative, persistance, confirmation locale, protection d’abus. Exclu : suivi public, vérification contact, consultation d’inventaire.
Préconditions : route publique/site active, stockage photos, types/tailles arrêtés (décisions du 28/10, REGLES : 3 photos, 10 Mo, JPEG/PNG/HEIC, métadonnées retirées ; Turnstile + limites ; QR pour A et R/D ; liste en lecture des dépôts).
Découpage adopté (28/10) : UX-03 maquettes ; S08-01 contrat et persistance sans doublon ; S08-02 photos ; S08-03 anti-abus ; S08-04 formulaire et QR ; S08-05 liste des dépôts reçus.
Critères :
- Étant donné un lien de site actif, quand tous les champs requis sont envoyés sans photo, alors un dossier est créé pour ce site.
- Si le contact n’est pas vérifié, le dépôt reste possible ; il est conservé comme information déclarative.
- Un rejeu de même commande ne crée pas un second dossier.
- La réponse ne fournit aucun secret permettant de lire le dossier.
- Danger transmis : événement d’alerte persisté avec le dossier.
Tests : validation, mauvais site, coupure/rejeu, upload, absence d’accès par identifiant connu.
Découpage proposé : contrat/validation ; endpoint/persistance ; formulaire ; photo ; tests/revue. Estimations sous-cartes à calibrer dans la fourchette US-08.

## F-02 — Score hiérarchique (US-11)
Préconditions : DEC-01 clos, catégories et rattachements adoptés.
Cas d’or : zone A 8/6, zone B 9 → site 8 ; ajouter un composant non évalué modifie couverture mais pas moyenne.
Une note ancienne reste utilisée avec sa date ; zéro est une vraie note, distincte de null.
Changement de note : recalcul concerné ; règles de lots et composants retirés testées selon décision.
Exclu : inventer une note à partir d’une photo non validée.

## F-03 — Réservation budgétaire (US-17)
Préconditions : DEC-02, schéma transactionnel et contrat adoptés.
Cas d’or : solde 100, deux demandes simultanées de 80 ; une seule autorisation ordinaire réussit.
Le rejet ne crée pas d’engagement partiel. Rejeu de la requête acceptée ne réserve pas 80 supplémentaires.
Exception pour dossier A : ne permet pas à B de dépasser son solde.
Tests transactionnels réels, pas seulement mocks. Justificatif et version d’estimation autorisée liés.

## Modèle de sous-carte
ID ; résultat ; inclus/exclus ; règles ; décisions résolues ; fichiers/modules attendus ; contrats ; migrations ; critères ; tests/commandes ; dépendances ; estimation ; responsable ; preuve et retour arrière.
Une sous-carte n’est Prête que lorsque ces champs ont des réponses concrètes. Le découpage complet de toutes les macro-stories sera affiné par lot, pas inventé avant les POC.


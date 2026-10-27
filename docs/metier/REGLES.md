# Catalogue des règles métier
V = validée ; P = proposition complémentaire. Les détails ouverts sont dans le registre des arbitrages.

| ID | Statut | Règle |
| --- | --- | --- |
| RG-01 | V | Organisations séparées ; accès aux sites selon habilitation. |
| RG-02 | V | Dépôt public sans compte ni vérification : nom, contact, site, emplacement, description ; photo facultative ; aucun suivi public. |
| RG-03 | V | Un QR général par site ; aucun accès public aux plans ou à l’inventaire. |
| RG-04 | V | Responsable/délégués qualifient, priorisent et affectent ; pasteurs/coordination consultent, déclarent et relancent. |
| RG-05 | V | Danger signalé : alerte avant qualification et suivi distinct de la mise en sécurité. |
| RG-06 | V | Technicien affecté clôture avec actions et état final ; photo selon catégorie. Responsable peut rouvrir. |
| RG-07 | V | Attente : motif et réexamen ; délai de résolution suspendu, ancienneté conservée. |
| RG-08 | V | Rappels puis escalade selon priorité ; notifications internes et push. |
| RG-09 | V | Préventif calendaire/ponctuel ; non-conformité génère un signalement. |
| RG-10 | V | Inventaire individuel et lots, avec quantités réparties par état. |
| RG-11 | V | Note sur 10, moyenne par niveaux, dernière évaluation conservée, fraîcheur et couverture visibles. |
| RG-12 | P | Arrondir uniquement l’affichage à une décimale ; conserver précision de calcul ; sans évaluation, aucune note. |
| RG-13 | V | IA lancée par utilisateur technique habilité ; informations insuffisantes : compléments, sans total global. |
| RG-14 | V | Produits France, fournisseurs ouverts, proposition principale et jusqu’à deux alternatives. |
| RG-15 | V | Responsable/délégué valide estimation et solution ; envoi PDF déclenché explicitement et tracé. |
| RG-16 | V | Enveloppe par site/période ; réservation dès autorisation ; pas de plafond par dossier. |
| RG-17 | V | Dépassement : accord préalable même urgent, limité au dossier et au montant ; justificatif requis. |
| RG-18 | V | Dépense réelle : montant, date, fournisseur, justificatif, dossier ; ajuster la réservation. |
| RG-19 | V | Demande matérielle suivie jusqu’à remise/installation ; achat réalisé hors application. |
| RG-20 | V | Transfert : départ puis réception ; état en transit entre les deux. |
| RG-21 | V | Technicien consomme en intervention ; responsable/délégués réceptionnent, ajustent et mettent au rebut. |
| RG-22 | V | Surconsommation hors connexion : garder les faits, signaler l’écart et régulariser après comptage. |
| RG-23 | V | Hors connexion 24 h, profils préparés, déverrouillage personnel ; conserver les saisies à expiration. |
| RG-24 | V | Ajouts compatibles conservés ; conflits de valeurs soumis à arbitrage, sans écrasement silencieux. |
| RG-25 | V | Position personnelle hors connexion à démontrer ; recalage puis choix manuel en cas d’incertitude. |
| RG-26 | V | Partage explicite, suspendable, limité à l’équipe habilitée du site ; aucun trajet conservé. |
| RG-27 | V | Rapports à la demande, filtrés selon les droits, export PDF. |
| RG-28 | V | Support temporaire autorisé et tracé ; délégation opérationnelle distincte d’administration. |
| RG-29 | V | Email/mot de passe initial ; MFA et fédération dans des lots ultérieurs. |
| RG-30 | V | Ouverture SaaS sur demande ; abonnement par organisation, facturation manuelle initiale. |
| RG-31 | P | Toute correction sensible conserve l’auteur, le motif et la valeur précédente dans un journal approprié, hors trajets de position. |
| RG-32 | P | Une modification matérielle d’estimation invalide sa validation antérieure ; nouvelle autorisation si montant ou périmètre autorisé change. |

## Scoring : proposition à arbitrer
Barème candidat : [0;2[ critique, [2;4[ mauvais, [4;6[ dégradé, [6;8[ satisfaisant, [8;10] bon. Ne pas implémenter ces seuils sans décision DEC-01.
Un danger ne disparaît pas si la moyenne est bonne. Les grilles doivent distinguer constat, état, mesure et note ; l’IA ne remplace pas l’évaluation humaine validée.

## Priorités, délais et relances — DEC-05, V, 27 octobre 2026 (porteur du projet)
Priorité fixée par R/D à la qualification, indépendamment du danger déclaré (qui reste une alerte distincte).

| Priorité | Usage | Prise en charge (dossier affecté) | Résolution (clôture, hors attente) |
| --- | --- | --- | --- |
| Urgente | Danger, activité bloquée | 4 h | 2 jours |
| Haute | Gêne importante | 1 jour | 7 jours |
| Normale | Cas courant | 3 jours | 30 jours |
| Basse | Amélioration, peut attendre | 7 jours | 90 jours |

- Délais comptés en temps calendaire (week-ends et jours fériés inclus), pour toutes les priorités. Ce sont des cibles de suivi, pas des engagements de service.
- Les périodes « En attente » suspendent le délai de résolution ; l’ancienneté totale reste affichée.
- Rappels : à 75 % du délai, rappel à la personne en charge (R/D si non affecté, sinon technicien affecté). À 100 %, escalade au responsable et aux délégués du site, puis rappel quotidien tant que le délai est dépassé. Une relance ne crée pas de doublon de notification pour le même seuil.
- Récurrence : au moins 3 dossiers (hors « classée sans suite » et doublons rattachés) sur le même composant ou la même zone en 12 mois glissants.

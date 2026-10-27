# Glossaire et dictionnaire métier
Statut : P de formalisation du PRD ; les concepts fonctionnels sont issus du cadrage.

| Terme | Définition et données essentielles |
| --- | --- |
| Organisation | Client autonome, église ou réseau ; identité, état d’activation, administrateurs, abonnement. |
| Site | Adresse ou ensemble géographique ; nom, adresse, organisation, contacts d’urgence, fuseau. |
| Bâtiment / niveau | Structure physique et étage ; nom, rattachement, géométrie facultative. |
| Pièce / zone | Unité de localisation et d’agrégation du score ; nom, niveau, identifiant stable. Une zone peut représenter un espace extérieur. |
| Composant | Élément évalué du bâti ou matériel ; catégorie, zone de référence, mode individuel ou lot, état actif/retiré. |
| Lot | Ensemble homogène ; unité, quantité totale et répartition par état. Formule de note encore ouverte. |
| Évaluation | Observation datée d’un composant ; auteur, grille versionnée, réponses, mesures, note sur 10, photos. |
| Mesure | Valeur numérique, unité, instant observé ; ne pas confondre unité et libellé libre. |
| Score | Agrégat calculé et libellé ; couverture, fraîcheur, version du calcul. Ce n’est pas un certificat de sécurité. |
| Signalement | Constat ou besoin reçu, sans présumer d’une intervention ; description, site, emplacement, déclarant, danger. |
| Intervention | Travail affecté lié à un dossier ; intervenant, actions, dates, attente et compte rendu. |
| Inspection | Exécution d’une grille planifiée ou ponctuelle, pouvant générer un signalement. |
| Estimation | Proposition interne versionnée, corrigible, avec sources et hypothèses ; distincte d’un devis fournisseur. |
| Engagement | Somme réservée par autorisation, non encore totalement convertie en dépense. |
| Dépense | Montant réel daté, fournisseur, justificatif et dossier. |
| Autorisation exceptionnelle | Accord financier plafonné pour un dossier ; n’augmente pas l’enveloppe commune. |
| Mouvement de stock | Entrée, consommation, ajustement, rebut, départ ou réception de transfert. |
| Session de position | Partage temporaire volontaire ; dernière position, précision, date et intervention. Aucun trajet. |

## Champs communs proposés
Identifiant stable, organisation, création, auteur, version de concurrence. Dates métier et dates de réception serveur distinctes pour le hors connexion. Montants en décimal et devise, jamais en flottant binaire. Les contacts du déclarant public sont non vérifiés et ne constituent pas une identité authentifiée.

## Mesures de suivi
- Ancienneté : clôture ou instant courant moins réception.
- Prise en charge : premier démarrage effectif moins réception ; à distinguer du délai d’affectation.
- Attente : union des intervalles d’attente pour éviter le double comptage.
- Temps hors attente : ancienneté moins attente ; pas une mesure de productivité individuelle.
- Couverture : composants actifs évalués / composants actifs éligibles ; si dénominateur nul, afficher « sans objet ».
- Score pièce proposé : somme des dernières notes disponibles / nombre de composants notés.
- Score site proposé : moyenne des scores des zones notées, sans ajouter une nouvelle moyenne par bâtiment ou étage.

Exemple : pièce A avec 8 et 6 → 7 ; pièce B avec 9 → 9 ; site → 8, et non 7,67. Un composant non évalué n’est pas noté zéro. Les composants retirés et les modifications de périmètre nécessitent une règle d’historisation à valider.


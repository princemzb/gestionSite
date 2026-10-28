# Risques et arbitrages
Statut initial : tous les arbitrages ci-dessous sont ouverts ; aucune approbation supposée.

| ID | Décision / risque | Responsable proposé | Bloque |
| --- | --- | --- | --- |
| DEC-01 | Barèmes, lots, score de zones communes, fraîcheur, arrondi : **tranchée** (REGLES, 27/10/2026) | Métier + BA | — |
| DEC-02 | Périodes, TTC/HT, allocation et régularisation des engagements : **tranchée** (REGLES, 27/10/2026) | Métier/finance | — |
| DEC-03 | Statuts, doublons, refus, annulation, cardinalité interventions : **tranchée** (PROCESSUS, 27/10/2026) | Métier + BA | — |
| DEC-04 | Lecture des coûts/contacts et droits d’édition grilles/plans : **tranchée** (PERMISSIONS, 27/10/2026) | Métier | — |
| DEC-05 | Priorités, horaires et délais de relance : **tranchée** (REGLES, 27/10/2026) | Métier | — |
| DEC-06 | Frameworks et hébergement préprod : **tranchés** (ADR-08, 27/10/2026). Restent : versions et appareils supportés, hébergement de production | Tech lead | US-13 / production |
| DEC-07 | Conservation, traitement IA, données locales et responsabilités | Référents métier/technique/données | Production |
| DEC-08 | Précision localisation et moyens installables | Métier + tech lead | US-23 |
| DEC-09 | Sources marchandes accessibles et budget IA | Tech lead + produit | US-19/20 |
| DEC-10 | Quotas, tarification, impayés, sortie du SaaS | Produit | US-28 / commercial |
| DEC-11 | Langues et usage téléphone des rôles internes | Produit | Design définitif |
| DEC-12 | Performance, restauration, disponibilité et budgets | Produit + tech lead | Engagement de service |

## Principaux risques
- Localisation multiétage et hors Internet non démontrée : étude précoce, matériel et budget à comparer ; maintenir repli manuel.
- Équipe réduite face à une cible large : déploiements par lots, limiter travaux parallèles et mesurer capacité.
- Données de départ absentes : campagne de relevé/inventaire avec responsable, lots et contrôle.
- Prix externes incomplets/périmés : date/source, absence de total si essentiel manquant, validation humaine.
- Préemption d'adresse e-mail : tant que les liens d'activation passent par l'administrateur (ADR-10), un tiers peut créer et activer un compte pour l'adresse d'autrui, puis accepter les invitations qui lui sont faites. Atténuations : invitation à accepter, lien remis seulement par l'organisation qui crée le compte ; levée avec l'envoi des liens à l'adresse (US-18).
- Révocation sur tablette hors ligne : fenêtre d’exposition à expliciter ; aucune promesse de révocation instantanée.
- Score rassurant avec données anciennes : couverture et fraîcheur obligatoires, alertes critiques séparées.
- Documents périmés : liens RG/US/T et revue documentaire obligatoire dans les PR.
- Estimation confondue avec engagement : recalcul après POC et capacité réelle.

Une décision clôturée doit porter auteur, date, choix et documents affectés. Un ticket bloqué référence DEC et l’action permettant de le débloquer.


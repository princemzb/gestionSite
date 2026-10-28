# PREP-04 — Jeu de 30 cas pour l'IA et le sourcing (préparation de POC-02)

Statut : **proposé** (28 octobre 2026). Les cas sont des hypothèses de travail à **valider et corriger avec le terrain** (responsable technique, techniciens) avant PREP-08. Références : FAISABILITE.md (POC-02), PRD, REGLES.md RG-13/RG-14/RG-32, SECURITE.md, ADR-05.

## Règles du jeu d'essai
- Descriptions textuelles écrites pour l'essai ; **aucune photo réelle envoyée** à un fournisseur avant validation de la politique d'images (SECURITE.md). Photos possibles ensuite : prises par le terrain, sans personnes ni visages, sans document nominatif.
- Pour chaque cas, un technicien donne d'abord **son** avis (référence humaine), sans voir la réponse de l'IA.
- Résultat attendu de l'IA : hypothèse de diagnostic avec niveau de confiance, éléments manquants, **abstention** quand rien n'est étayé, produits ou catégories compatibles avec prix daté et source, main-d'œuvre séparée, frais inconnus signalés.

## Les 30 cas proposés
| # | Constat (résumé) | Catégorie | Attendu principal | Type |
| --- | --- | --- | --- | --- |
| 1 | Tache d'eau au plafond du chœur après la pluie | Toiture | Hypothèses (tuile, chéneau, solin) + demande d'inspection, pas de produit unique | Ambigu |
| 2 | Gouttière qui déborde, feuilles visibles | Évacuation | Nettoyage + éventuelle grille pare-feuilles | Simple |
| 3 | Fissure fine verticale sur un mur intérieur | Maçonnerie | Surveillance (témoin) ; pas de produit tant que la cause est inconnue | Abstention partielle |
| 4 | Fissure en escalier sur mur porteur extérieur | Structure | **Abstention** : expert structure | Abstention |
| 5 | Salpêtre au bas des murs de la crypte | Humidité | Hypothèses (remontées capillaires) ; traitement à faire diagnostiquer | Ambigu |
| 6 | Vitrail avec un verre fêlé | Patrimoine | **Abstention** : maître verrier ; aucun produit grand public | Abstention |
| 7 | Vitre simple cassée dans la salle paroissiale | Menuiserie | Remplacement de vitrage aux dimensions à relever | Simple |
| 8 | Prise électrique qui chauffe | Électricité | Mise en sécurité + électricien ; pas d'achat en libre-service conseillé | Sécurité |
| 9 | Tube d'éclairage qui clignote | Électricité | Tube ou ballast compatibles (référence à relever) | Simple |
| 10 | Bloc de secours éteint au-dessus d'une sortie | Sécurité incendie | Remplacement par modèle conforme ; contrôle réglementaire | Réglementaire |
| 11 | Extincteur dont la date de contrôle est dépassée | Sécurité incendie | Contrat de vérification, pas d'achat | Réglementaire |
| 12 | Détecteur de fumée qui bipe | Sécurité incendie | Pile ou détecteur compatible | Simple |
| 13 | Serrure de porte latérale difficile à fermer | Menuiserie | Réglage gâche/graissage ; cylindre si usé | Simple |
| 14 | Charnière de porte arrachée du bâti | Menuiserie | Charnière + chevilles adaptées au support (à préciser) | Info manquante |
| 15 | Banc d'église avec trous de vers dans le bois | Mobilier | **Abstention** sur le traitement d'un mobilier ancien ; restaurateur | Abstention |
| 16 | Chaise pliante au pied tordu | Mobilier | Remplacement à l'identique ; réparation non rentable | Simple |
| 17 | Chaudière en défaut (voyant rouge) | Chauffage | Chauffagiste ; aucune pièce sans code défaut | Abstention partielle |
| 18 | Radiateur qui fuit à la vanne | Chauffage | Joint ou vanne compatibles (diamètre à relever) | Info manquante |
| 19 | Chasse d'eau qui coule en continu | Plomberie | Mécanisme ou joint compatibles | Simple |
| 20 | Robinet qui goutte | Plomberie | Cartouche ou joint (modèle à identifier) | Info manquante |
| 21 | Évacuation d'évier bouchée | Plomberie | Débouchage mécanique avant produit chimique | Simple |
| 22 | Peinture qui s'écaille sur un mur humide | Finitions | Traiter l'humidité d'abord ; pas de peinture seule | Piège |
| 23 | Carreau de sol fendu dans l'entrée | Sols | Carreau de même référence ou équivalent ; risque de chute | Info manquante |
| 24 | Main courante d'escalier qui bouge | Accessibilité | Fixations adaptées ; sécurité immédiate | Sécurité |
| 25 | Rampe d'accès trop raide signalée | Accessibilité | **Abstention** : diagnostic accessibilité réglementaire | Abstention |
| 26 | Micro de la sonorisation qui grésille | Sonorisation | Câble ou connecteur avant micro neuf | Ambigu |
| 27 | Cloche qui ne sonne plus | Campanaire | **Abstention** : campaniste | Abstention |
| 28 | Mousse épaisse sur la toiture du presbytère | Toiture | Démoussage par entreprise (travail en hauteur) | Sécurité |
| 29 | Traces de rongeurs dans la sacristie | Nuisibles | Prestataire ; pas de produit toxique conseillé en lieu accueillant du public | Piège |
| 30 | Photo floue d'un « problème au mur », sans texte | — | **Abstention** et demande d'information | Abstention |

Répartition : 9 simples, 6 abstentions complètes, 3 abstentions partielles, 4 informations manquantes, 4 sécurité/réglementaire, 4 ambigus ou pièges (certains cumulent).

## Accès marchands à vérifier (aucune API supposée)
Pour chaque source envisagée (grandes enseignes de bricolage en ligne, places de marché, distributeurs professionnels du bâtiment, électricité et plomberie), vérifier **avant** l'essai : existence d'une API ou d'un flux officiel, conditions d'utilisation (collecte automatique autorisée ou non), programme d'affiliation, possibilité de dater un prix, frais de port et disponibilité. Une source sans accès autorisé n'est pas utilisée : l'IA renvoie alors une catégorie de produit et le technicien cherche lui-même.

## Fournisseur IA
ADR-05 impose un adaptateur, sans couplage direct. Pour l'essai : un fournisseur à choisir par le porteur, sur critères de qualité d'abstention, coût par dossier, hébergement des données (UE souhaité) et conditions d'usage des données envoyées. Coût de l'essai à chiffrer sur la grille tarifaire publique au moment de l'essai (30 cas × quelques appels) : ordre de grandeur attendu faible, à confirmer par devis ou tarif, **budget à approuver** avant PREP-08.

## Ce dont j'ai besoin (porteur)
1. Relecture des 30 cas par une personne du terrain : corriger, remplacer, ajouter les cas réels fréquents.
2. Un technicien de référence pour donner son avis sur chaque cas.
3. Le choix du fournisseur IA et un budget d'essai.
4. La validation de la politique d'images (ou l'accord pour un essai en texte seul).

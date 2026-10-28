# PREP-03 — Protocole de terrain : localisation intérieure (préparation de POC-01)

Statut : **proposé** (28 octobre 2026), à valider par le porteur avant PREP-07. Aucun essai réalisé, aucun matériel acheté. Références : FAISABILITE.md (POC-01), REGLES.md RG-25/RG-26, SECURITE.md.

## Question à trancher
Peut-on situer un technicien **dans la bonne pièce et au bon étage**, hors Internet, avec les appareils disponibles, assez vite pour l'usage (affichage de l'emplacement, rattachement d'un constat) ? Sinon, quel repli (recalage, choix manuel) et à quel coût ?

## Appareils
| Appareil | État | Conséquence pour l'essai |
| --- | --- | --- |
| Lenovo Tab M11 (Android 15) | Disponible. Capteurs déclarés : accéléromètre, gyroscope, podomètre, Bluetooth LE ; **non déclarés : baromètre, boussole, Wi-Fi RTT, UWB** (relevé ADB du 27/10) | Pas de détection d'étage par pression, pas de cap absolu : cap relatif par gyroscope, dérive à mesurer |
| iPad | **Indisponible** | Volet iPad reporté ; à obtenir avant toute conclusion multi-plateforme (DEC-06) |

## Techniques comparées (sans achat)
| Code | Technique | Ce qu'elle exige | Risque principal |
| --- | --- | --- | --- |
| T1 | Navigation à l'estime : podomètre + gyroscope sur plan | Plan du site avec échelle approximative, point de départ connu | Dérive (pas de boussole), changements d'étage invisibles |
| T2 | Empreinte des réseaux Wi-Fi existants (puissance reçue) | Bornes Wi-Fi déjà présentes dans le bâtiment ; relevé préalable pièce par pièce | Android limite les balayages (4 toutes les 2 min au premier plan) ; peu ou pas de bornes dans une église |
| T3 | Signaux Bluetooth existants | Appareils BLE déjà présents (rare) | Probablement inutilisable sans balises |
| T4 | Points de recalage : QR imprimés aux portes et escaliers, lus par la caméra | Feuilles imprimées (accord du lieu) | Geste volontaire du technicien ; affichage dans un lieu de culte |
| T5 | Choix manuel de la pièce dans la liste (repli RG-25) | Liste des pièces | Saisie ; c'est la référence de comparaison |

Hors protocole (achat ou matériel absent) : balises BLE dédiées, UWB, Wi-Fi RTT. Elles ne seront chiffrées qu'**après** les mesures, si T1–T4 sont insuffisantes.

## Site recherché
- Au moins **deux niveaux** reliés par un escalier (ascenseur si présent), 8 à 15 pièces ou zones nommées (nef, chœur, sacristie, tribune, salles, bureaux, sous-sol…).
- Un plan, même sommaire (croquis coté accepté), et la liste des pièces avec leurs noms d'usage.
- Une zone sans réseau mobile ni Wi-Fi si possible.

## Parcours (chacun répété 3 fois)
| Parcours | Déroulé | Mesure visée |
| --- | --- | --- |
| P1 | Pièce à pièce sur un même niveau (6 pièces, 2 min d'arrêt chacune) | Pièce correcte, latence de transition |
| P2 | Changement de niveau par l'escalier, aller et retour | Étage correct, latence |
| P3 | Application fermée puis rouverte au milieu de P1 | Reprise, recalage nécessaire ou non |
| P4 | Mode avion pendant tout P1, puis réactivé | Fonctionnement hors Internet |
| P5 | Marche continue de 10 min sans recalage | Dérive cumulée |

**Vérité terrain** : un observateur note l'heure exacte de chaque entrée dans une pièce (feuille horodatée ou bouton de l'outil de mesure). Aucune précision n'est annoncée sans cette mesure (critère PREP-07).

## Mesures et seuils candidats (non promis)
- Pièce correcte (part du temps passé avec la bonne pièce affichée) — candidat ≥ 95 %.
- Étage correct — candidat ≥ 99 %.
- Latence de transition (entrée réelle → affichage), p95 — candidat ≤ 5 s.
- Dérive en P5 (mètres ou pièces d'écart), batterie consommée par heure, taux d'échec et étapes échouées.

## Données et confidentialité
- Aucune donnée personnelle : pas de nom, pas de photo de personnes, identifiant d'appareil anonymisé.
- Les traces de position restent dans les fichiers d'essai, supprimées après le rapport (RG-26 : aucun trajet conservé dans le produit).
- Photos du lieu seulement avec l'accord du responsable, sans personnes.

## Outil de mesure
Un enregistreur de capteurs sans interface élaborée (journal horodaté des pas, rotations, balayages Wi-Fi/BLE, lectures QR, saisies de vérité terrain). Code d'essai jetable. Toute interface destinée aux utilisateurs reste soumise à validation de maquettes.

## Ce dont j'ai besoin (porteur)
1. Le site d'essai (ou deux candidats) et l'accord de son responsable.
2. Deux demi-journées et une personne sur place avec la tablette (moi, je prépare l'outil et l'analyse, je ne peux pas me déplacer).
3. Le plan ou un croquis, la liste des pièces, la présence ou non de Wi-Fi.
4. L'accord pour imprimer et poser temporairement des QR (T4).
5. Pour le volet iPad : un appareil ou la décision de le reporter.

## Livrables de PREP-07
Données brutes anonymisées, tableau des mesures par technique et parcours, étapes échouées, limites, coût (matériel, temps de relevé par site, entretien) et recommandation motivée — y compris « choix manuel + recalage QR » si les mesures ne justifient pas plus.

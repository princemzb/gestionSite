# Charte graphique et thèmes
Statut : **V** — choisie par le porteur le 27 octobre 2026 (2e série de maquettes : V1 retenue comme thème Jour, V2 comme thème Nuit). Maquettes de référence : canevas privé https://claude.ai/artifact/MMmTWs6J8aH8wKroDG6PQn.

Origine : couleurs, dégradés et formes relevés sur le site de référence indiqué par le porteur ; police propriétaire remplacée par Inter ; ni logo, ni texte, ni illustration repris.

## Maquettes validées
- 28 octobre 2026 : parcours du 1er incrément validé par le porteur, en Jour et Nuit — Réglages (bascule de thème), file de qualification (UI-03), dossier (UI-04), clôture d'intervention (tablette), sur le même canevas. Ils font référence pour l'implémentation.
- 28 octobre 2026 (UX-02) : 1er écran testable — connexion = proposition B « Bandeau », gestion des membres = proposition C « Par site » (thème Jour, choix du porteur) ; déclinaison Nuit validée le même jour. Ils font référence pour l'implémentation web.
- Restent à maquetter, puis à faire valider avant code, avec leurs US : fiche composant (UI-05), proposition IA (UI-08), autorisation budgétaire (UI-09), plan (UI-10), stock (UI-11), administration (UI-12).

## Règles de thème
- Deux thèmes : **Jour** (par défaut) et **Nuit**.
- Chaque utilisateur bascule dans ses réglages ; le choix est mémorisé **par compte** (sur une tablette partagée, chaque profil garde le sien).
- Proposition (P) : le formulaire public, sans compte, s'affiche en thème Jour.
- Un état (danger, retard, synchronisation) ne repose jamais sur la couleur seule : libellé toujours écrit. Contraste du texte ≥ 4,5:1 dans les deux thèmes.

## Jetons communs
| Jeton | Valeur |
| --- | --- |
| Police | Inter (400, 500, 600, 700) |
| Rayons | 8, 12, 16, 24 px (cartes) ; boutons en pastille (999 px) |
| Dégradé d'action | 90°, #1E6CFF → #7569FF (bouton principal, texte blanc) |
| Cibles tactiles | ≥ 44 px |
| Espacements | 4, 8, 12, 16, 24, 32, 48, 64 px |

## Thème Jour (V1)
| Jeton | Valeur |
| --- | --- |
| Fond | #F4F4F5 |
| Surface (cartes) | #FFFFFF, ombre `0 1px 3px #0000000A, 0 8px 28px #0000000A` |
| Encre | #000740 ; secondaire #3D4654 ; tertiaire #6B7588 |
| Barre de navigation | #000740, texte #FFFFFF / #D2E2FF |
| Lien, accent | #005DC4 (survol #004A9E) |
| Bandeau | 135°, #E6E6EC → #A5C4FF (transparence 64 %) |
| Carte mise en avant | halo radial #005DC42E + 135°, #FFFFFF → #F6F8FB → #DFE8F7 |
| Bordure | #0007400F (forte #0007401F) |
| Succès / avertissement / danger | #0D7C3E / #B45309 / #C7202C, fonds à 8 % (`14` en hexadécimal) |

## Thème Nuit (V2)
| Jeton | Valeur |
| --- | --- |
| Fond | halo radial #005DC46B (coin haut droit) + 135°, #07090D → #0A111D → #16284A |
| Surface (verre) | #FFFFFF0F, bordure #FFFFFF1F |
| Encre | #F2F5FA ; secondaire #AEB8C8 |
| Lien, accent | #A5C4FF (survol #D2E2FF) |
| Carte mise en avant | 132°, #0B1118 → #152A58 → #5D5DF3, halos #FFFFFF1F et #005DC4B8 |
| Succès / avertissement / danger | #3DDC84 / #F5A524 / #FF8A90, fonds à 15 % |

## Mise en œuvre
- Web (Blazor) : variables CSS par thème, basculées par un attribut sur la racine.
- Tablette (Flutter) : deux `ThemeData` construits depuis les mêmes jetons.
- Les jetons sont la source unique : aucune couleur codée en dur dans les écrans.

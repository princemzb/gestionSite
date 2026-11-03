# Manuel d'utilisation — fonctionnement et mise à jour

Ce dossier est la **source unique** du manuel d'utilisation de gestionSite (carte DOC-01). Le même texte sert :

- au **livret PDF A4**, imprimé ou lu sur tablette ;
- à l'**aide intégrée** de l'application (DOC-02, à venir) : chaque section porte un identifiant d'ancre stable que les écrans pourront citer.

## Contenu

| Fichier | Chapitre |
| --- | --- |
| `00-sommaire.md` | Présentation, liste des chapitres, ce qui n'existe pas encore |
| `01-principes.md` | Organisations, sites, rôles, droits, menu, appareils, thèmes |
| `02-connexion.md` | Activation, connexion, déconnexion |
| `03-administration.md` | Membres, invitations, sites, QR code, numéro d'urgence, affiche |
| `04-depot-public.md` | Formulaire public (vu du fidèle) et signalements reçus |
| `05-dossiers.md` | Suivi, déclaration, qualification, affectation, interventions, attente, clôture, réouverture, délais |
| `06-inventaire.md` | Bâtiments, niveaux, zones, composants, lots |
| `07-evaluations.md` | Évaluations, état du site, grilles |
| `08-preventif.md` | Classement ERP, plans proposés, calendrier, contrôles, vérification extérieure, non-conformités |
| `09-plans.md` | Plans de bâtiment : parcours guidé, fond, calibration, atelier, brouillon et conflit |
| `10-workflows.md` | Cycles de vie en schémas (Mermaid) |
| `11-glossaire.md`, `12-faq.md` | Glossaire, questions fréquentes |
| `images/` | Captures d'écran (PNG en palette, < 300 Ko) |

## Règles d'écriture

- Décrire **ce qui existe** en préproduction, jamais ce qui est seulement prévu. Ce qui est prévu va dans un encadré « **Pas encore disponible.** ».
- Vouvoiement, phrases courtes, aucun jargon technique. Les libellés d'écran sont repris à l'identique, en **gras**.
- Une tâche = **Pour qui**, **Où cliquer**, étapes numérotées, ce que l'on voit ensuite, erreurs fréquentes (messages exacts de l'application).
- Chaque titre de niveau 1 à 3 porte une **ancre stable** : `## Qualifier un dossier {#dossiers-qualifier}` (minuscules, chiffres, tirets ; préfixe du chapitre). Une ancre publiée ne change plus : l'aide intégrée s'y réfère. Si une section disparaît, gardez son ancre sur la section qui la remplace.
- Liens entre chapitres : `[texte](05-dossiers.md#dossiers-qualifier)`. Images : `![légende](images/nom.png)` ; une capture de téléphone se nomme `…-tel.png` (affichée plus étroite dans le livret).
- Données d'exemple **fictives** uniquement (organisation « Paroisse de démonstration », personnes et numéros inventés). Aucune adresse réelle de l'application, aucun lien d'activation, aucun QR code lisible.

## Contrôler et produire le livret

Prérequis : Node.js 22.

```bash
cd scripts/manuel
npm ci
npx playwright install chromium   # une fois par poste
./verifier.sh                     # tests de l'assemblage (couverture ≥ 80 %) et contrôle du manuel
npm run livret                    # → scripts/manuel/sortie/manuel-gestionsite.pdf
```

- `node livret.mjs --verifier` contrôle seulement le manuel (ancres présentes, valides et uniques ; images et liens existants ; aucune image orpheline), sans navigateur. La CI le lance à chaque PR (job « Outillage Trello (tests) »).
- Le livret assemble les chapitres dans l'ordre de leur numéro, ajoute une couverture et un sommaire avec les numéros de page (deux impressions successives : la première sert à lire la page de chaque section), rend les schémas Mermaid et numérote les pages en pied de page.
- Le PDF (environ 4 Mo, 75 pages au 3 novembre 2026) n'est pas versionné : `scripts/manuel/sortie/` est ignoré par Git.

## Mettre à jour les captures

Les captures sont prises en préproduction, sur une organisation fictive ouverte pour l'occasion, par `deploy/manuel/captures.mjs` (Playwright 1.55.0, thème Jour ; ordinateur 1280 × 800, téléphone 390 × 844, tablette 1280 × 800).

1. Ouvrir une organisation fictive avec la commande d'exploitation `ouvrir-organisation --nom "Paroisse de démonstration" --admin-nom "Alice Administratrice" --admin-email … --site "Église du centre"` (docs/exploitation/DEPLOIEMENT.md, étape 8 ; chaînes Neon depuis le Trousseau, jamais affichées).
2. Lancer `VERIF_URL=… VERIF_ORG=… VERIF_JETON=… CAPTURES=<dossier> node captures.mjs` depuis un dossier où Playwright est installé. Le script joue tout le scénario (membres, inventaire, évaluations, dossiers, préventif, plans, formulaire public) et écrit les PNG.
3. Avant chaque capture, le script remplace l'adresse de la préproduction par `https://gestionsite.exemple`, masque les jetons d'activation et floute les QR codes. Vérifiez quand même chaque image avant de la versionner.
4. Optimiser les PNG (palette de 256 couleurs, par exemple avec `sharp` ou `pngquant`) pour rester sous 300 Ko, puis les copier dans `images/` sous le même nom.
5. `./verifier.sh`, puis relire les pages concernées du livret.

Le dépôt public réel n'est pas capturé : le défi anti-robot refuse un navigateur automatisé. L'accusé de réception est décrit d'après l'écran.

## Règle de mise à jour

**Toute story qui change un comportement visible met à jour le chapitre concerné de `docs/manuel/` (texte et captures) avant Livré** (AGENTS.md, section Exécution ; docs/pilotage/TRELLO.md, conditions de passage).

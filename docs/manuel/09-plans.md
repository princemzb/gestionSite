# 9. Plans de bâtiment {#plans}

Le **plan de bâtiment** est le dessin d'un niveau (ou du plan masse pour un bâtiment sans niveau). On part d'un plan papier scanné, d'une photo, d'un PDF, ou d'une grille vierge en mètres.

Ne confondez pas avec les **plans préventifs** du chapitre [Préventif](08-preventif.md#preventif).

**Pour qui :** tous les membres du site consultent les plans. Le responsable et le délégué les modifient, ainsi que les membres à qui l'administrateur a accordé le droit « modifier les plans du bâtiment ».

**Où cliquer :** menu **Plans**, puis les onglets du site, du bâtiment et du niveau.

Les plans se dessinent de préférence sur ordinateur ou tablette.

> **Pas encore disponible.** Le tracé des pièces, des ouvertures et des escaliers, et le placement des composants sur le plan, arrivent dans une prochaine version (étapes 3 et 4 du parcours guidé). Le droit nominatif « modifier les plans du bâtiment » n'a pas encore d'écran : demandez-le à l'équipe du projet.

## Créer un plan : le parcours guidé {#plans-parcours}

Quand un niveau n'a pas encore de plan, l'écran **Nouveau plan** ouvre un parcours en quatre étapes : **Fond du plan**, **Calibrer l'échelle**, **Tracer les pièces**, **Placer les composants**. Chaque étape validée est enregistrée comme une version : vous pouvez vous arrêter et reprendre plus tard.

Un membre qui ne peut pas modifier les plans voit le message « Aucun plan de bâtiment pour ce niveau ».

![Niveau sans plan, vu par un lecteur](images/plans-absent.png)

## Importer le fond du plan {#plans-fond}

**Pour qui :** responsable, délégué, ou titulaire du droit sur les plans.

![Étape 1 : fond du plan](images/plans-guide-fond.png)

1. Préparez le fichier : un plan scanné, une photo ou un PDF. **Masquez d'abord les noms du cartouche** (personnes, entreprises).
2. Dans **Fichier**, choisissez le document : JPEG, PNG, HEIC ou PDF, 25 Mo au plus.
3. Pour un PDF, indiquez la **Page du PDF** (1 par défaut) et, si vous la connaissez, l'**Échelle 1:N du PDF** (par exemple 100 pour 1:100).
4. Cliquez sur **Importer ce fond**. Un PDF peut demander jusqu'à une minute.

Sans document, cliquez sur **Commencer sur une grille métrique** : le plan se dessinera sur une grille en mètres.

Si un fond a déjà été importé pour ce plan, il est proposé dans **Fonds déjà importés**.

**Erreurs fréquentes**

- « Fichier trop volumineux : 25 Mo au plus. »
- PDF protégé : enregistrez-le sans protection, puis importez-le de nouveau.
- Page absente : le message indique le nombre de pages du PDF.
- Quota atteint : l'organisation dispose d'un espace limité pour ses fonds de plan. Contactez l'équipe du projet.

## Calibrer l'échelle {#plans-calibrer}

La calibration indique à l'application combien mesure une longueur du fond. Elle est obligatoire pour un fond importé.

1. Touchez (ou cliquez) les deux extrémités d'une **longueur connue** sur le fond, idéalement plus de 10 mètres (la longueur de la nef). Zoomez pour pointer avec précision.
2. Saisissez la **Distance réelle de calibration** en mètres (par exemple 14,33).
3. Conseillé : choisissez **Cote de contrôle**, touchez les extrémités d'une autre longueur mesurée sur place, puis saisissez la **Longueur de contrôle mesurée sur place**. L'application affiche l'écart en pourcentage.
4. Cliquez sur **Valider l'échelle**. Le plan est enregistré en version 1 et l'atelier s'ouvre.

![Étape 2 : calibrer l'échelle avec une cote de contrôle](images/plans-calibration.png)

**À savoir**

- Au-delà de 2 % d'écart sur la cote de contrôle, un avertissement s'affiche : vérifiez vos points et vos mesures. Ce n'est pas un blocage.
- Pour un PDF à échelle déclarée, la distance est proposée d'après l'échelle ; confirmez-la par une cote mesurée.
- **Annuler** retire la dernière saisie. **Retour** revient au choix du fond.

**Erreurs fréquentes**

- « Touchez à l'intérieur du fond. »
- « Distance : saisissez un nombre de mètres, par exemple 14,33. »

## Travailler dans l'atelier {#plans-atelier}

Dès qu'un niveau a un plan, l'**atelier** s'ouvre : outils à gauche, plan au centre, propriétés à droite. En haut, le résumé de la version (numéro, date, auteur, échelle).

![L'atelier avec une cote mesurée](images/plans-atelier.png)

**Se déplacer dans le plan**

- À la souris ou au doigt : glissez pour déplacer la vue ; molette ou deux doigts pour zoomer.
- Avec les boutons : **+**, **−**, **Tout afficher** et les flèches.
- Au clavier, le plan sélectionné : flèches, **+**, **−**, **0** (tout afficher).

**Outils**

- **Sélection** (flèche) : déplacer la vue.
- **Cote** (double flèche) : touchez deux points pour mesurer une longueur. La mesure s'affiche sur le plan et dans le panneau (« Cote mesurée »).
- **Annuler** et **Rétablir** : aussi avec Ctrl+Z et Ctrl+Y.

**Panneau de droite :** fond, échelle et cote de contrôle du plan ; **Changer de fond** et **Recalibrer** (une nouvelle calibration donne une nouvelle version).

**Calque du niveau inférieur :** quand le niveau du dessous a un plan, le bouton **Calque … : masqué** l'affiche en pointillés sous le plan courant.

**Enregistrer :** le bouton **Enregistrer la version …** crée une nouvelle version. Chaque version est conservée.

Un membre qui consulte seulement voit le plan, le zoom et le calque, sans commande de modification.

## Brouillon et conflit {#plans-brouillon}

**Le brouillon.** Tant que vous n'avez pas enregistré, vos modifications sont gardées sur l'appareil, pour votre compte. Si la page se ferme, elles sont reprises à la prochaine ouverture (« Brouillon non enregistré repris »). **Abandonner le brouillon** les efface. La déconnexion efface aussi tous les brouillons de l'appareil.

**Le conflit.** Si quelqu'un d'autre a enregistré le même plan pendant que vous travailliez, votre enregistrement est refusé. Rien n'est écrasé : le message nomme l'autre auteur et sa version.

![Conflit : le brouillon est conservé](images/plans-conflit.png)

Deux choix :

- **Abandonner le brouillon** : vous rechargez la version de l'autre personne ;
- **Reprendre mon brouillon**, puis **Confirmer** : votre brouillon deviendra la prochaine version. Celle de l'autre personne reste dans l'historique.

Enregistrez ensuite avec **Enregistrer la version …**.

**Erreurs fréquentes**

- « Votre session a expiré : reconnectez-vous. Votre brouillon est conservé sur cet appareil. »
- « Ce niveau a déjà un plan, créé entre-temps : rechargez la page. »

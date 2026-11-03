# 1. Principes {#principes}

## À quoi sert gestionSite {#principes-objet}

gestionSite aide une église ou un réseau d'églises à entretenir ses lieux. L'application sert à :

- **recevoir les signalements** : un fidèle signale un problème avec le QR code affiché sur place, un membre de l'équipe le déclare dans l'application ;
- **suivre chaque dossier** jusqu'à sa clôture : qui s'en occupe, dans quel délai, ce qui a été fait ;
- **connaître le patrimoine** : bâtiments, pièces, équipements et matériel (l'inventaire) ;
- **mesurer l'état** des lieux avec une note sur 10 ;
- **organiser l'entretien préventif** et les vérifications périodiques ;
- **dessiner les plans** des bâtiments.

L'application n'est ni une alarme ni un système de sécurité incendie. En cas de danger immédiat, appelez toujours les secours (18 ou 112).

## Organisations et sites {#principes-organisations}

- Une **organisation** représente votre église ou votre réseau d'églises. Ses données sont séparées de celles des autres organisations.
- Une organisation compte un ou plusieurs **sites** : chaque lieu géographique (une église, un centre paroissial).
- Un site contient des **bâtiments**, découpés en **niveaux** et en **zones** (pièces ou espaces). Voir le chapitre [Inventaire](06-inventaire.md#inventaire).
- Vos droits sont donnés **site par site**. Vous ne voyez que les sites où vous avez un rôle.

> **Pas encore disponible.** Il n'y a pas d'écran pour créer un site. Les sites sont créés par l'équipe du projet à l'ouverture de l'organisation. Pour ajouter un site, contactez-la.

## Les rôles {#principes-roles}

Chaque membre reçoit un ou plusieurs rôles. Un rôle s'applique à un site, sauf l'administrateur qui couvre toute l'organisation.

| Rôle | Mission |
| --- | --- |
| **Administrateur** | Invite les membres, attribue les rôles, gère les sites (lien public, QR code, numéro d'urgence). |
| **Responsable** | Pilote l'entretien du site : qualifie et affecte les dossiers, planifie le préventif, adapte les grilles. |
| **Délégué** | Mêmes droits que le responsable sur son site. |
| **Technicien** | Réalise les interventions et les contrôles qui lui sont confiés, tient l'inventaire, évalue les composants. |
| **Pasteur** | Consulte le site, déclare des problèmes. |
| **Coordination** | Consulte le site, déclare des problèmes. |
| **Déclarant public** | N'a pas de compte. Signale un problème avec le QR code ou le lien public du site. |

## Qui peut faire quoi {#principes-droits}

| Action | Administrateur | Responsable, délégué | Technicien | Pasteur, coordination |
| --- | --- | --- | --- | --- |
| Inviter des membres | Oui | Non | Non | Non |
| Afficher le lien public, le QR code et l'affiche | Tous les sites | Ses sites | Non | Non |
| Saisir le numéro à appeler en cas de danger | Oui | Non | Non | Non |
| Consulter dossiers, inventaire, état du site, préventif, plans | Non (sauf s'il a aussi un rôle sur le site) | Oui | Oui | Oui |
| Déclarer un problème | Non (sauf rôle sur le site) | Oui | Oui | Oui |
| Qualifier, affecter, classer sans suite, rouvrir un dossier | Non | Oui | Non | Non |
| Démarrer, mettre en attente, clôturer une intervention | Non | Oui | Celles qui lui sont confiées | Non |
| Modifier l'inventaire, évaluer un composant | Non | Oui | Oui | Non |
| Adapter les grilles d'évaluation | Non | Oui | Non | Non |
| Planifier le préventif (classement ERP, plans, affectation) | Non | Oui | Non | Non |
| Faire un contrôle préventif | Non | Oui | Ceux qui lui sont affectés | Non |
| Modifier les plans de bâtiment | Non | Oui | Sur droit accordé | Sur droit accordé |

L'application vérifie toujours vos droits. Un bouton absent signifie que votre rôle ne permet pas l'action.

## Le menu {#principes-menu}

Le menu montre seulement les entrées utiles à vos rôles :

| Entrée | Qui la voit |
| --- | --- |
| **État du site**, **Dossiers**, **Inventaire**, **Plans**, **Préventif** | Membres ayant un rôle sur un site |
| **Mes interventions** | Technicien, responsable, délégué |
| **Grilles d'évaluation** | Responsable, délégué |
| **Sites** | Administrateur, responsable, délégué |
| **Membres** | Administrateur |
| **Se déconnecter** | Tout le monde |

Si vous avez des rôles sur plusieurs sites, des onglets en haut de chaque écran permettent de changer de site.

## Ordinateur, tablette et téléphone {#principes-appareils}

- gestionSite s'utilise dans un navigateur internet, avec une connexion.
- Sur **ordinateur**, le menu est à gauche.
- Sur **téléphone**, le menu passe en haut de l'écran. Les écrans de terrain (Mes interventions, évaluation, contrôle préventif) sont pensés pour le téléphone : une étape par écran, de grands boutons.
- Sur **tablette**, l'éditeur de plans se manipule au doigt : un doigt déplace la vue, deux doigts zooment.

> **Pas encore disponible.** L'application tablette qui fonctionne sans connexion est en préparation.

## Thèmes Jour et Nuit {#principes-themes}

L'application s'affiche en thème **Jour** (fond clair). Un thème **Nuit** (fond sombre) est prévu, avec un choix dans vos réglages, mémorisé pour votre compte.

> **Pas encore disponible.** Le choix du thème Nuit n'est pas encore proposé.

## Bonnes habitudes {#principes-habitudes}

- Une personne, un compte. Ne partagez jamais votre mot de passe.
- Sur un appareil partagé, cliquez toujours sur **Se déconnecter** en partant.
- Un état important (danger, retard) est toujours écrit en toutes lettres, pas seulement signalé par une couleur.
- Si deux personnes modifient la même fiche en même temps, l'application refuse la seconde modification et l'explique : rien n'est écrasé. Rechargez la fiche et recommencez.

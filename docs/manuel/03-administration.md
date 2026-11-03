# 3. Administration {#administration}

## Voir qui fait quoi {#administration-membres}

**Pour qui :** administrateur.

**Où cliquer :** menu **Membres**.

L'écran **Qui fait quoi, par site** liste les membres de l'organisation. Pour chaque membre, vous voyez :

- son nom et son adresse e-mail ;
- son rôle sur chaque site (une colonne par site), ou « Administrateur · toute l'organisation » ;
- son **État** : **Actif** (compte activé) ou **Invitation en attente** (lien pas encore utilisé).

![Liste des membres par site](images/membres.png)

> **Pas encore disponible.** Il n'y a pas encore d'écran pour modifier les rôles d'un membre, révoquer un accès ou consulter le journal des habilitations. Contactez l'équipe du projet si un changement est urgent.

## Inviter un membre {#administration-inviter}

**Pour qui :** administrateur.

**Où cliquer :** menu **Membres**, puis **Inviter un membre**.

L'invitation se fait en trois étapes.

1. **Personne** : saisissez le **Nom** et l'**Adresse e-mail** du nouveau membre, puis cliquez sur **Continuer**.

   ![Étape 1 : la personne](images/membres-inviter-1.png)

2. **Rôles par site** : choisissez son rôle sur chaque site (**Technicien**, **Responsable**, **Délégué**, **Pasteur** ou **Coordination**), ou laissez **Aucun rôle**. Pour un administrateur, cochez **Administrateur de toute l'organisation**. Cliquez sur **Créer l'invitation**.

   ![Étape 2 : les rôles par site](images/membres-inviter-2.png)

3. **Lien d'activation** : l'application affiche le lien. Copiez-le et transmettez-le vous-même à la personne (message, e-mail). Cliquez sur **Terminer**.

   ![Étape 3 : le lien d'activation à transmettre](images/membres-inviter-3.png)

**À savoir**

- Le lien n'est affiché qu'une fois. Il reste valable 72 heures.
- L'application n'envoie aucun e-mail : c'est vous qui transmettez le lien.
- Le nouveau membre apparaît avec l'état **Invitation en attente** jusqu'à l'activation de son compte.
- Si la personne a déjà un compte gestionSite, aucun lien n'est créé : le message indique que l'invitation l'attend dans son espace.
- Un technicien doit avoir activé son compte avant qu'on puisse lui confier une intervention.

**Erreurs fréquentes**

| Message | Que faire |
| --- | --- |
| « Le nom et l'adresse e-mail sont obligatoires. » | Remplissez les deux champs. |
| « Choisissez au moins un rôle, ou l'administration de toute l'organisation. » | Attribuez un rôle sur au moins un site. |
| « Cette personne est déjà membre ou invitée. » | Elle figure déjà dans la liste. |
| « Ce compte n'est pas encore activé : réessayez après son activation. » | La personne a un compte en attente d'activation : attendez qu'elle l'active. |
| « Adresse e-mail invalide. » | Corrigez l'adresse. |

## Afficher le lien public et le QR code d'un site {#administration-sites}

**Pour qui :** administrateur (tous les sites), responsable et délégué (leurs sites).

**Où cliquer :** menu **Sites**.

Chaque site a un **lien public** et un **QR code**. Ils permettent à n'importe qui de signaler un problème sans compte (voir le chapitre [Dépôt public](04-depot-public.md#depot)). Le formulaire public ne donne accès ni aux plans ni à l'inventaire.

Sur la carte du site, vous pouvez :

- **Imprimer l'affiche** : ouvre une affiche A4 prête à imprimer ;
- **Télécharger le QR** : enregistre l'image du QR code (pour un bulletin, un écran) ;
- **Copier le lien** : copie l'adresse du formulaire public.

![Écran Sites : QR code, lien public et numéro d'urgence](images/sites.png)

Sur les images de ce manuel, le QR code est volontairement flou.

## Saisir le numéro à appeler en cas de danger {#administration-numero-urgence}

**Pour qui :** administrateur seulement.

**Où cliquer :** menu **Sites**, champ **Numéro à appeler en cas de danger**.

1. Saisissez le numéro (chiffres, espaces, +, point, tiret ou parenthèses).
2. Cliquez sur **Enregistrer**. Le message « Numéro enregistré. » s'affiche.

Ce numéro est montré à la personne qui signale un danger avec le formulaire public, après le rappel d'appeler les secours (18 ou 112). Sans numéro, la mention « Non renseigné : aucune consigne affichée » apparaît.

Les responsables et délégués voient le numéro mais ne peuvent pas le modifier.

## Imprimer l'affiche du site {#administration-affiche}

**Pour qui :** administrateur, responsable, délégué.

1. Menu **Sites**, puis **Imprimer l'affiche** sur la carte du site. L'affiche s'ouvre dans un nouvel onglet.
2. Cliquez sur **Imprimer**. Choisissez le format A4, portrait.
3. Affichez-la à l'entrée et dans les lieux de passage.

L'affiche porte le nom de l'organisation et du site, le QR code, trois consignes et l'adresse du formulaire pour ceux qui n'ont pas de lecteur de QR code.

![Affiche A4 du site](images/affiche.png)

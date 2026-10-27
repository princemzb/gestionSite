# Synchronisation et travail hors connexion
Statut : V pour comportements utilisateur ; P pour protocole.

## Parcours
Connexion en ligne → préparation du profil et des interventions → téléchargement des ressources → session locale 24 h → commandes en file → reconnexion → contrôle des droits → envoi/reprise → application ou conflit → confirmation locale.

## Protocole proposé
Une commande porte operationId, deviceId, actorId, organisation, cible, baseVersion, instant observé et payload. Le serveur déduit l’identité autorisée de la session et vérifie les champs, sans leur faire confiance.
Résultats : applied, alreadyApplied, conflict, rejected. Une erreur métier n’efface pas une saisie locale ; elle devient récupérable par son auteur ou un administrateur selon procédure.
Les ajouts distincts fusionnent ; les changements contradictoires de statut/affectation/valeur créent un conflit. Sa résolution est une nouvelle décision tracée.
Après accusé durable, retirer de la file d’envoi ; maintenir les pièces tant qu’elles ne sont pas confirmées. Prévoir reconnexion avec reprise par curseur pour les mises à jour entrantes.

## Particularités
- Budget : proposer l’autorisation uniquement en ligne ; hors connexion, préparer la demande sans réserver un solde périmé.
- Stock : conserver les consommations même si leur somme révèle un écart.
- Photos : enregistrer localement avant annonce de succès ; signaler les fichiers encore en attente.
- Comptes partagés : files et données séparées ; ne pas envoyer sous le compte du collègue les commandes d’un autre auteur.
- Révocation : contrôle serveur à la synchronisation ; les anciennes saisies refusées restent récupérables sans les appliquer sous une autre identité.
- Position : pas de file historique de positions à renvoyer ; uniquement la position courante si le partage est encore actif.
- Plans : lier un constat à la version du plan utilisée ; conserver l’identifiant de zone malgré évolution géométrique.

## Validation
Couper réseau avant/après chaque étape, rejouer trois fois, modifier le même dossier sur deux appareils et tester expiration/revocation. La durée 24 h est un plafond d’accès retenu, pas une promesse de révocation instantanée hors connexion.


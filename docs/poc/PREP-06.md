# PREP-06 — Commandes hors connexion rejouables (POC expérimental)

Statut : **expérimental**, exécuté le 28 octobre 2026 sur macOS (API .NET 10 + PostgreSQL 17.6 locaux, client Dart/Flutter 3.47.1). Code jetable, désactivé par défaut (`Poc:Synchronisation:Active`), à remplacer par la synchronisation réelle (US-14). Protocole : docs/dev/SYNCHRONISATION.md.

## Ce qui est démontré
| Exigence (carte PREP-06) | Preuve automatique | Résultat |
| --- | --- | --- |
| Coupure après validation serveur | Réponse perdue après le commit : la commande reste en file, le renvoi identique répond `alreadyApplied` ; une seule écriture | OK |
| Doublon | Même opération envoyée 3 fois (et 10 fois en parallèle côté serveur) : 1 application, le reste `alreadyApplied` | OK |
| Deux profils | Files séparées par compte sur la même tablette ; une commande n'est jamais envoyée sous la session d'un collègue (refus `acteur_different`) | OK |
| Conflit | Deux appareils modifient la même note depuis la version 1 : le second reçoit `conflict` avec l'état serveur ; sa saisie reste sur l'appareil, rien n'est écrasé ; le rejeu redonne la même décision | OK |

Autres comportements vérifiés : file durable après redémarrage (écriture atomique, écritures sérialisées), ordre de saisie conservé sans réseau, commandes suivantes d'une note en conflit retenues jusqu'à l'arbitrage, session expirée (401), surcharge (429), page de portail captif ou délai dépassé traités comme réseau indisponible (rien n'est perdu), commande refusée gardée et récupérable, identifiant d'opération réutilisé avec un autre contenu refusé (409), note d'un autre site refusée (404), identifiants des appareils sans collision entre organisations, droits et isolation inchangés (403 sans rôle, 404 autre organisation), POC absent quand il est désactivé.

## Rejouer le rapport
- `scripts/poc/prep06.sh` : démarre la base Docker et l'API, ouvre une organisation de démonstration (commande d'exploitation), crée un site et deux techniciens, puis exécute le scénario Dart contre l'API (`mobile/test/poc/synchronisation_e2e_test.dart`). Sortie attendue : quatre lignes `RAPPORT | … — OK`.
- Tests automatiques sans réseau : `server/eng/verify.sh` (`PocSynchronisationTests`, 9 cas sur PostgreSQL réel) et `mobile/eng/verify.sh` (`test/poc/`, 15 cas).

## Conception retenue pour la suite
- Identifiant d'opération créé sur l'appareil avant tout envoi ; empreinte du contenu stockée avec le résultat : même opération = même résultat, contenu différent = refus.
- Une transaction par commande, organisation posée dans la transaction (RLS), note verrouillée (`FOR UPDATE`) puis comparaison avec la version attendue.
- Auteur déduit de la session, comparé à celui de la commande, jamais cru.
- Côté appareil : retrait de la file seulement après accusé `applied`/`alreadyApplied` ; conflit et refus conservés.

## Limites (non démontré ici)
- Fichiers de file **non chiffrés** et sans déverrouillage personnel : à démontrer avec POC-03 (ADR-03), condition de l'adoption du client tablette.
- Une seule ressource (texte d'une note) : pas de fusion des ajouts distincts, ni de photos/pièces, ni de mises à jour entrantes par curseur (SYNCHRONISATION.md).
- Pas d'interface d'arbitrage du conflit ni de commande de résolution tracée.
- Pas d'essai sur tablette réelle ni de coupure réseau physique : coupures simulées dans le transport ; la révocation et l'expiration de 24 h ne sont pas exercées.
- Tables du POC hors migrations EF (script idempotent) ; à supprimer avec le POC.

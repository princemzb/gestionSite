# Guide développeur et conventions
Statut : règles de travail proposées ; dépôt documentaire, aucun runtime applicatif choisi.

## Démarrage actuel
Lire README documentaire → PRD → règles/permissions → architecture → carte et contrats liés.
Aucune commande npm/dotnet/docker n’est disponible ou validée aujourd’hui. US-05 doit fournir README d’installation, versions, fichiers d’exemple sans secrets, fixtures et commandes réelles.

## Conventions
Identifiants métier stables ; séparation des modules ; validation et autorisation côté serveur ; erreurs structurées ; dates métier distinctes des dates de réception.
Toute mutation critique supporte reprise et concurrence. Ne pas appeler un fournisseur externe dans la transaction qui réserve le budget.
Les journaux techniques contiennent corrélation et résultat, pas photos/contacts/positions. Les fixtures sont synthétiques.
Les modifications de données sont migrées explicitement, avec compatibilité et procédure de restauration évaluées.

## Travail sur une carte
1. Vérifier état Prêt, dépendances, ADR et droits.
2. Préparer l’approche et les tests qui prouveront le résultat.
3. Implémenter le périmètre minimal complet ; ne pas élargir la story.
4. Exécuter tests ciblés puis intégration requise.
5. Mettre à jour contrats, règles affectées et documentation.
6. Soumettre PR avec problème, comportement final, preuves et limites.
7. Faire valider par une autre personne ; recette puis livraison distinctes.

## Reprise d’un agent
Fournir : carte et commit de référence, état du dépôt, modifications non livrées, résultats de tests exacts, décisions, blocages et prochaine commande/action.
Ne jamais écrire « testé » sans résultat réel. Un service simulé doit rester identifié comme tel.


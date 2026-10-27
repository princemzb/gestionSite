# Guide développeur et conventions
Statut : règles de travail proposées. Stack retenue par ADR-08 (C# / ASP.NET Core .NET 10, PostgreSQL, Flutter, Blazor) ; aucun code applicatif encore présent.

## Démarrage actuel
Lire README documentaire → PRD → règles/permissions → architecture → carte et contrats liés.
Aucune commande dotnet/flutter/docker n’est encore disponible ou validée. US-05 doit fournir README d’installation, versions, fichiers d’exemple sans secrets, fixtures et commandes réelles.

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
6. Travailler sur une branche ; soumettre une PR avec problème, comportement final, preuves et limites.
7. Faire revoir par agents IA selon ADR-09 (`/code-review` high, max pour les modules critiques, `/security-review` si applicable) ; corriger et relancer jusqu’à zéro constat confirmé.
8. Fusionner uniquement si CI, porte Sonar et revue sont au vert ; recette puis livraison distinctes.

## Reprise d’un agent
Fournir : carte et commit de référence, état du dépôt, modifications non livrées, résultats de tests exacts, décisions, blocages et prochaine commande/action.
Ne jamais écrire « testé » sans résultat réel. Un service simulé doit rester identifié comme tel.


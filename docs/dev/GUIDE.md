# Guide développeur et conventions
Statut : règles de travail proposées. Stack retenue par ADR-08 (C# / ASP.NET Core .NET 10, PostgreSQL, Flutter, Blazor) ; socle serveur en construction (US-05).

## Démarrage actuel
Lire README documentaire → PRD → règles/permissions → architecture → carte et contrats liés.
Serveur (`server/`, SDK défini par `global.json` à la racine : .NET 10.0.302 minimum, correctifs 10.0.3xx acceptés) — commandes vérifiées le 27 octobre 2026 :
- `dotnet build server/GestionSite.slnx` : avertissements et style bloquants (`Directory.Build.props`, `.editorconfig`).
- `dotnet test server/GestionSite.slnx` : tests d’intégration de l’API et tests d’architecture des modules.
- `dotnet format server/GestionSite.slnx --verify-no-changes` : mise en forme.
- `dotnet list server/GestionSite.slnx package --vulnerable --include-transitive` : dépendances vulnérables.
Porte complète en une commande : `server/eng/verify.sh` (identique à la CI). CI GitHub Actions et SonarQube Cloud (dépôt entier, C# et scripts) obligatoires sur `main`. Environnement local (S05-02), commandes vérifiées le 27 octobre 2026 :
- `docker compose -f server/compose.yaml up -d --wait` : PostgreSQL 17.6 (port 55433) et stockage compatible S3 SeaweedFS (port 58333, compartiment `gestionsite-local` créé au démarrage). Ports modifiables par `GESTIONSITE_POSTGRES_PORT` et `GESTIONSITE_STORAGE_PORT`.
- `dotnet run --project server/src/GestionSite.Api --launch-profile http` puis `curl http://localhost:5280/health` (vivacité) et `curl http://localhost:5280/health/ready` (PostgreSQL et stockage joignables ; 503 sinon).
- `docker compose -f server/compose.yaml down` pour arrêter ; ajouter `-v` pour effacer les données locales.
Les valeurs locales (mots de passe, clés) sont non sensibles et limitées à `appsettings.Development.json` et `compose.yaml` ; ailleurs, `ConnectionStrings:Main` et la section `ObjectStorage` viennent de l'environnement. Les tests d'intégration démarrent leurs propres conteneurs (Testcontainers, Docker requis).
Flutter arrive avec S05-03 ; le guide complet d’installation sera finalisé par S05-05.

Frontières des modules : un module `GestionSite.Modules.X` ne référence que des contrats `GestionSite.Modules.Y.Contracts` ou `GestionSite.SharedKernel`, jamais une autre implémentation, l’API ou un hôte ; les tests d’architecture le vérifient sur chaque fichier projet (donc sur toute chaîne de dépendances), interdisent les références injectées par fichiers partagés et contrôlent les assemblages compilés.

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


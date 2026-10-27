# Guide développeur et conventions
Statut : règles de travail proposées. Stack retenue par ADR-08 (C# / ASP.NET Core .NET 10, PostgreSQL, Flutter, Blazor) ; socle technique US-05 en place. Lire d'abord docs/pilotage/ETAT.md, puis README documentaire → PRD → règles/permissions → architecture → carte et contrats liés.

## Installation (S05-05)
Toutes les commandes ci-dessous ont été exécutées le 27 octobre 2026 sur macOS 14.5 (arm64). Lancer depuis la racine du dépôt.

### Prérequis
| Outil | Version | Rôle |
| --- | --- | --- |
| .NET SDK | 10.0.302 (fixée par `global.json`, correctifs 10.0.3xx acceptés) | Serveur |
| Docker (Desktop ou moteur) | 29.x | PostgreSQL et stockage S3 locaux, tests d'intégration |
| Flutter | 3.47.1 (fixée par `mobile/pubspec.yaml`) | Application terrain |
| SDK Android + `adb` | outils récents | Compilation et installation sur tablette |
| Python | 3.9 ou plus | Outillage Trello (`scripts/trello`) |
| `gh` (GitHub CLI) | 2.x | PR et CI |

### Première mise en route
1. `git clone https://github.com/princemzb/gestionSite.git && cd gestionSite`
2. Serveur : `server/eng/verify.sh` (restaure les outils, compile, vérifie le format, lance les tests, couverture ≥ 80 %, dépendances vulnérables).
3. Environnement local : `docker compose -f server/compose.yaml up -d --wait` (PostgreSQL 17.6 sur le port 55433, stockage SeaweedFS compatible S3 sur le port 58333, compartiment `gestionsite-local` créé au démarrage).
4. API : `dotnet run --project server/src/GestionSite.Api --launch-profile http`, puis `curl http://localhost:5280/health` (vivacité) et `curl http://localhost:5280/health/ready` (base et stockage joignables, 503 sinon ; résultat positif mis en cache 10 s).
5. Application terrain : `mobile/eng/verify.sh`, puis `cd mobile && flutter build apk --debug` et, tablette branchée en débogage USB, `adb install -r build/app/outputs/flutter-apk/app-debug.apk`.
6. Arrêt : `docker compose -f server/compose.yaml down` (ajouter `-v` pour effacer les données locales).

### Commandes unitaires
- `dotnet build server/GestionSite.slnx` : avertissements et style bloquants (`Directory.Build.props`, `.editorconfig`).
- `dotnet test server/GestionSite.slnx` : tests unitaires des modules, tests d'architecture et tests d'intégration (Testcontainers, Docker requis).
- `dotnet format server/GestionSite.slnx --verify-no-changes` : mise en forme.
- `dotnet list server/GestionSite.slnx package --vulnerable --include-transitive` : dépendances vulnérables.
- `python3 -m unittest discover -s scripts/trello` : outillage Trello.
- Application Flutter : voir `mobile/README.md`.

### Configuration et secrets
- Valeurs locales non sensibles, à modifier ensemble : `server/src/GestionSite.Api/appsettings.Development.json`, `server/compose.yaml`, `server/docker/seaweedfs-s3.json`, `server/tests/GestionSite.Api.Tests/InfrastructureFixture.cs`.
- Ailleurs, `ConnectionStrings:Main` et la section `ObjectStorage` viennent de l'environnement (validées au démarrage : l'API refuse de démarrer si elles sont absentes ou invalides).
- Ports locaux modifiables par `GESTIONSITE_POSTGRES_PORT` et `GESTIONSITE_STORAGE_PORT` ; fournir alors à l'API `ConnectionStrings__Main` et `ObjectStorage__ServiceUrl` avec les nouveaux ports.
- Jamais de secret dans le dépôt : jetons Trello et Sonar dans le Trousseau macOS du porteur ; jeton CI dans le secret GitHub `SONAR_TOKEN` ; signature Android dans `mobile/android/key.properties` (non versionné).

### Intégration continue
Six vérifications obligatoires sur `main` : « Serveur .NET (porte qualité) », « Outillage Trello (tests) », « SonarQube Cloud », « SonarCloud Code Analysis », « Application Flutter (analyse et tests) », « Application Flutter (dépendances vulnérables) ». Deux projets Sonar : `princemzb_gestionSite` (C#, scripts, CI) et `princemzb_gestionSite_mobile` (Dart et code natif).

### Frontières des modules
Un module `GestionSite.Modules.X` ne référence que des contrats `GestionSite.Modules.Y.Contracts` ou `GestionSite.SharedKernel`, jamais une autre implémentation, l'API ou un hôte ; les tests d'architecture le vérifient sur chaque fichier projet, interdisent les références injectées par fichiers partagés et contrôlent les assemblages compilés.

Aucun écran designé avant validation des maquettes par le porteur.

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


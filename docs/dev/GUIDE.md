# Guide développeur et conventions
Statut : règles de travail proposées. Stack retenue par ADR-08 (C# / ASP.NET Core .NET 10, PostgreSQL, Flutter, Blazor) ; socle technique US-05 en place. Lire d'abord docs/pilotage/ETAT.md, puis README documentaire → PRD → règles/permissions → architecture → carte et contrats liés.

## Installation (S05-05)
Toutes les commandes ci-dessous ont été exécutées le 27 octobre 2026 sur macOS 14.5 (arm64) ; celles de la section « Base de données et migrations » (`dotnet ef migrations add/list/script/has-pending-model-changes`, `database update`) le 28 octobre 2026. Lancer depuis la racine du dépôt.

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

### Base de données et migrations (S06-01, S06-02)
- Deux rôles PostgreSQL (`server/docker/postgres/roles-local.sql` en local) : `gestionsite_migrator`, propriétaire des schémas, applique les migrations (`ConnectionStrings:Migrations`) ; `gestionsite_app`, rôle de l'API (`ConnectionStrings:Main`), sans droit sur le schéma. Ni l'un ni l'autre n'est superutilisateur : la RLS (forcée) s'applique aux deux.
- Après la mise à jour S06-02, recréer la base locale pour créer ces rôles (données locales perdues) : `docker compose -f server/compose.yaml down -v && docker compose -f server/compose.yaml up -d --wait`.
- Isolation (ADR-10) : chaque requête définit `OrganisationCourante` ; le contexte EF filtre sur cette organisation et refuse d'écrire pour une autre ; chaque commande EF commence par `SET LOCAL app.organisation_id = …` dans le même lot (valable le temps de sa transaction, compatible avec un pooler en mode transaction), lue par les politiques RLS. Sans organisation : aucune ligne. L'organisation d'une route `/organizations/{orgId}` n'est définie qu'après vérification d'une adhésion active de l'appelant (sinon 404). Le SQL brut hors EF ne voit aucune donnée d'organisation.
- Migration de données : le rôle propriétaire est soumis à la RLS forcée sans organisation, donc un `UPDATE`/`DELETE` sur des tables isolées ne touche aucune ligne. Toute migration de données doit encadrer ses requêtes par `ALTER TABLE … NO FORCE ROW LEVEL SECURITY` / `FORCE ROW LEVEL SECURITY` dans la même migration, et être testée sur une base peuplée.
- Toute nouvelle table d'organisation : entité marquée `IAppartientAOrganisation` (filtre EF automatique), RLS activée et forcée avec politique dans sa migration, droits du rôle applicatif ; un test de catalogue échoue sinon.
- Chaque module a son schéma PostgreSQL et ses migrations EF Core (`src/Modules/<Module>/Persistence/Migrations`, code généré exclu du style et de la couverture).
- Créer une migration après un changement de modèle : `dotnet ef migrations add <Nom> --project server/src/Modules/GestionSite.Modules.Identite --startup-project server/src/GestionSite.Api --context IdentiteDbContext --output-dir Persistence/Migrations`. Un test échoue si une migration manque.
- Voir le SQL : `dotnet ef migrations script --project server/src/Modules/GestionSite.Modules.Identite --startup-project server/src/GestionSite.Api --context IdentiteDbContext` (sans `--output-dir`). `dotnet ef` construit l'hôte de l'API (environnement Development) : `migrations list` lit avec le rôle applicatif ; `database update` exige le rôle propriétaire : `dotnet ef database update --connection "<ConnectionStrings:Migrations>" …` (sans `--connection`, le rôle applicatif est refusé).
- Application automatique au démarrage si `Database:MigrateOnStartup` vaut `true` (profil `dotnet run` local et tests) ; une valeur non booléenne bloque le démarrage, et l'API refuse de démarrer si la migration est demandée sans `ConnectionStrings:Migrations` ou sur une base non locale (hôte autre que localhost, boucle locale ou socket Unix). En préproduction et production, l'étape de déploiement les appliquera (à mettre en place avec le premier déploiement). `/health/ready` répond 503 tant qu'une migration reste en attente.
- Prudence : `dotnet ef database update --connection …` n'a pas ce garde-fou ; ne jamais lui passer la chaîne propriétaire d'une base partagée hors de l'étape de déploiement.

### Authentification (S06-03)
- `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `GET /me` (CONTRATS). Jeton d'accès JWT HS256 15 min ; jeton de renouvellement aléatoire (32 octets), stocké en empreinte SHA-256, rotation à chaque usage, réutilisation d'un jeton remplacé = révocation de toute la lignée.
- Clé de signature `Authentification:CleSignature` (base64, ≥ 32 octets) : valeur locale non sensible dans `appsettings.Development.json`, fournie par l'environnement ailleurs ; l'API refuse de démarrer sans clé valide.
- Comptes ASP.NET Core Identity (tables `identite.comptes*`, hors organisation, sans RLS) : mot de passe ≥ 12 caractères sans règle de composition, verrouillage 15 min après 5 échecs.
- `/me` lit les adhésions du compte dans toutes ses organisations grâce aux politiques RLS `lecture_par_compte` (lecture seule, variable `app.compte_id`).

### Membres et ouverture d'organisation (S06-04)
- Ouvrir une organisation (exploitation, base déjà migrée) : `dotnet run --project server/src/GestionSite.Api -- ouvrir-organisation --nom "<organisation>" --admin-email <e-mail> --admin-nom "<nom>"`. Affiche l'identifiant et le jeton d'activation (72 h, usage unique) à transmettre à l'administrateur, qui l'utilise avec `POST /auth/activate`.
- L'administrateur gère sites et membres : `GET/POST /organizations/{orgId}/sites`, `POST /organizations/{orgId}/members` (jeton d'activation montré une seule fois, absent si le compte a déjà un mot de passe), `PUT …/members/{id}` avec la version attendue (409 si dépassée), `DELETE …/members/{id}` (adhésion révoquée : accès à l'organisation coupé aussitôt, jeton d'accès en cours compris ; les sessions du compte, qui peuvent servir d'autres organisations, restent ouvertes). L'organisation garde toujours un administrateur (409).
- Lien d'activation remis seulement pour un compte créé par l'appel, rattaché à l'organisation émettrice ; `POST …/members/{id}/activation` en émet un nouveau (lien expiré) si cette organisation a créé le compte. Un compte créé ailleurs et pas encore activé ne peut pas être ajouté (409 `compte_en_attente`) : sinon un administrateur tiers choisirait son mot de passe.
- Adhésion par invitation (ADR-10, complément du 28/10) : tout membre créé est d'abord invité, sans aucun droit. Compte créé par l'invitation : l'activation vaut acceptation. Compte existant : le titulaire voit l'invitation dans `GET /me` (`invitations`) et l'accepte ou la refuse (`POST /me/invitations/{memberId}/accept|decline`).
- Erreurs : ProblemDetails avec `code` stable (`introuvable` 404, `droit_insuffisant` 403, `version_depassee` / `dernier_administrateur` / `deja_membre` / `compte_en_attente` / `activation_impossible` 409, `lien_invalide` 410, `role_inconnu` / `site_inconnu` / `site_selon_role` / `mot_de_passe_invalide` 422).

### Journal des habilitations (S06-06, RG-31)
- Ouverture, création, révision, révocation, acceptation et refus d'une adhésion ajoutent une entrée `identite.journal_habilitations` (auteur, date, motif facultatif, rôles avant et après) dans la même transaction que le changement, datée comme lui. `motif` dans le corps (jamais dans l'URL) : obligatoire pour réviser et révoquer (RG-31), facultatif à la création. Une révision sans changement n'écrit rien. Lecture par pages (`?limite=&avant=<id>`, identifiants ordonnés dans le temps).
- Ajout seulement : le rôle applicatif n'a que SELECT et INSERT sur la table (test) ; RLS forcée par organisation. Lecture : `GET /organizations/{orgId}/members/journal` (administrateur).
- Journaux techniques : ni e-mail, ni mot de passe, ni jeton (test sur un parcours complet) ; les commandes SQL journalisées ne contiennent que des identifiants.

### Autorisation des opérations (S06-05)
- Tout module vérifie les droits par le contrat `GestionSite.Modules.Identite.Contracts.IAutorisations` : `VerifierAsync(orgId, Droit.X, siteId)` → `Autorise`, `Introuvable` (répondre 404 : non-membre, invitation non acceptée, site sans aucun rôle de l'appelant ou inconnu) ou `Refuse` (403 : site visible, rôle insuffisant). Quand l'appelant est membre actif, l'organisation devient courante (filtres EF et RLS).
- La matrice `MatriceDroits` reproduit docs/metier/PERMISSIONS.md ; un test en garde une copie ligne à ligne, à modifier dans la même PR que le document (le test ne lit pas le Markdown). L'administrateur n'a que `GererComptes` et `AutoriserSupport` : sans rôle métier, aucun droit sur les opérations. Les conditions propres à un module (technicien affecté, délégation budgétaire, relance interne du pasteur) restent vérifiées par ce module.
- Toute route `/organizations/...` exige l'authentification (test) et répond à l'identique pour une organisation étrangère et une organisation inexistante.

### Application web d'administration (WEB-01)
- `server/src/GestionSite.Web` : Blazor en rendu serveur statique (formulaires classiques, aucune connexion persistante), cliente de l'API. Écrans conformes aux maquettes validées (UX-02) : connexion, activation, membres par site avec invitation en 3 étapes ; jetons de la charte en variables CSS, thème Jour (Nuit prêt, bascule par compte à venir).
- Lancer en local : API (`dotnet run --project server/src/GestionSite.Api`, port 5280) puis web (`dotnet run --project server/src/GestionSite.Web`, http://localhost:5290). Configuration : `Web:ApiUrl` (adresse de l'API, qui doit figurer dans `AllowedHosts` de l'API) ; derrière un proxy TLS : `Web:UrlPublique` (adresse publique des liens d'activation) et `Web:RelaisHttps=true` (se fier au seul en-tête `X-Forwarded-Proto` du relais, qui doit être le seul à joindre le serveur ; l'adresse du client n'est pas reprise, donc non falsifiable par ce biais) ; `Web:DossierCles` (clés de chiffrement des cookies, à conserver entre redémarrages, sinon tout le monde est déconnecté). Cookie de session toujours `Secure` hors développement ; cookie antiforgery `Secure` dès que la requête d'origine est en https.
- Jetons de l'API gardés côté serveur web, dans un cookie chiffré HttpOnly (SameSite=Lax) ; renouvellement automatique, partagé deux minutes entre requêtes simultanées (deux onglets, double clic) pour ne pas déclencher la détection de vol ; déconnexion protégée par jeton anti-falsification. En-têtes : CSP stricte, `Referrer-Policy: no-referrer` (le jeton d'activation n'est pas transmis), `X-Content-Type-Options`.
- Ouvrir une organisation avec son premier site : `ouvrir-organisation … --site "<nom>"`.

### Configuration et secrets
- Valeurs locales non sensibles, à modifier ensemble : `server/src/GestionSite.Api/appsettings.Development.json`, `server/compose.yaml`, `server/docker/seaweedfs-s3.json`, `server/docker/postgres/roles-local.sql`, `server/tests/GestionSite.Api.Tests/InfrastructureFixture.cs` et `TestDatabase.cs`.
- Ailleurs, `ConnectionStrings:Main`, `ConnectionStrings:Migrations` (étape de migration seulement) et la section `ObjectStorage` viennent de l'environnement (validées au démarrage : l'API refuse de démarrer si elles sont absentes ou invalides).
- Ports locaux modifiables par `GESTIONSITE_POSTGRES_PORT` et `GESTIONSITE_STORAGE_PORT` ; fournir alors à l'API `ConnectionStrings__Main`, `ConnectionStrings__Migrations` et `ObjectStorage__ServiceUrl` avec les nouveaux ports (même base pour les deux chaînes).
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


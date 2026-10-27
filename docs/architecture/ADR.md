# Registre des décisions d’architecture
Statut : ADR-01 à ADR-07 proposées, non adoptées. ADR-08 et ADR-09 adoptées le 27 octobre 2026, sous réserve des preuves listées.

| ADR | Décision proposée | Alternatives et compromis | Condition de validation |
| --- | --- | --- | --- |
| ADR-01 | Backend modulaire unique | Microservices : plus d’exploitation ; monolithe sans frontières : couplage | Revue tech lead et frontières métier |
| ADR-02 | Base relationnelle et transactions | Stockage documentaire : contraintes croisées à traiter autrement | POC budget/concurrence |
| ADR-03 | Client tablette installé avec stockage local protégé | Web seul : compatibilité localisation à démontrer | Essais Android/iPad |
| ADR-04 | Commandes idempotentes et conflits explicites | Dernière écriture gagnante : incompatible avec arbitrage choisi | POC synchronisation |
| ADR-05 | Fournisseurs IA/marchands derrière adaptateurs | Couplage direct : remplacement difficile | Essais et contrat fournisseur |
| ADR-06 | Emission différée après transaction via journal d’événements à traiter | Appel direct : perte possible entre commit et envoi | Test panne et reprise |
| ADR-07 | Position temporaire hors historique métier | Stockage des trajets : contraire au choix utilisateur | Revue sécurité et effacement |

## Forme obligatoire pour une décision adoptée
Contexte, exigences concernées, options évaluées, preuves, choix et auteur approbateur, conséquences, coût, date, conditions de révision. Le choix de framework et d’hébergeur sera une ADR supplémentaire ; il n’est pas implicite dans ces documents.

## ADR-08 — Stack applicative et hébergement de préproduction
**Statut :** adoptée le 27 octobre 2026 par le porteur du projet (tech lead). Résout la partie « frameworks et hébergement de préproduction » de DEC-06.

**Contexte.** Un seul développeur assisté d’agents IA ; hors connexion 24 h avec données locales chiffrées par compte (NFR-02) ; commandes rejouables (NFR-03) ; réservations budgétaires atomiques (NFR-04) ; isolation organisation/site (NFR-01) ; accès capteurs pour la localisation (POC-01) ; tablettes Android et iPad, web PC, formulaire public sur téléphone accessible (NFR-12) ; dépenses minimales en préproduction.

**Choix.**
| Couche | Retenu | Alternatives écartées |
| --- | --- | --- |
| Backend | ASP.NET Core .NET 10 (LTS), monolithe modulaire, EF Core | NestJS, Supabase, Dart serveur |
| Base | PostgreSQL ; Row-Level Security envisagée en seconde barrière d’isolation | SQL Server (coût), SQLite/D1 (concurrence, isolation) |
| Tablette | Flutter (Android + iPad), SQLite chiffré par profil | .NET MAUI (écosystème BLE/3D), React Native, PWA (stockage iPad, capteurs) |
| Web admin et formulaire public | Blazor Web App .NET 10 ; formulaire public en rendu serveur statique | React (3e langage), Flutter Web (poids, accessibilité) |
| Préproduction | Cloudflare Containers (API + Blazor, offre Workers Paid existante) ; PostgreSQL géré Neon, région UE, projet dédié, via Hyperdrive | VPS UE + Docker Compose, PaaS français, Azure, Supabase (pause après inactivité) |
| Compléments | Cloudflare R2 (fichiers privés), Turnstile (anti-abus du formulaire public), Tunnel (accès tablette en développement) | Resend reporté à US-18 |

Langages à maintenir : C# et Dart. En local, Docker simule les services : PostgreSQL et stockage compatible S3 SeaweedFS (MinIO ne publiant plus d'image Docker communautaire, constat du 27/10/2026).

**Conséquences.**
- Les conteneurs Cloudflare ont un disque éphémère et se mettent en veille : aucune donnée durable dans le conteneur ; tâches différées (ADR-06) réveillées par déclenchements planifiés ; démarrage à froid de l’ordre de 1–3 s accepté en préproduction.
- Le mode interactif Blazor dépend d’une connexion persistante : à vérifier avec la mise en veille des conteneurs ; repli possible sur le rendu WebAssembly pour l’administration.
- L’image Docker reste portable : changer d’hébergeur ne remet pas en cause le code.
- La préproduction ne contient que des données synthétiques.

**Coût.** Préproduction attendue dans les quotas inclus : offre Workers Paid déjà souscrite (5 $/mois), offre gratuite Neon, 10 Go R2 gratuits. Non garanti : surveiller la consommation et activer une alerte de facturation Cloudflare.

**Preuves attendues et révision.** ADR-03 reste conditionnée à POC-03 (Flutter hors connexion, profils séparés) et à POC-01 (capteurs) ; un échec rouvre le choix du client tablette. Restent ouverts dans DEC-06 : versions minimales Android/iPadOS, appareils de référence, hébergement de production (avec DEC-07 et DEC-12). Réviser si les quotas Cloudflare/Neon sont dépassés ou si une contrainte de localisation des données s’impose.

## ADR-09 — Qualité du code, métriques et revue
**Statut :** adoptée le 27 octobre 2026 par le porteur du projet (tech lead).

**Contexte.** Développeur unique assisté d’agents IA ; exigence d’une architecture cohérente et d’un code de haute qualité, mesurés en continu jusqu’à la fin du projet. Les dépôts privés gratuits n’offrent ni protection de branche GitHub ni analyse Sonar des pull requests.

**Choix.**
- Dépôt GitHub rendu **public** le 27 octobre 2026 ; commits signés avec l’adresse noreply GitHub. Aucun secret ni donnée personnelle réelle dans le dépôt (AGENTS.md).
- **SonarQube Cloud** (gratuit pour un dépôt public) : analyse de `main` et de chaque pull request, C# et Dart.
- **Porte qualité stricte**, bloquante pour la fusion :
  - Sonar sur le nouveau code : notes A (fiabilité, sécurité, maintenabilité), 0 bug et 0 vulnérabilité, 100 % des points sensibles de sécurité revus, couverture ≥ 80 %, duplication < 3 % ;
  - compilation sans avertissement (C# `TreatWarningsAsErrors`, `dart analyze --fatal-infos`) ;
  - tests d’architecture des frontières de modules (ADR-01) ;
  - audit des dépendances vulnérables ;
  - tous les tests réussis en CI.
- **Revue par agents IA graduée**, avant chaque fusion :
  - `/code-review` niveau high pour toute PR de code ;
  - niveau max pour budget, stock, synchronisation, droits et isolation ;
  - `/security-review` pour authentification, fichiers et formulaire public ;
  - correction puis nouvelle revue jusqu’à zéro constat confirmé ; résultats en commentaire de PR et de carte Trello.
- **Protection de `main`** : pas de poussée directe ni forcée, fusion par pull request ; vérifications CI et Sonar requises dès leur création (US-05).

**Conséquences.** Tout le travail passe par branche et PR, documentation comprise. Une PR purement documentaire n’exige pas de revue de code ni de porte Sonar. Le cadrage est visible publiquement. La configuration Sonar (compte, organisation, jeton CI) est réalisée avec US-05, avant la première ligne de code applicatif.

**Révision.** Si un seuil bloque sans gain de qualité démontré, le modifier par une nouvelle décision motivée, jamais en contournant la porte.

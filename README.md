# gestionSite

SaaS de gestion du patrimoine et de la maintenance des églises : inventaire et état des bâtiments, signalements et interventions, préventif, stocks, budget, application terrain hors connexion sur tablettes.

**État :** cadrage terminé, socle technique en place ; aucune fonctionnalité métier livrée à ce stade.

- Reprendre le travail : [docs/pilotage/ETAT.md](docs/pilotage/ETAT.md)
- Dossier de cadrage : [docs/README.md](docs/README.md)
- Installation et commandes : [docs/dev/GUIDE.md](docs/dev/GUIDE.md)
- Décisions d'architecture : [docs/architecture/ADR.md](docs/architecture/ADR.md)

| Dossier | Contenu |
| --- | --- |
| `server/` | API ASP.NET Core .NET 10 (monolithe modulaire), environnement local Docker |
| `mobile/` | Application terrain Flutter (Android, iPad) |
| `scripts/` | Outillage de pilotage (Trello) |
| `docs/` | Cadrage, règles métier, architecture, pilotage, recette |

Qualité : CI GitHub Actions et SonarQube Cloud obligatoires avant toute fusion (ADR-09).

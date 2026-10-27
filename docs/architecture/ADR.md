# Registre des décisions d’architecture
Statut de toutes les entrées : proposées, non adoptées.

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


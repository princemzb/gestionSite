import 'commande.dart';
import 'file_commandes.dart';
import 'transport.dart';

/// Bilan d'un passage de synchronisation.
class Bilan {
  int appliquees = 0;
  int conflits = 0;
  int rejetees = 0;
  bool interrompu = false;
}

/// Envoie la file d'un profil, dans l'ordre, avec la session de CE profil :
/// une commande n'est jamais envoyée sous le compte d'un collègue.
/// - applied / alreadyApplied : accusé durable, la commande quitte la file ;
/// - conflict : conservée avec l'état serveur, pour arbitrage ;
/// - rejected : conservée, récupérable par son auteur ;
/// - réseau indisponible : arrêt, tout reste en file (renvoi identique plus tard).
class Synchroniseur {
  Synchroniseur(this.file, this.transport, this.jetonAcces);

  final FileCommandes file;
  final Transport transport;
  final String jetonAcces;

  Future<Bilan> synchroniser() async {
    final bilan = Bilan();
    for (final commande in file.enAttente) {
      if (commande.acteurId != file.acteurId) {
        continue; // défense en profondeur : la file n'en contient pas
      }

      final ReponseServeur reponse;
      try {
        reponse = await transport.envoyer(commande, jetonAcces);
      } on TransportIndisponible {
        bilan.interrompu = true;
        break;
      }

      switch (reponse.resultat) {
        case Resultat.applied:
        case Resultat.alreadyApplied:
          await file.retirer(commande.operationId);
          bilan.appliquees++;
        case Resultat.conflict:
          await file.remplacer(
            commande.avecEtat(
              EtatCommande.conflit,
              'Version serveur ${reponse.version} : « ${reponse.texteCourant ?? ''} »',
            ),
          );
          bilan.conflits++;
        case Resultat.rejected:
          await file.remplacer(
            commande.avecEtat(EtatCommande.rejetee, reponse.code),
          );
          bilan.rejetees++;
      }
    }

    return bilan;
  }
}

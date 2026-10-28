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
/// - réseau indisponible : arrêt, tout reste en file (renvoi identique plus tard) ;
/// - une note en conflit bloque les commandes suivantes sur la même note : elles
///   reposent sur une version que le serveur n'a pas, il faut d'abord arbitrer.
class Synchroniseur {
  Synchroniseur(this.file, this.transport, this.jetonAcces);

  final FileCommandes file;
  final Transport transport;
  final String jetonAcces;

  Future<Bilan> synchroniser() async {
    final bilan = Bilan();
    final bloquees = file.commandes
        .where((c) => c.etat == EtatCommande.conflit)
        .map((c) => c.noteId)
        .toSet();
    for (final commande in file.enAttente) {
      if (commande.acteurId != file.acteurId ||
          bloquees.contains(commande.noteId)) {
        continue; // autre auteur (défense en profondeur) ou note à arbitrer d'abord
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
          bloquees.add(commande.noteId);
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

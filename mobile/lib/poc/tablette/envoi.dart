/// POC-03 (S04-01, expérimental) : passage de synchronisation d'un profil.
///
/// 1. Contact : renouvellement de la session de CE profil, puis adhésions
///    relues ; le délai de 24 h repart, les organisations coupées sur cet
///    appareil sont effacées.
/// 2. File : dans l'ordre de saisie, photos d'abord (reprise à l'octet près,
///    même identifiant de fichier), puis la commande (même identifiant
///    d'opération) ; retrait de la file seulement après accusé durable.
library;

import 'dart:io';

import 'client_sync.dart';
import 'file_envoi.dart';
import 'plateforme.dart';
import 'profil.dart';

class BilanEnvoi {
  bool contact = false;
  bool interrompu = false;

  /// Session refusée : connexion par mot de passe nécessaire.
  String? sessionRefusee;
  int appliquees = 0;
  int conflits = 0;
  int rejetees = 0;
  int attentes = 0;

  /// Octets de photos envoyés pendant ce passage (mesure des reprises).
  int octetsEnvoyes = 0;

  @override
  String toString() =>
      'contact=$contact interrompu=$interrompu session=$sessionRefusee '
      'appliquees=$appliquees conflits=$conflits rejetees=$rejetees '
      'attentes=$attentes octets=$octetsEnvoyes';
}

class MoteurEnvoi {
  MoteurEnvoi(this.client, {this.tailleMorceau = 256 * 1024});

  final ClientSync client;
  final int tailleMorceau;

  Future<BilanEnvoi> synchroniser(ProfilTablette profil) async {
    final bilan = BilanEnvoi();
    final jetons = profil.jetons;
    if (jetons == null) {
      bilan.sessionRefusee = 'connexion_requise';
      return bilan;
    }

    final String acces;
    try {
      final nouveaux = await client.renouveler(jetons.renouvellement);
      // Rotation : le nouveau jeton est enregistré avant tout autre appel.
      profil.enregistrerJetons(nouveaux);
      final adhesions = await client.lireAdhesions(nouveaux.acces);
      await profil.enregistrerContact(
        Contact(
          serveurMs: adhesions.serveurMs,
          jetons: nouveaux,
          organisations: adhesions.organisations,
        ),
      );
      bilan.contact = true;
      acces = nouveaux.acces;
    } on ReseauIndisponible {
      bilan.interrompu = true;
      return bilan;
    } on SessionRefusee catch (refus) {
      await _sessionRefusee(profil, refus.code);
      bilan.sessionRefusee = refus.code;
      return bilan;
    }

    try {
      await _envoyerFile(profil, acces, bilan);
    } on ReseauIndisponible {
      bilan.interrompu = true;
    } on SessionRefusee catch (refus) {
      await _sessionRefusee(profil, refus.code);
      bilan.sessionRefusee = refus.code;
    }

    return bilan;
  }

  /// Révocation par le titulaire : consultation détruite. Autre refus (jeton
  /// expiré après 14 jours, rejeu détecté) : connexion requise, rien d'effacé.
  /// Dans les deux cas, les saisies restent en file pour le même compte.
  Future<void> _sessionRefusee(ProfilTablette profil, String code) async {
    if (code == 'session_revoquee') {
      await profil.revoquer();
    } else {
      profil.oublierSession();
    }
  }

  Future<void> _envoyerFile(
    ProfilTablette profil,
    String acces,
    BilanEnvoi bilan,
  ) async {
    final accessibles = profil.organisations;
    for (final commande in profil.file.enAttente) {
      if (commande.acteurId != profil.compteId) {
        continue; // défense en profondeur : jamais sous la session d'un collègue
      }

      if (!accessibles.contains(commande.organisationId)) {
        // Accès coupé sur cet appareil : conservée, récupérable, jamais envoyée.
        profil.file.marquer(
          commande.operationId,
          EtatEnvoi.rejetee,
          'acces_coupe',
        );
        bilan.rejetees++;
        continue;
      }

      for (final photo in profil.file.photosDe(commande.operationId)) {
        if (!photo.complete) {
          await _envoyerPhoto(profil.file, acces, photo, bilan);
        }
      }

      final reponse = await client.envoyerCommande(acces, commande);
      switch (reponse.resultat) {
        case Resultat.applied:
        case Resultat.alreadyApplied:
          profil.file.retirer(commande.operationId);
          bilan.appliquees++;
        case Resultat.conflict:
          profil.file.marquer(
            commande.operationId,
            EtatEnvoi.conflit,
            reponse.code,
          );
          bilan.conflits++;
        case Resultat.rejected:
          profil.file.marquer(
            commande.operationId,
            EtatEnvoi.rejetee,
            reponse.code,
          );
          bilan.rejetees++;
        case Resultat.fichiersManquants:
          // Une photo manque côté serveur (purgée ?) : elle sera renvoyée.
          for (final photo in profil.file.photosDe(commande.operationId)) {
            profil.file.marquerPhotoIncomplete(photo.fichierId);
          }
          bilan.attentes++;
      }
    }
  }

  /// Reprise : le serveur dit combien d'octets il a ; seule la suite part.
  Future<void> _envoyerPhoto(
    FileEnvoi file,
    String acces,
    PhotoEnAttente photo,
    BilanEnvoi bilan,
  ) async {
    var recu = await client.octetsRecus(acces, photo.fichierId);
    while (recu < photo.taille) {
      final fin = recu + tailleMorceau < photo.taille
          ? recu + tailleMorceau
          : photo.taille;
      final morceau = file.morceau(photo.fichierId, recu, fin);
      recu = await client.envoyerMorceau(
        acces,
        photo.fichierId,
        morceau,
        debut: recu,
        total: photo.taille,
        empreinte: photo.empreinte,
      );
      bilan.octetsEnvoyes += morceau.length;
    }
    file.marquerPhotoComplete(photo.fichierId);
  }
}

/// Configuration lue par l'envoi en arrière-plan (aucun secret).
class ConfigurationEnvoi {
  const ConfigurationEnvoi(this.serveur, this.appareilId);

  final Uri serveur;
  final String appareilId;
}

/// Envoie les files de TOUS les profils de la tablette, chacune sous la
/// session de son titulaire, sans rien afficher ni demander de PIN (T-02).
Future<Map<String, BilanEnvoi>> envoyerTousLesProfils({
  required Directory racine,
  required ConfigurationEnvoi configuration,
  required Coffre coffre,
  required HorlogeAppareil horloge,
  String origine = 'arriere-plan',
}) async {
  final bilans = <String, BilanEnvoi>{};
  final client = ClientSync(
    configuration.serveur,
    appareilId: configuration.appareilId,
    origine: origine,
  );
  try {
    for (final compteId in ProfilTablette.presents(racine)) {
      final profil = await ProfilTablette.charger(
        racine: racine,
        compteId: compteId,
        coffre: coffre,
        horloge: horloge,
      );
      try {
        bilans[compteId] = await MoteurEnvoi(client).synchroniser(profil);
      } finally {
        profil.fermer();
      }
    }
  } finally {
    client.fermer();
  }

  return bilans;
}

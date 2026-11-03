/// POC-03 (S04-01, expérimental) : saisie hors connexion et téléchargement de
/// l'instantané de consultation.
library;

import 'dart:typed_data';

import '../synchronisation/identifiants.dart';
import 'client_sync.dart';
import 'file_envoi.dart';
import 'plateforme.dart';
import 'profil.dart';

/// Enregistre une saisie (et ses photos réduites) dans la file du profil
/// ouvert, avant toute annonce de succès. Les identifiants d'opération et de
/// fichier sont créés ici, une fois : chaque renvoi porte les mêmes.
Future<CommandeTablette> saisir(
  ProfilTablette profil, {
  required String organisationId,
  required String type,
  required Map<String, Object?> corps,
  List<Uint8List> photos = const [],
  ReducteurPhoto? reducteur,
}) async {
  if (!profil.deverrouille) {
    throw const ProfilVerrouille();
  }

  final reduites = <Uint8List>[];
  for (final photo in photos) {
    reduites.add((await reducteur!.reduire(photo)).octets);
  }

  final heure = await profil.heureDeSaisie();
  final commande = CommandeTablette(
    operationId: nouvelIdentifiant(),
    acteurId: profil.compteId,
    organisationId: organisationId,
    type: type,
    corps: corps,
    saisieServeurMs: heure.serveurMs,
    heureGarantie: heure.garantie,
    fichiers: [for (final _ in reduites) nouvelIdentifiant()],
  );
  profil.file.ajouter(commande, photos: reduites);
  return commande;
}

/// Télécharge l'instantané de chaque organisation accessible (profil ouvert,
/// session valide). Renvoie le nombre d'éléments enregistrés.
Future<int> telechargerInstantanes(
  ProfilTablette profil,
  ClientSync client,
) async {
  final jetons = profil.jetons;
  if (jetons == null) {
    throw const SessionRefusee('connexion_requise');
  }

  var total = 0;
  for (final organisation in profil.organisations) {
    final elements = await client.instantane(jetons.acces, organisation);
    profil.remplacerInstantane(organisation, elements);
    total += elements.length;
  }

  return total;
}

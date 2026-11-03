/// POC-03 (S04-01, expérimental) : envoi en arrière-plan par WorkManager
/// (T-07) : au retour du réseau (contrainte « connecté ») et toutes les
/// 15 minutes au plus, même application fermée ou écran éteint.
library;

import 'dart:convert';
import 'dart:io';

import 'package:flutter/widgets.dart';
import 'package:workmanager/workmanager.dart';

import 'envoi.dart';
import 'plateforme.dart';

const tacheEnvoi = 'poc03-envoi';
const tachePeriodique = 'poc03-envoi-periodique';

File _fichierConfiguration(Directory racine) =>
    File('${racine.path}/configuration.json');

/// Adresse du serveur et identifiant d'appareil (aucun secret).
void ecrireConfiguration(Directory racine, ConfigurationEnvoi configuration) {
  racine.createSync(recursive: true);
  _fichierConfiguration(racine).writeAsStringSync(
    jsonEncode({
      'serveur': configuration.serveur.toString(),
      'appareilId': configuration.appareilId,
    }),
    flush: true,
  );
}

ConfigurationEnvoi? lireConfiguration(Directory racine) {
  final fichier = _fichierConfiguration(racine);
  if (!fichier.existsSync()) {
    return null;
  }

  final json = jsonDecode(fichier.readAsStringSync()) as Map<String, Object?>;
  return ConfigurationEnvoi(
    Uri.parse(json['serveur']! as String),
    json['appareilId']! as String,
  );
}

/// Travail d'un passage en arrière-plan ; `false` : WorkManager réessaiera
/// plus tard (délai croissant).
Future<bool> executerEnvoiArrierePlan(
  Directory racine, {
  required Coffre coffre,
  required HorlogeAppareil horloge,
}) async {
  final configuration = lireConfiguration(racine);
  if (configuration == null) {
    return true;
  }

  final bilans = await envoyerTousLesProfils(
    racine: racine,
    configuration: configuration,
    coffre: coffre,
    horloge: horloge,
  );
  return !bilans.values.any((b) => b.interrompu);
}

/// Dossier des profils du POC, dans le stockage privé exclu des sauvegardes.
Future<Directory> racineAppareil(PlateformeAndroid plateforme) async =>
    Directory('${await plateforme.dossier()}/poc03');

/// Point d'entrée du moteur Flutter démarré par WorkManager, sans activité.
@pragma('vm:entry-point')
void distributeurArrierePlan() {
  Workmanager().executeTask((tache, donnees) async {
    WidgetsFlutterBinding.ensureInitialized();
    const plateforme = PlateformeAndroid();
    return executerEnvoiArrierePlan(
      await racineAppareil(plateforme),
      coffre: plateforme,
      horloge: plateforme,
    );
  });
}

/// Programme un envoi dès que le réseau est là, et un passage périodique.
Future<void> programmerEnvoi() async {
  final reseau = Constraints(networkType: NetworkType.connected);
  await Workmanager().initialize(distributeurArrierePlan);
  await Workmanager().registerOneOffTask(
    tacheEnvoi,
    tacheEnvoi,
    constraints: reseau,
    existingWorkPolicy: ExistingWorkPolicy.replace,
    backoffPolicy: BackoffPolicy.exponential,
    backoffPolicyDelay: const Duration(seconds: 30),
  );
  await Workmanager().registerPeriodicTask(
    tachePeriodique,
    tachePeriodique,
    frequency: const Duration(minutes: 15),
    constraints: reseau,
    existingWorkPolicy: ExistingPeriodicWorkPolicy.keep,
  );
}

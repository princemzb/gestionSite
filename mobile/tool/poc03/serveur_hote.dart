// POC-03 (S04-01, expérimental) : serveur factice lancé sur le Mac pour les
// essais sur émulateur ou tablette (integration_test/poc03_test.dart).
//
// Usage : dart run tool/poc03/serveur_hote.dart [port]
// Variables : ANDROID_SERIAL (appareil visé par adb), ADB (chemin d'adb).
//
// Les routes /controle/adb permettent à l'essai, qui tourne SUR l'appareil, de
// demander au Mac une action qu'il ne peut pas faire lui-même : vraie coupure
// réseau (mode avion), écran éteint, retour à l'accueil, changement d'heure.
// Aucune donnée réelle : comptes et organisations de démonstration.
import 'dart:async';
import 'dart:io';

import '../../test/poc/tablette/serveur_factice.dart';

final String _adb =
    Platform.environment['ADB'] ??
    '${Platform.environment['HOME']}/Library/Android/sdk/platform-tools/adb';

Future<void> _shell(List<String> arguments) async {
  final resultat = await Process.run(_adb, ['shell', ...arguments]);
  stdout.writeln(
    'adb shell ${arguments.join(' ')} -> ${resultat.exitCode} '
    '${(resultat.stdout as String).trim()} ${(resultat.stderr as String).trim()}',
  );
}

int _entier(Map<String, Object?> options, String cle, int defaut) =>
    (options[cle] as int?) ?? defaut;

Future<void> _executer(String action, Map<String, Object?> options) async {
  await Future<void>.delayed(
    Duration(milliseconds: _entier(options, 'delaiMs', 0)),
  );
  stdout.writeln('${DateTime.now().toIso8601String()} action $action');
  switch (action) {
    case 'couper_reseau':
      await _shell(['cmd', 'connectivity', 'airplane-mode', 'enable']);
      await Future<void>.delayed(
        Duration(milliseconds: _entier(options, 'dureeMs', 10000)),
      );
      await _shell(['cmd', 'connectivity', 'airplane-mode', 'disable']);
    case 'ecran_eteint':
      await _shell(['input', 'keyevent', 'KEYCODE_SLEEP']);
    case 'reveiller':
      await _shell(['input', 'keyevent', 'KEYCODE_WAKEUP']);
      await _shell(['wm', 'dismiss-keyguard']);
      // Émulateur : code d'écran d'essai (POC03_CODE_ECRAN, jamais celui d'une
      // vraie tablette). Sans lui, la personne présente déverrouille à la main.
      final code = Platform.environment['POC03_CODE_ECRAN'];
      if (code != null) {
        await Future<void>.delayed(const Duration(seconds: 1));
        await _shell(['input', 'text', code]);
        await _shell(['input', 'keyevent', 'KEYCODE_ENTER']);
      }
    case 'accueil':
      await _shell(['input', 'keyevent', 'KEYCODE_HOME']);
    case 'ramener':
      await _shell([
        'am',
        'start',
        '-n',
        'fr.gestionsite.terrain/fr.gestionsite.gestionsite_terrain.MainActivity',
      ]);
    case 'heure':
      // Exige `adb root` (image google_apis) lancé AVANT l'essai.
      await _shell(['settings', 'put', 'global', 'auto_time', '0']);
      final cible = DateTime.now().add(
        Duration(milliseconds: _entier(options, 'decalageMs', 0)),
      );
      await _shell(['date', '@${cible.millisecondsSinceEpoch ~/ 1000}']);
    case 'heure_auto':
      final maintenant = DateTime.now();
      await _shell(['date', '@${maintenant.millisecondsSinceEpoch ~/ 1000}']);
      await _shell(['settings', 'put', 'global', 'auto_time', '1']);
    default:
      stdout.writeln('action inconnue : $action');
  }
}

Future<void> main(List<String> arguments) async {
  final port = arguments.isEmpty ? 8765 : int.parse(arguments.first);
  final serveur = ServeurFactice(pilotage: true, executerAdb: _executer);
  await serveur.demarrer(adresse: InternetAddress.anyIPv4, port: port);
  stdout.writeln('POC-03 : serveur factice prêt sur le port ${serveur.port}');
  final arret = Completer<void>();
  ProcessSignal.sigint.watch().listen((_) => arret.complete());
  await arret.future;
  for (final ligne in serveur.journal) {
    stdout.writeln(ligne);
  }
  await serveur.arreter();
}

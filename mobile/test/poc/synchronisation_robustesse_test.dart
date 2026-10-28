// POC PREP-06 (expérimental) : robustesse de la file et du transport.
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/synchronisation/commande.dart';
import 'package:gestionsite_terrain/poc/synchronisation/file_commandes.dart';
import 'package:gestionsite_terrain/poc/synchronisation/synchroniseur.dart';
import 'package:gestionsite_terrain/poc/synchronisation/transport.dart';

import 'synchronisation_test.dart' show ServeurSimule, marc, nouvelle;

void main() {
  late Directory dossier;

  setUp(() => dossier = Directory.systemTemp.createTempSync('prep06_r_'));
  tearDown(() => dossier.deleteSync(recursive: true));

  test('après un conflit, les modifications suivantes de la même note attendent l\'arbitrage', () async {
    final serveur = ServeurSimule()..notes['n1'] = (2, 'Modifiée ailleurs');
    final file = await FileCommandes.ouvrir(dossier, marc);
    await file.ajouter(nouvelle(marc, 'n1', 1, 'Ma modification'));
    await file.ajouter(nouvelle(marc, 'n1', 2, 'Suite de ma modification'));
    await file.ajouter(nouvelle(marc, 'n2', 0, 'Autre note'));

    final bilan = await Synchroniseur(
      file,
      serveur,
      'jeton-$marc',
    ).synchroniser();
    final ensuite = await Synchroniseur(
      file,
      serveur,
      'jeton-$marc',
    ).synchroniser();

    expect((bilan.conflits, bilan.appliquees), (1, 1));
    expect(serveur.notes['n1'], (
      2,
      'Modifiée ailleurs',
    )); // rien d'écrasé par la suite
    expect(file.enAttente.single.texte, 'Suite de ma modification');
    expect(
      file.commandes.where((c) => c.etat == EtatCommande.conflit),
      hasLength(1),
    );
    expect(ensuite.appliquees, 0);
  });

  test(
    'saisies et synchronisation simultanées : file cohérente sur disque',
    () async {
      final file = await FileCommandes.ouvrir(dossier, marc);
      final commandes = List.generate(
        20,
        (i) => nouvelle(marc, 'n$i', 0, 'Saisie $i'),
      );
      await Future.wait(commandes.map(file.ajouter));
      await Future.wait([
        for (final c in commandes.take(10)) file.retirer(c.operationId),
        file.ajouter(nouvelle(marc, 'n99', 0, 'Pendant la synchro')),
      ]);

      final relue = await FileCommandes.ouvrir(dossier, marc);

      expect(relue.commandes, hasLength(11));
      expect(
        relue.commandes.map((c) => c.texte),
        file.commandes.map((c) => c.texte),
      );
    },
  );

  test(
    'session expirée, surcharge ou page étrangère : la commande reste en file',
    () async {
      final serveur = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      final reponses = [
        (401, ''),
        (429, ''),
        (200, '<html>portail captif</html>'),
      ];
      serveur.listen((requete) async {
        await requete.drain<void>();
        final (statut, corps) = reponses.removeAt(0);
        requete.response
          ..statusCode = statut
          ..write(corps);
        await requete.response.close();
      });
      final transport = TransportHttp(
        Uri.parse('http://127.0.0.1:${serveur.port}'),
      );
      final file = await FileCommandes.ouvrir(dossier, marc);
      await file.ajouter(nouvelle(marc, 'n1', 0, 'Hors ligne'));

      for (var i = 0; i < 3; i++) {
        final bilan = await Synchroniseur(
          file,
          transport,
          'jeton',
        ).synchroniser();
        expect(bilan.interrompu, isTrue);
      }

      expect(file.enAttente.single.texte, 'Hors ligne');
      transport.fermer();
      await serveur.close(force: true);
    },
  );

  test('réponse trop lente : transport indisponible', () async {
    final lent = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    lent.listen((_) {}); // ne répond jamais
    final transport = TransportHttp(
      Uri.parse('http://127.0.0.1:${lent.port}'),
      delai: const Duration(milliseconds: 200),
    );

    await expectLater(
      transport.envoyer(nouvelle(marc, 'n1', 0, 'x'), 'jeton'),
      throwsA(isA<TransportIndisponible>()),
    );
    transport.fermer();
    await lent.close(force: true);
  });
}

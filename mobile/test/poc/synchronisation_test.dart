// POC PREP-06 (expérimental) : file de commandes hors connexion, côté tablette.
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/synchronisation/commande.dart';
import 'package:gestionsite_terrain/poc/synchronisation/file_commandes.dart';
import 'package:gestionsite_terrain/poc/synchronisation/identifiants.dart';
import 'package:gestionsite_terrain/poc/synchronisation/synchroniseur.dart';
import 'package:gestionsite_terrain/poc/synchronisation/transport.dart';

/// Serveur simulé, mêmes règles que l'API du POC : idempotence par opération,
/// version attendue, auteur imposé par la session.
class ServeurSimule implements Transport {
  final Map<String, ReponseServeur> operations = {};
  final Map<String, (int, String)> notes = {};
  final List<(String, String)> envois = []; // (opération, jeton)
  bool reseauCoupe = false;
  bool coupureApresValidation = false;

  @override
  Future<ReponseServeur> envoyer(Commande commande, String jetonAcces) async {
    if (reseauCoupe) {
      throw const TransportIndisponible('réseau coupé');
    }

    envois.add((commande.operationId, jetonAcces));
    if (jetonAcces != 'jeton-${commande.acteurId}') {
      return const ReponseServeur(Resultat.rejected, code: 'acteur_different');
    }

    final reponse = operations[commande.operationId] ?? _appliquer(commande);
    final rejeu = operations.containsKey(commande.operationId);
    operations[commande.operationId] = reponse;
    if (coupureApresValidation) {
      coupureApresValidation = false;
      throw const TransportIndisponible('réponse perdue après validation');
    }

    return rejeu && reponse.resultat == Resultat.applied
        ? ReponseServeur(Resultat.alreadyApplied, version: reponse.version)
        : reponse;
  }

  ReponseServeur _appliquer(Commande commande) {
    final (version, texte) = notes[commande.noteId] ?? (0, '');
    if (version != commande.baseVersion) {
      return ReponseServeur(
        Resultat.conflict,
        version: version,
        texteCourant: texte,
      );
    }

    notes[commande.noteId] = (version + 1, commande.texte);
    return ReponseServeur(Resultat.applied, version: version + 1);
  }
}

const String marc = '11111111-1111-4111-8111-111111111111';
const String lea = '22222222-2222-4222-8222-222222222222';
const String organisation = '33333333-3333-4333-8333-333333333333';
const String site = '44444444-4444-4444-8444-444444444444';

Commande nouvelle(String acteur, String note, int base, String texte) =>
    Commande(
      operationId: nouvelIdentifiant(),
      deviceId: 'tablette-1',
      acteurId: acteur,
      organisationId: organisation,
      siteId: site,
      noteId: note,
      baseVersion: base,
      texte: texte,
      observeLe: DateTime.utc(2026, 10, 28, 9),
    );

void main() {
  late Directory dossier;
  late ServeurSimule serveur;

  setUp(() {
    dossier = Directory.systemTemp.createTempSync('prep06_');
    serveur = ServeurSimule();
  });

  tearDown(() => dossier.deleteSync(recursive: true));

  test(
    'une réponse perdue après validation est récupérée par un renvoi identique',
    () async {
      final file = await FileCommandes.ouvrir(dossier, marc);
      await file.ajouter(nouvelle(marc, 'n1', 0, 'Fuite constatée'));
      serveur.coupureApresValidation = true;

      final premier = await Synchroniseur(
        file,
        serveur,
        'jeton-$marc',
      ).synchroniser();
      final second = await Synchroniseur(
        file,
        serveur,
        'jeton-$marc',
      ).synchroniser();

      expect(premier.interrompu, isTrue);
      expect(second.appliquees, 1);
      expect(file.commandes, isEmpty);
      expect(serveur.notes['n1'], (1, 'Fuite constatée'));
      expect(
        serveur.envois.map((e) => e.$1).toSet(),
        hasLength(1),
      ); // même opération envoyée deux fois
    },
  );

  test('la file survit à un redémarrage de l\'application', () async {
    final avant = await FileCommandes.ouvrir(dossier, marc);
    await avant.ajouter(nouvelle(marc, 'n1', 0, 'Saisie hors ligne'));

    final apres = await FileCommandes.ouvrir(dossier, marc);

    expect(apres.enAttente.single.texte, 'Saisie hors ligne');
  });

  test('renvoyer trois fois ne crée qu\'une modification', () async {
    final file = await FileCommandes.ouvrir(dossier, marc);
    final commande = nouvelle(marc, 'n1', 0, 'Doublon');
    for (var i = 0; i < 3; i++) {
      await serveur.envoyer(commande, 'jeton-$marc');
    }

    await file.ajouter(commande);
    final bilan = await Synchroniseur(
      file,
      serveur,
      'jeton-$marc',
    ).synchroniser();

    expect(bilan.appliquees, 1);
    expect(serveur.notes['n1'], (1, 'Doublon'));
  });

  test('deux profils d\'une même tablette ont des files séparées, jamais mélangées', () async {
    final fileMarc = await FileCommandes.ouvrir(dossier, marc);
    final fileLea = await FileCommandes.ouvrir(dossier, lea);
    await fileMarc.ajouter(nouvelle(marc, 'n1', 0, 'Marc'));
    await fileLea.ajouter(nouvelle(lea, 'n2', 0, 'Léa'));

    await Synchroniseur(fileMarc, serveur, 'jeton-$marc').synchroniser();

    expect(
      fileLea.enAttente.single.texte,
      'Léa',
    ); // pas envoyée avec la session de Marc
    expect(serveur.envois.every((e) => e.$2 == 'jeton-$marc'), isTrue);
    expect(
      () => fileMarc.ajouter(nouvelle(lea, 'n3', 0, 'Intrus')),
      throwsStateError,
    );
    expect(
      (await FileCommandes.ouvrir(dossier, lea)).enAttente.single.acteurId,
      lea,
    );
  });

  test(
    'deux appareils modifiant la même note : conflit explicite, rien d\'écrasé',
    () async {
      serveur.notes['n1'] = (1, 'Version initiale');
      final fileMarc = await FileCommandes.ouvrir(dossier, marc);
      final fileLea = await FileCommandes.ouvrir(dossier, lea);
      await fileMarc.ajouter(nouvelle(marc, 'n1', 1, 'Bâche posée'));
      await fileLea.ajouter(nouvelle(lea, 'n1', 1, 'Seau installé'));

      await Synchroniseur(fileMarc, serveur, 'jeton-$marc').synchroniser();
      final bilan = await Synchroniseur(
        fileLea,
        serveur,
        'jeton-$lea',
      ).synchroniser();

      expect(bilan.conflits, 1);
      expect(serveur.notes['n1'], (2, 'Bâche posée'));
      final conflit = fileLea.commandes.single;
      expect(conflit.etat, EtatCommande.conflit);
      expect(conflit.texte, 'Seau installé'); // la saisie de Léa est conservée
      expect(conflit.detail, contains('Bâche posée'));
    },
  );

  test(
    'une commande refusée reste récupérable et n\'est plus renvoyée',
    () async {
      final file = await FileCommandes.ouvrir(dossier, marc);
      await file.ajouter(nouvelle(marc, 'n1', 0, 'Refusée'));

      final bilan = await Synchroniseur(
        file,
        serveur,
        'jeton-autre',
      ).synchroniser();
      final ensuite = await Synchroniseur(
        file,
        serveur,
        'jeton-$marc',
      ).synchroniser();

      expect(bilan.rejetees, 1);
      expect(file.commandes.single.etat, EtatCommande.rejetee);
      expect(file.commandes.single.detail, 'acteur_different');
      expect(ensuite.appliquees, 0);
    },
  );

  test('sans réseau, tout reste en file dans l\'ordre de saisie', () async {
    final file = await FileCommandes.ouvrir(dossier, marc);
    await file.ajouter(nouvelle(marc, 'n1', 0, 'Un'));
    await file.ajouter(nouvelle(marc, 'n2', 0, 'Deux'));
    serveur.reseauCoupe = true;

    final bilan = await Synchroniseur(
      file,
      serveur,
      'jeton-$marc',
    ).synchroniser();

    expect(bilan.interrompu, isTrue);
    expect(file.enAttente.map((c) => c.texte), ['Un', 'Deux']);
  });

  test('identifiants d\'opération : UUID v4 distincts', () {
    final ids = List.generate(1000, (_) => nouvelIdentifiant());

    expect(ids.toSet(), hasLength(1000));
    expect(
      ids.every(
        RegExp(
          r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
        ).hasMatch,
      ),
      isTrue,
    );
  });

  test('un profil se désigne par un identifiant de compte', () {
    expect(
      () => FileCommandes.ouvrir(dossier, '../autre'),
      throwsArgumentError,
    );
  });

  group('transport HTTP', () {
    late HttpServer serveurHttp;
    late List<int> statuts;

    setUp(() async {
      statuts = [];
      serveurHttp = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      serveurHttp.listen((requete) async {
        final corps = jsonDecode(
          await utf8.decoder.bind(requete).join(),
        ) as Map<String, Object?>;
        final statut = statuts.removeAt(0);
        requete.response
          ..statusCode = statut
          ..headers.contentType = ContentType.json
          ..write(
            jsonEncode(switch (statut) {
              200 => {
                'operationId': corps['operationId'],
                'resultat': 'applied',
                'version': 1,
              },
              409 => {'code': 'operation_reutilisee'},
              _ => <String, Object?>{},
            }),
          );
        await requete.response.close();
      });
    });

    tearDown(() => serveurHttp.close(force: true));

    test('traduit les réponses du serveur', () async {
      final transport = TransportHttp(
        Uri.parse('http://127.0.0.1:${serveurHttp.port}'),
      );
      statuts.addAll([200, 409, 503]);
      final commande = nouvelle(marc, 'n1', 0, 'Texte');

      final applique = await transport.envoyer(commande, 'jeton');
      final rejete = await transport.envoyer(commande, 'jeton');

      expect((applique.resultat, applique.version), (Resultat.applied, 1));
      expect(
        (rejete.resultat, rejete.code),
        (Resultat.rejected, 'operation_reutilisee'),
      );
      await expectLater(
        transport.envoyer(commande, 'jeton'),
        throwsA(isA<TransportIndisponible>()),
      );
      transport.fermer();
    });

    test('serveur injoignable : transport indisponible', () async {
      final port = serveurHttp.port;
      await serveurHttp.close(force: true);
      final transport = TransportHttp(Uri.parse('http://127.0.0.1:$port'));

      await expectLater(
        transport.envoyer(nouvelle(marc, 'n1', 0, 'x'), 'jeton'),
        throwsA(isA<TransportIndisponible>()),
      );
      expect(
        const TransportIndisponible('cause').toString(),
        contains('cause'),
      );
      transport.fermer();
    });
  });
}

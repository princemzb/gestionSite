import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/base_chiffree.dart';
import 'package:gestionsite_terrain/poc/tablette/cryptographie.dart';
import 'package:gestionsite_terrain/poc/tablette/file_envoi.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';
import 'package:gestionsite_terrain/poc/tablette/profil.dart';
import 'package:gestionsite_terrain/poc/tablette/saisie.dart';
import 'package:sqlite3/sqlite3.dart';

import 'outils.dart';

const compteA = '0b6f8a39-6a7c-4c6e-9d43-6c2c1d1f0a01';
const compteB = '0b6f8a39-6a7c-4c6e-9d43-6c2c1d1f0a02';
const orgX = 'org-x';
const orgY = 'org-y';
const marqueur = 'MARQUEUR-POC03-CONFIDENTIEL';

Contact contactDemo({
  Set<String> organisations = const {orgX, orgY},
  int serveurMs = 1000,
}) => Contact(
  serveurMs: serveurMs,
  jetons: const Jetons('acces', 'renouvellement'),
  organisations: organisations,
);

void main() {
  late Directory racine;
  late CoffreLogiciel coffre;
  late HorlogeFactice horloge;

  setUp(() {
    racine = Directory.systemTemp.createTempSync('poc03_profil_');
    coffre = CoffreLogiciel();
    horloge = HorlogeFactice();
  });

  tearDown(() => racine.deleteSync(recursive: true));

  Future<ProfilTablette> preparer(String compte, {String pin = '123456'}) =>
      ProfilTablette.preparer(
        racine: racine,
        compteId: compte,
        pin: pin,
        contact: contactDemo(),
        coffre: coffre,
        horloge: horloge,
        parametres: argon2Test,
      );

  Future<ProfilTablette> recharger(String compte) => ProfilTablette.charger(
    racine: racine,
    compteId: compte,
    coffre: coffre,
    horloge: horloge,
  );

  Future<void> saisirCommentaire(ProfilTablette profil, String texte) => saisir(
    profil,
    organisationId: orgX,
    type: 'commentaire',
    corps: {'texte': texte},
  );

  test('préparation : deux clés Keystore, la consultation exige la tablette '
      'déverrouillée, la file non', () async {
    final profil = await preparer(compteA);

    expect(coffre.creations, {
      'poc03.$compteA.envoi': false,
      'poc03.$compteA.consultation': true,
    });
    expect(ProfilTablette.presents(racine), [compteA]);
    expect(profil.deverrouille, isFalse);
    expect(profil.organisations, {orgX, orgY});
    expect(profil.jetons!.renouvellement, 'renouvellement');
    profil.fermer();
  });

  test('base chiffrée : ni en-tête SQLite ni texte lisible, illisible sans clé '
      'ou avec la clé d\'un autre profil', () async {
    final a = await preparer(compteA);
    final b = await preparer(compteB, pin: '654321');
    expect(
      (await a.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.ouvert,
    );
    a.remplacerInstantane(orgX, [
      {'id': 'e1', 'siteId': 's1', 'contenu': marqueur},
    ]);
    await saisirCommentaire(a, marqueur);
    a.fermer();
    b.fermer();

    for (final nom in ['consultation_$orgX.db', 'envoi.db']) {
      final chemin = '${racine.path}/profils/$compteA/$nom';
      final octets = File(chemin).readAsBytesSync();
      expect(
        ascii.decode(octets.sublist(0, 15), allowInvalid: true),
        isNot('SQLite format 3'),
      );
      expect(latin1.decode(octets).contains(marqueur), isFalse);

      // Sans clé : SQLCipher refuse de lire.
      final sansCle = sqlite3.open(chemin);
      expect(
        () => sansCle.select('SELECT count(*) FROM sqlite_master'),
        throwsA(isA<SqliteException>()),
      );
      sansCle.close();
      // Avec une autre clé (autre profil : clé aléatoire différente).
      expect(
        () => ouvrirBaseChiffree(chemin, octetsAleatoires(32)),
        throwsA(isA<BaseIllisible>()),
      );
    }

    // La clé Keystore de B n'ouvre pas l'enveloppe de A.
    final enveloppeA = File('${racine.path}/profils/$compteA/consultation.cle')
        .readAsBytesSync();
    expect(
      () => coffre.dechiffrer('poc03.$compteB.consultation', enveloppeA),
      throwsA(isA<DechiffrementRefuse>()),
    );
  });

  test(
    'déverrouillage par PIN, verrouillage efface la clé et ferme les bases',
    () async {
      final profil = await preparer(compteA);
      expect(() => profil.consultation(orgX), throwsA(isA<ProfilVerrouille>()));

      final ouvert = await profil.deverrouiller('123456');
      expect(ouvert.resultat, ResultatDeverrouillage.ouvert);
      profil.remplacerInstantane(orgX, [
        {'id': 'e1', 'siteId': 's1', 'contenu': 'texte'},
      ]);
      expect(
        profil.consultation(orgX).select('SELECT * FROM elements'),
        hasLength(1),
      );
      expect(() => profil.consultation('org-inconnue'), throwsStateError);

      profil.verrouiller();
      expect(profil.deverrouille, isFalse);
      expect(() => profil.consultation(orgX), throwsA(isA<ProfilVerrouille>()));
      profil.fermer();
    },
  );

  test('5 PIN faux : profil bloqué, même le bon PIN est refusé, rien n\'est '
      'effacé ; reprise en ligne avec un nouveau PIN', () async {
    var profil = await preparer(compteA);
    await profil.deverrouiller('123456');
    await saisirCommentaire(profil, 'saisie avant blocage');
    profil.verrouiller();

    for (var restants = 4; restants >= 1; restants--) {
      final r = await profil.deverrouiller('000000');
      expect(r.resultat, ResultatDeverrouillage.pinFaux);
      expect(r.essaisRestants, restants);
    }
    expect(
      (await profil.deverrouiller('12a456')).resultat,
      ResultatDeverrouillage.bloque,
      reason: 'un PIN mal formé compte comme un essai',
    );
    expect(profil.bloque, isTrue);
    expect(
      (await profil.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.bloque,
    );

    // Le blocage survit à un redémarrage de l'application.
    profil.fermer();
    profil = await recharger(compteA);
    expect(profil.bloque, isTrue);
    expect(profil.file.commandes, hasLength(1));

    await profil.reprendreEnLigne(
      nouveauPin: '246810',
      contact: contactDemo(),
      parametres: argon2Test,
    );
    expect(profil.bloque, isFalse);
    expect(
      (await profil.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.pinFaux,
    );
    expect(
      (await profil.deverrouiller('246810')).resultat,
      ResultatDeverrouillage.ouvert,
    );
    expect(profil.file.commandes.single.corps['texte'], 'saisie avant blocage');
    profil.fermer();
  });

  test(
    'PIN oublié : instantané effacé et à retélécharger, saisies intactes',
    () async {
      final profil = await preparer(compteA);
      await profil.deverrouiller('123456');
      profil.remplacerInstantane(orgX, [
        {'id': 'e1', 'siteId': 's1', 'contenu': 'ancien'},
      ]);
      await saisirCommentaire(profil, 'à garder');
      profil.verrouiller();
      final ancienneCle = coffre.cles['poc03.$compteA.consultation'];

      await profil.reprendreEnLigne(
        nouveauPin: '135790',
        contact: contactDemo(),
        parametres: argon2Test,
      );

      expect(
        File('${racine.path}/profils/$compteA/consultation_$orgX.db')
            .existsSync(),
        isFalse,
      );
      expect(coffre.cles['poc03.$compteA.consultation'], isNot(ancienneCle));
      await profil.deverrouiller('135790');
      expect(
        profil.consultation(orgX).select('SELECT * FROM elements'),
        isEmpty,
      );
      expect(profil.file.commandes.single.corps['texte'], 'à garder');
      expect(
        () => profil.reprendreEnLigne(nouveauPin: '12', contact: contactDemo()),
        throwsArgumentError,
      );
      profil.fermer();
    },
  );

  test(
    '24 h depuis le dernier contact : consultation bloquée, saisies '
    'gardées ; l\'heure reculée n\'y change rien ; un contact relance',
    () async {
      final profil = await preparer(compteA);
      await profil.deverrouiller('123456');
      await saisirCommentaire(profil, 'saisie du matin');
      profil.verrouiller();

      horloge
        ..avancer(const Duration(hours: 23, minutes: 59))
        ..regler(const Duration(days: -3));
      expect(
        (await profil.deverrouiller('123456')).resultat,
        ResultatDeverrouillage.ouvert,
      );
      profil.verrouiller();

      horloge.avancer(const Duration(minutes: 1));
      expect(
        (await profil.deverrouiller('123456')).resultat,
        ResultatDeverrouillage.expire,
      );
      expect(profil.echecs, 0, reason: 'aucun essai de PIN consommé');
      expect(profil.file.commandes, hasLength(1));

      await profil.enregistrerContact(contactDemo());
      expect(
        (await profil.deverrouiller('123456')).resultat,
        ResultatDeverrouillage.ouvert,
      );
      profil.fermer();
    },
  );

  test(
    'heure de saisie : heure du serveur au contact + temps monotone écoulé',
    () async {
      final profil = await preparer(compteA);
      await profil.deverrouiller('123456');
      horloge
        ..avancer(const Duration(minutes: 10))
        ..regler(const Duration(hours: -5));
      final heure = await profil.heureDeSaisie();

      expect(
        heure.serveurMs,
        1000 + const Duration(minutes: 10).inMilliseconds,
      );
      expect(heure.garantie, isTrue);

      // Heure de la tablette avancée de 6 h : le délai raccourcit, mais
      // l'heure de saisie garantie reste celle du monotone.
      horloge.regler(const Duration(hours: 11));
      final avancee = await profil.heureDeSaisie();
      expect(
        avancee.serveurMs,
        1000 + const Duration(minutes: 10).inMilliseconds,
      );
      expect(avancee.garantie, isTrue);

      horloge.redemarrer(eteinte: const Duration(minutes: 1));
      expect((await profil.heureDeSaisie()).garantie, isFalse);
      profil.fermer();
    },
  );

  test('Keystore refusé (tablette verrouillée) : erreur remontée, aucun '
      'essai de PIN consommé', () async {
    final profil = await preparer(compteA);
    final cle = coffre.cles.remove('poc03.$compteA.consultation')!;
    for (var i = 0; i < 6; i++) {
      await expectLater(
        profil.deverrouiller('123456'),
        throwsA(isA<CleIntrouvable>()),
      );
    }
    expect(profil.echecs, 0);
    expect(profil.bloque, isFalse);

    coffre.cles['poc03.$compteA.consultation'] = cle;
    expect(
      (await profil.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.ouvert,
    );
    profil.fermer();
  });

  test('organisation coupée : sa base est effacée au contact, même profil '
      'verrouillé ; l\'autre organisation reste', () async {
    final profil = await preparer(compteA);
    await profil.deverrouiller('123456');
    profil
      ..remplacerInstantane(orgX, [
        {'id': 'x', 'siteId': 's', 'contenu': 'x'},
      ])
      ..remplacerInstantane(orgY, [
        {'id': 'y', 'siteId': 's', 'contenu': 'y'},
      ])
      ..verrouiller();

    await profil.enregistrerContact(contactDemo(organisations: {orgY}));

    final dossier = '${racine.path}/profils/$compteA';
    expect(File('$dossier/consultation_$orgX.db').existsSync(), isFalse);
    expect(File('$dossier/consultation_$orgY.db').existsSync(), isTrue);
    await profil.deverrouiller('123456');
    expect(() => profil.consultation(orgX), throwsStateError);
    expect(
      profil.consultation(orgY).select('SELECT * FROM elements'),
      hasLength(1),
    );
    profil.fermer();
  });

  test('session révoquée : clé de consultation détruite puis fichiers, jetons '
      'supprimés, saisies conservées', () async {
    final profil = await preparer(compteA);
    await profil.deverrouiller('123456');
    profil.remplacerInstantane(orgX, [
      {'id': 'x', 'siteId': 's', 'contenu': 'x'},
    ]);
    await saisirCommentaire(profil, 'saisie');

    await profil.revoquer();

    expect(profil.revoque, isTrue);
    expect(profil.deverrouille, isFalse);
    expect(profil.jetons, isNull);
    expect(coffre.cles.containsKey('poc03.$compteA.consultation'), isFalse);
    expect(
      Directory('${racine.path}/profils/$compteA')
          .listSync()
          .map((f) => f.uri.pathSegments.last)
          .where((n) => n.startsWith('consultation')),
      isEmpty,
    );
    expect(
      (await profil.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.revoque,
    );
    expect(profil.file.commandes, hasLength(1));

    await profil.reprendreEnLigne(
      nouveauPin: '111111',
      contact: contactDemo(),
      parametres: argon2Test,
    );
    expect(profil.revoque, isFalse);
    profil.fermer();
  });

  test('retrait du profil refusé tant que des saisies attendent', () async {
    final profil = await preparer(compteA);
    await profil.deverrouiller('123456');
    await saisirCommentaire(profil, 'en attente');

    await expectLater(profil.retirer(), throwsStateError);
    profil.file.retirer(profil.file.commandes.single.operationId);
    await profil.retirer();

    expect(ProfilTablette.presents(racine), isEmpty);
    expect(coffre.cles, isEmpty);
  });

  test('saisie refusée profil verrouillé ; photos enregistrées avec la '
      'commande, dans la même transaction', () async {
    final profil = await preparer(compteA);
    await expectLater(
      saisirCommentaire(profil, 'non'),
      throwsA(isA<ProfilVerrouille>()),
    );

    await profil.deverrouiller('123456');
    final commande = await saisir(
      profil,
      organisationId: orgX,
      type: 'photo',
      corps: const {},
      photos: [Uint8List.fromList(List.filled(1000, 7))],
      reducteur: _ReducteurIdentite(),
    );

    final photo = profil.file.photosDe(commande.operationId).single;
    expect(photo.fichierId, commande.fichiers.single);
    expect(photo.taille, 1000);
    expect(
      photo.empreinte,
      empreinteSha256(Uint8List.fromList(List.filled(1000, 7))),
    );
    expect(profil.file.morceau(photo.fichierId, 10, 20), List.filled(10, 7));
    expect(
      () => profil.file.ajouter(
        const CommandeTablette(
          operationId: 'x',
          acteurId: compteA,
          organisationId: orgX,
          type: 'photo',
          corps: {},
          saisieServeurMs: 0,
          heureGarantie: true,
          fichiers: ['f'],
        ),
      ),
      throwsArgumentError,
    );
    // Transaction annulée : un identifiant de fichier déjà pris ne laisse pas
    // de commande orpheline.
    expect(
      () => profil.file.ajouter(
        CommandeTablette(
          operationId: 'y',
          acteurId: compteA,
          organisationId: orgX,
          type: 'photo',
          corps: const {},
          saisieServeurMs: 0,
          heureGarantie: true,
          fichiers: [photo.fichierId],
        ),
        photos: [Uint8List(1)],
      ),
      throwsA(isA<SqliteException>()),
    );
    expect(profil.file.commandes, hasLength(1));
    profil.fermer();
  });

  test('identifiants et PIN contrôlés', () async {
    expect(
      () => ProfilTablette.dossierDu(racine, '../../etc'),
      throwsArgumentError,
    );
    await expectLater(preparer(compteA, pin: '12345'), throwsArgumentError);
    (await preparer(compteA)).fermer();
    await expectLater(preparer(compteA), throwsStateError);
    expect(
      ProfilTablette.presents(Directory('${racine.path}/absent')),
      isEmpty,
    );
    expect(const ProfilVerrouille().toString(), 'ProfilVerrouille');
    expect(const BaseIllisible('x').toString(), 'BaseIllisible: x');
  });
}

class _ReducteurIdentite implements ReducteurPhoto {
  @override
  Future<PhotoReduite> reduire(Uint8List octets) async =>
      PhotoReduite(octets, 1, 1);
}

// POC-03 (S04-01, expérimental) : essais sur Android réel (émulateur, puis
// tablette) avec le vrai Keystore, SQLCipher, WorkManager et de vraies
// coupures réseau. Le serveur factice tourne sur le Mac
// (tool/poc03/serveur_hote.dart) ; voir docs/poc/POC-03.md.
//
// flutter test integration_test/poc03_test.dart -d <appareil> \
//   --dart-define=POC03_SERVEUR=http://10.0.2.2:8765
//
// Chaque mesure est écrite sur une ligne « RAPPORT | … ».
import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/synchronisation/identifiants.dart';
import 'package:gestionsite_terrain/poc/tablette/arriere_plan.dart';
import 'package:gestionsite_terrain/poc/tablette/base_chiffree.dart';
import 'package:gestionsite_terrain/poc/tablette/client_sync.dart';
import 'package:gestionsite_terrain/poc/tablette/cryptographie.dart';
import 'package:gestionsite_terrain/poc/tablette/envoi.dart';
import 'package:gestionsite_terrain/poc/tablette/limite.dart';
import 'package:gestionsite_terrain/poc/tablette/photo_essai.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';
import 'package:gestionsite_terrain/poc/tablette/profil.dart';
import 'package:gestionsite_terrain/poc/tablette/saisie.dart';
import 'package:gestionsite_terrain/poc/tablette/verrou.dart';
import 'package:integration_test/integration_test.dart';
import 'package:sqlite3/sqlite3.dart';

const _serveur = String.fromEnvironment(
  'POC03_SERVEUR',
  defaultValue: 'http://10.0.2.2:8765',
);

/// `false` sur une tablette sans `adb root` : l'essai d'heure est alors manuel.
const _heureModifiable = bool.fromEnvironment(
  'POC03_HEURE',
  defaultValue: true,
);

const plateforme = PlateformeAndroid();
final serveur = Uri.parse(_serveur);
const marqueur = 'MARQUEUR-POC03-CONFIDENTIEL';

void rapport(String ligne) => debugPrint('RAPPORT | $ligne');

Future<Map<String, Object?>> controle(
  String action, [
  Map<String, Object?>? corps,
]) async {
  final client = HttpClient()..connectionTimeout = const Duration(seconds: 3);
  try {
    final requete = await client
        .openUrl(
          corps == null ? 'GET' : 'POST',
          serveur.resolve('/controle/$action'),
        )
        .timeout(const Duration(seconds: 5));
    if (corps != null) {
      requete.headers.contentType = ContentType.json;
      requete.write(jsonEncode(corps));
    }
    final reponse = await requete.close().timeout(const Duration(seconds: 5));
    final texte = await reponse.transform(utf8.decoder).join();
    return texte.isEmpty ? {} : jsonDecode(texte) as Map<String, Object?>;
  } finally {
    client.close(force: true);
  }
}

Future<void> adb(String action, [Map<String, Object?> options = const {}]) =>
    controle('adb', {'action': action, ...options});

/// Attend que le serveur réponde de nouveau (réseau rétabli).
Future<Duration> attendreReseau({
  Duration max = const Duration(seconds: 90),
}) async {
  final chrono = Stopwatch()..start();
  while (chrono.elapsed < max) {
    try {
      await controle('etat');
      return chrono.elapsed;
    } on Object {
      await Future<void>.delayed(const Duration(milliseconds: 500));
    }
  }
  throw TimeoutException('réseau non rétabli');
}

int mediane(List<int> valeurs) =>
    (List.of(valeurs)..sort())[valeurs.length ~/ 2];

/// Compte de démonstration créé sur le serveur factice.
Future<({String id, String email, String motDePasse})> compteDemo(
  List<String> organisations,
) async {
  final id = nouvelIdentifiant();
  final email = 'poc03-$id@demo.invalid';
  const motDePasse = 'mot-de-passe-de-demo';
  await controle('compte', {
    'id': id,
    'email': email,
    'motDePasse': motDePasse,
    'organisations': organisations,
  });
  return (id: id, email: email, motDePasse: motDePasse);
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  late Directory racine;
  late String appareilId;
  late ClientSync client;

  setUpAll(() async {
    racine = await racineAppareil(plateforme);
    if (racine.existsSync()) {
      racine.deleteSync(recursive: true);
    }
    racine.createSync(recursive: true);
    appareilId = 'emulateur-${nouvelIdentifiant()}';
    client = ClientSync(serveur, appareilId: appareilId);
  });

  tearDownAll(() => client.fermer());

  // Après un mode avion, le réseau de l'émulateur revient en 30 à 40 s : chaque
  // essai attend que le serveur réponde, pour ne pas hériter de la coupure.
  setUp(() => attendreReseau(max: const Duration(minutes: 3)));

  Future<ProfilTablette> preparer(
    ({String id, String email, String motDePasse}) compte, {
    String pin = '123456',
    Duration limite = limiteHorsConnexion,
  }) async {
    final jetons = await client.connecter(compte.email, compte.motDePasse);
    final adhesions = await client.lireAdhesions(jetons.acces);
    final profil = await ProfilTablette.preparer(
      racine: racine,
      compteId: compte.id,
      pin: pin,
      contact: Contact(
        serveurMs: adhesions.serveurMs,
        jetons: jetons,
        organisations: adhesions.organisations,
      ),
      coffre: plateforme,
      horloge: plateforme,
      limite: limite,
    );
    return profil;
  }

  testWidgets('appareil, Keystore et Argon2id', (tester) async {
    final infos = await plateforme.infos();
    rapport(
      'Appareil : ${infos.fabricant} ${infos.modele}, SDK ${infos.sdk}, '
      'verrouillage d\'écran : ${infos.appareilSecurise}, '
      'StrongBox déclarée : ${infos.strongBoxDeclaree}',
    );

    for (final exige in [true, false]) {
      final chrono = Stopwatch()..start();
      final niveau = await plateforme.creerCle(
        'poc03.essai.$exige',
        deverrouillageExige: exige,
      );
      final creation = chrono.elapsedMilliseconds;
      final durees = <int>[];
      for (var i = 0; i < 10; i++) {
        final t = Stopwatch()..start();
        final chiffre = await plateforme.chiffrer(
          'poc03.essai.$exige',
          octetsAleatoires(200),
        );
        await plateforme.dechiffrer('poc03.essai.$exige', chiffre);
        durees.add(t.elapsedMilliseconds);
      }
      rapport(
        'Clé Keystore (tablette déverrouillée exigée : $exige) : niveau '
        '${niveau.name}, création $creation ms, chiffrer + déchiffrer '
        'médiane ${mediane(durees)} ms',
      );
      await plateforme.supprimerCle('poc03.essai.$exige');
      await expectLater(
        plateforme.chiffrer('poc03.essai.$exige', Uint8List(1)),
        throwsA(isA<CleIntrouvable>()),
      );
    }

    final argon = <int>[];
    for (var i = 0; i < 5; i++) {
      final t = Stopwatch()..start();
      deriverCleDuPin('123456', octetsAleatoires(16), ParametresArgon2.owasp);
      argon.add(t.elapsedMilliseconds);
    }
    rapport(
      'Argon2id m=19 Mio t=2 p=1 : médiane ${mediane(argon)} ms ($argon)',
    );
  });

  testWidgets('profils chiffrés : PIN, fichiers illisibles, séparation', (
    tester,
  ) async {
    final a = await compteDemo(['org-x']);
    final b = await compteDemo(['org-x']);
    var chrono = Stopwatch()..start();
    final profilA = await preparer(a);
    rapport('Préparation d\'un profil : ${chrono.elapsedMilliseconds} ms');
    final profilB = await preparer(b, pin: '654321');

    final ouvertures = <int>[];
    for (var i = 0; i < 5; i++) {
      chrono = Stopwatch()..start();
      final r = await profilA.deverrouiller('123456');
      ouvertures.add(chrono.elapsedMilliseconds);
      expect(r.resultat, ResultatDeverrouillage.ouvert);
      profilA.consultation('org-x'); // ouverture SQLCipher comprise
      ouvertures[i] = chrono.elapsedMilliseconds;
      profilA.verrouiller();
    }
    rapport(
      'Déverrouillage par PIN (Keystore + Argon2id + SQLCipher) : médiane '
      '${mediane(ouvertures)} ms ($ouvertures)',
    );

    chrono = Stopwatch()..start();
    final faux = await profilA.deverrouiller('000000');
    rapport(
      'PIN faux détecté en ${chrono.elapsedMilliseconds} ms '
      '(${faux.resultat.name}, essais restants ${faux.essaisRestants})',
    );

    await profilA.deverrouiller('123456');
    await telechargerInstantanes(profilA, client);
    profilA.remplacerInstantane('org-x', [
      {'id': 'm', 'siteId': 's', 'contenu': marqueur},
    ]);
    await saisir(
      profilA,
      organisationId: 'org-x',
      type: 'commentaire',
      corps: {'texte': marqueur},
    );
    profilA.fermer();
    profilB.fermer();

    for (final nom in ['consultation_org-x.db', 'envoi.db']) {
      final chemin = '${racine.path}/profils/${a.id}/$nom';
      final octets = File(chemin).readAsBytesSync();
      final entete = latin1.decode(octets.sublist(0, 16));
      final enClair = latin1.decode(octets).contains(marqueur);
      final sansCle = sqlite3.open(chemin);
      var lisibleSansCle = true;
      try {
        sansCle.select('SELECT count(*) FROM sqlite_master');
      } on SqliteException {
        lisibleSansCle = false;
      }
      sansCle.close();
      var lisibleAutreCle = true;
      try {
        ouvrirBaseChiffree(chemin, octetsAleatoires(32)).close();
      } on BaseIllisible {
        lisibleAutreCle = false;
      }
      rapport(
        '$nom (${octets.length} o) : en-tête SQLite ${entete.startsWith('SQLite format 3')}, '
        'marqueur en clair $enClair, lisible sans clé $lisibleSansCle, '
        'lisible avec une autre clé $lisibleAutreCle',
      );
      expect(entete.startsWith('SQLite format 3'), isFalse);
      expect(enClair, isFalse);
      expect(lisibleSansCle, isFalse);
      expect(lisibleAutreCle, isFalse);
    }

    final enveloppeA = File('${racine.path}/profils/${a.id}/consultation.cle')
        .readAsBytesSync();
    await expectLater(
      plateforme.dechiffrer('poc03.${b.id}.consultation', enveloppeA),
      throwsA(isA<DechiffrementRefuse>()),
    );
    rapport(
      'Enveloppe du profil A refusée par la clé Keystore du profil B : OK',
    );
  });

  testWidgets('5 PIN faux puis PIN oublié : rien n\'est perdu', (tester) async {
    final a = await compteDemo(['org-x']);
    var profil = await preparer(a);
    await profil.deverrouiller('123456');
    await saisir(
      profil,
      organisationId: 'org-x',
      type: 'commentaire',
      corps: const {'texte': 'avant blocage'},
    );
    profil.verrouiller();
    for (var i = 0; i < 5; i++) {
      await profil.deverrouiller('111111');
    }
    final bloque = await profil.deverrouiller('123456');
    expect(bloque.resultat, ResultatDeverrouillage.bloque);

    // Redémarrage de l'application : toujours bloqué, file intacte.
    profil.fermer();
    profil = await ProfilTablette.charger(
      racine: racine,
      compteId: a.id,
      coffre: plateforme,
      horloge: plateforme,
    );
    expect(profil.bloque, isTrue);
    expect(profil.file.commandes, hasLength(1));

    final jetons = await client.connecter(a.email, a.motDePasse);
    final adhesions = await client.lireAdhesions(jetons.acces);
    await profil.reprendreEnLigne(
      nouveauPin: '246802',
      contact: Contact(
        serveurMs: adhesions.serveurMs,
        jetons: jetons,
        organisations: adhesions.organisations,
      ),
    );
    expect(
      (await profil.deverrouiller('246802')).resultat,
      ResultatDeverrouillage.ouvert,
    );
    expect(await telechargerInstantanes(profil, client), 1);
    expect(profil.file.commandes.single.corps['texte'], 'avant blocage');
    rapport(
      '5 PIN faux : bloqué (même avec le bon PIN, après redémarrage), file '
      'intacte ; reprise en ligne avec nouveau PIN, instantané retéléchargé : OK',
    );
    profil.fermer();
  });

  testWidgets('limite hors connexion (raccourcie à 8 s) et heure modifiée', (
    tester,
  ) async {
    final a = await compteDemo(['org-x']);
    final profil = await preparer(a, limite: const Duration(seconds: 8));
    await profil.deverrouiller('123456');
    await saisir(
      profil,
      organisationId: 'org-x',
      type: 'commentaire',
      corps: const {'texte': 'saisie hors connexion'},
    );
    profil.verrouiller();

    if (_heureModifiable) {
      // Heure reculée de 2 jours : le délai continue de courir.
      await adb('heure', {
        'decalageMs': -const Duration(days: 2).inMilliseconds,
      });
      await Future<void>.delayed(const Duration(seconds: 3));
      final recule = (await plateforme.lire()).muraleMs;
      rapport(
        'Heure de la tablette reculée : ${DateTime.fromMillisecondsSinceEpoch(recule)}',
      );
    }

    await Future<void>.delayed(const Duration(seconds: 9));
    final expire = await profil.deverrouiller('123456');
    final etat = await profil.etatLimite();
    rapport(
      'Après 9 s réelles (limite 8 s) : ${expire.resultat.name}, consommé '
      '${etat.consomme.inMilliseconds} ms, heure garantie ${etat.heureGarantie}',
    );
    expect(expire.resultat, ResultatDeverrouillage.expire);
    expect(profil.file.commandes, hasLength(1));

    if (_heureModifiable) {
      await adb('heure_auto');
      await Future<void>.delayed(const Duration(seconds: 3));
    }

    final bilan = await MoteurEnvoi(client).synchroniser(profil);
    expect(bilan.appliquees, 1);
    expect(
      (await profil.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.ouvert,
    );
    rapport('Contact réussi : saisie envoyée ($bilan), consultation rouverte');

    if (_heureModifiable) {
      profil.verrouiller();
      await adb('heure', {
        'decalageMs': const Duration(days: 2).inMilliseconds,
      });
      await Future<void>.delayed(const Duration(seconds: 3));
      final avance = await profil.deverrouiller('123456');
      rapport(
        'Heure avancée de 2 jours : ${avance.resultat.name} (raccourcit)',
      );
      expect(avance.resultat, ResultatDeverrouillage.expire);
      await adb('heure_auto');
      await Future<void>.delayed(const Duration(seconds: 3));
    }
    profil.fermer();
  });

  testWidgets('révocations : appareil par le titulaire, organisation par '
      'son administrateur', (tester) async {
    final a = await compteDemo(['org-coupee', 'org-gardee']);
    final profil = await preparer(a);
    await profil.deverrouiller('123456');
    expect(await telechargerInstantanes(profil, client), 2);
    await saisir(
      profil,
      organisationId: 'org-coupee',
      type: 'commentaire',
      corps: const {'texte': 'org-coupee'},
    );
    profil.verrouiller();

    await controle('couper-organisation', {
      'organisationId': 'org-coupee',
      'appareilId': appareilId,
    });
    final coupe = await MoteurEnvoi(client).synchroniser(profil);
    final dossier = '${racine.path}/profils/${a.id}';
    final xPresente = File('$dossier/consultation_org-coupee.db').existsSync();
    final yPresente = File('$dossier/consultation_org-gardee.db').existsSync();
    rapport(
      'Organisation coupée sur l\'appareil (profil verrouillé) : base org-coupee '
      'présente $xPresente, org-gardee présente $yPresente, saisie org-coupee '
      '${profil.file.commandes.single.etat.name}/${profil.file.commandes.single.detail}, '
      'session gardée ${profil.jetons != null} ($coupe)',
    );
    expect(xPresente, isFalse);
    expect(yPresente, isTrue);

    await controle('revoquer-appareil', {
      'compteId': a.id,
      'appareilId': appareilId,
    });
    final revoque = await MoteurEnvoi(client).synchroniser(profil);
    final restants = Directory(dossier)
        .listSync()
        .map((f) => f.uri.pathSegments.last)
        .where((n) => n.startsWith('consultation'))
        .toList();
    rapport(
      'Session de l\'appareil révoquée par le titulaire : ${revoque.sessionRefusee}, '
      'fichiers de consultation restants $restants, saisies gardées '
      '${profil.file.commandes.length}',
    );
    expect(revoque.sessionRefusee, 'session_revoquee');
    expect(restants, isEmpty);
    await expectLater(
      plateforme.dechiffrer('poc03.${a.id}.consultation', Uint8List(40)),
      throwsA(isA<CleIntrouvable>()),
    );
    expect(profil.file.commandes, hasLength(1));
    profil.fermer();
  });

  testWidgets('photo : réduction, EXIF retirés, envoi coupé par le mode '
      'avion puis repris sans doublon', (tester) async {
    final chrono = Stopwatch()..start();
    final source = await tester.runAsync(
      () => photoDEssai(largeur: 4000, hauteur: 3000),
    );
    rapport(
      'Photo d\'essai 4000×3000 générée : ${source!.length} o en '
      '${chrono.elapsedMilliseconds} ms',
    );
    final t = Stopwatch()..start();
    final reduite = await plateforme.reduire(source);
    rapport(
      'Réduction native : ${reduite.largeur}×${reduite.hauteur}, '
      '${reduite.octets.length} o JPEG, ${t.elapsedMilliseconds} ms',
    );
    expect([reduite.largeur, reduite.hauteur], [2560, 1920]);

    final avecGps = avecExif(reduite.octets);
    final nettoyee = await plateforme.reduire(avecGps);
    rapport(
      'EXIF orientation 6 + GPS : réorientée ${nettoyee.largeur}×'
      '${nettoyee.hauteur}, EXIF restant ${contientExif(nettoyee.octets)}',
    );
    expect([nettoyee.largeur, nettoyee.hauteur], [1920, 2560]);
    expect(contientExif(nettoyee.octets), isFalse);

    final a = await compteDemo(['org-x']);
    final profil = await preparer(a);
    await profil.deverrouiller('123456');
    final commande = await saisir(
      profil,
      organisationId: 'org-x',
      type: 'cloture',
      corps: const {'etatFinal': 'Bon'},
      photos: [source],
      reducteur: plateforme,
    );
    final taille = profil.file.photosDe(commande.operationId).single.taille;

    await controle('lenteur', {'ms': 1500});
    await adb('couper_reseau', {'delaiMs': 2500, 'dureeMs': 15000});
    final envoi = Stopwatch()..start();
    final coupe = await MoteurEnvoi(client).synchroniser(profil);
    rapport(
      'Pendant la coupure (après ${envoi.elapsedMilliseconds} ms) : $coupe',
    );
    expect(coupe.interrompu, isTrue);
    final retour = await attendreReseau();
    rapport(
      'Réseau rétabli, serveur joignable après ${retour.inMilliseconds} ms',
    );

    var etatServeur = await controle('etat');
    final partiel =
        (etatServeur['fichiers']!
                as Map<String, Object?>)[commande.fichiers.single]
            as Map<String, Object?>?;
    final reprise = await MoteurEnvoi(client).synchroniser(profil);
    await controle('lenteur', {'ms': 0});
    etatServeur = await controle('etat');
    final fichier =
        (etatServeur['fichiers']!
                as Map<String, Object?>)[commande.fichiers.single]!
            as Map<String, Object?>;
    final appliquees = (etatServeur['appliquees']! as List<Object?>)
        .cast<Map<String, Object?>>()
        .where((c) => c['operationId'] == commande.operationId)
        .length;
    rapport(
      'Photo de $taille o : reçus avant reprise ${partiel?['recu'] ?? 0}, '
      'reprise $reprise, fichier complet ${fichier['complet']} '
      '(${fichier['recu']} o, ${fichier['morceaux']} morceaux), commande '
      'appliquée $appliquees fois',
    );
    expect(reprise.appliquees, 1);
    expect(fichier['complet'], isTrue);
    expect(fichier['recu'], taille);
    expect(appliquees, 1);
    expect(reprise.octetsEnvoyes, taille - ((partiel?['recu'] as int?) ?? 0));
    profil.fermer();
  });

  testWidgets('verrouillage automatique : écran éteint, 30 s hors de '
      'l\'application', (tester) async {
    final motifs = <String>[];
    final ecran = plateforme.ecranEteint.asBroadcastStream();
    final delais = <int>[];
    final mesure = ecran.listen((instant) async {
      delais.add((await plateforme.lire()).monotoneMs - instant);
    });
    final verrou = VerrouAutomatique(
      horloge: plateforme,
      ecranEteint: ecran,
      verrouiller: motifs.add,
    );
    final observateur = _Observateur(verrou);
    WidgetsBinding.instance.addObserver(observateur);
    await tester.runAsync(() async {
      await verrou.demarrer();
      final chrono = Stopwatch()..start();
      await adb('ecran_eteint');
      // L'émulateur chargé peut mettre des dizaines de secondes à exécuter la
      // commande adb ; le délai utile est celui entre la diffusion et le verrou.
      while (motifs.isEmpty && chrono.elapsed < const Duration(seconds: 90)) {
        await Future<void>.delayed(const Duration(milliseconds: 100));
      }
      await Future<void>.delayed(const Duration(milliseconds: 500));
      rapport(
        'Écran éteint : verrouillage « ${motifs.join()} » ; délai entre la '
        'diffusion système et la réception dans Dart : $delais ms '
        '(demande adb comprise : ${chrono.elapsedMilliseconds} ms)',
      );
      expect(motifs, ['ecran_eteint']);
      await adb('reveiller');
      await Future<void>.delayed(const Duration(seconds: 3));
      await adb('ramener');
      await Future<void>.delayed(const Duration(seconds: 3));

      motifs.clear();
      await verrou.demarrer();
      await adb('accueil');
      final dehors = Stopwatch()..start();
      while (motifs.isEmpty && dehors.elapsed < const Duration(seconds: 90)) {
        await Future<void>.delayed(const Duration(milliseconds: 200));
      }
      await adb('ramener');
      await Future<void>.delayed(const Duration(seconds: 3));
      rapport(
        'Hors de l\'application : verrouillage « ${motifs.join()} » '
        '${dehors.elapsedMilliseconds} ms après la demande « accueil » '
        '(minuterie de 30 s)',
      );
    });
    WidgetsBinding.instance.removeObserver(observateur);
    await verrou.fermer();
    await mesure.cancel();
    expect(motifs, ['hors_application']);
  });

  testWidgets('envoi en arrière-plan par WorkManager après retour du réseau, '
      'application quittée et écran éteint', (tester) async {
    final a = await compteDemo(['org-x']);
    final profil = await preparer(a);
    await profil.deverrouiller('123456');
    final commande = await saisir(
      profil,
      organisationId: 'org-x',
      type: 'commentaire',
      corps: const {'texte': 'envoyée en arrière-plan'},
    );
    profil.fermer();
    ecrireConfiguration(racine, ConfigurationEnvoi(serveur, appareilId));

    await tester.runAsync(() async {
      await adb('couper_reseau', {'delaiMs': 0, 'dureeMs': 20000});
      await Future<void>.delayed(const Duration(seconds: 4));
      await programmerEnvoi();
      await adb('accueil');
      await adb('ecran_eteint', {'delaiMs': 1000});
      final debut = DateTime.now();
      final chrono = Stopwatch()..start();
      Map<String, Object?>? recue;
      while (recue == null && chrono.elapsed < const Duration(minutes: 4)) {
        await Future<void>.delayed(const Duration(seconds: 2));
        try {
          final etat = await controle('etat');
          recue = (etat['appliquees']! as List<Object?>)
              .cast<Map<String, Object?>>()
              .where((c) => c['operationId'] == commande.operationId)
              .firstOrNull;
        } on Object {
          // réseau encore coupé
        }
      }
      rapport(
        'WorkManager : commande reçue ${recue != null} (origine '
        '${recue?['origine']}) ${chrono.elapsed.inSeconds} s après la mise en '
        'arrière-plan (coupure de 20 s comprise ; début $debut)',
      );
      expect(recue?['origine'], 'arriere-plan');
      await adb('reveiller');
      await Future<void>.delayed(const Duration(seconds: 2));
      await adb('ramener');
      await Future<void>.delayed(const Duration(seconds: 2));
    });
  });
}

class _Observateur with WidgetsBindingObserver {
  _Observateur(this.verrou);

  final VerrouAutomatique verrou;

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    unawaited(verrou.cycleDeVie(state));
  }
}

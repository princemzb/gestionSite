import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/arriere_plan.dart';
import 'package:gestionsite_terrain/poc/tablette/client_sync.dart';
import 'package:gestionsite_terrain/poc/tablette/envoi.dart';
import 'package:gestionsite_terrain/poc/tablette/file_envoi.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';
import 'package:gestionsite_terrain/poc/tablette/profil.dart';
import 'package:gestionsite_terrain/poc/tablette/saisie.dart';

import 'outils.dart';
import 'serveur_factice.dart';

const compteA = '0b6f8a39-6a7c-4c6e-9d43-6c2c1d1f0a01';
const compteB = '0b6f8a39-6a7c-4c6e-9d43-6c2c1d1f0a02';
const appareil = 'appareil-poc03';
const orgX = 'org-x';
const orgY = 'org-y';

class _ReducteurIdentite implements ReducteurPhoto {
  @override
  Future<PhotoReduite> reduire(Uint8List octets) async =>
      PhotoReduite(octets, 1, 1);
}

Uint8List _photo(int taille, int graine) =>
    Uint8List.fromList(List<int>.generate(taille, (i) => (i * graine) & 0xff));

void main() {
  late Directory racine;
  late CoffreLogiciel coffre;
  late HorlogeFactice horloge;
  late ServeurFactice serveur;
  late ClientSync client;

  setUp(() async {
    racine = Directory.systemTemp.createTempSync('poc03_envoi_');
    coffre = CoffreLogiciel();
    horloge = HorlogeFactice();
    serveur = ServeurFactice()
      ..ajouterCompte('a@demo.invalid', compteA, 'mot-de-passe-a')
      ..ajouterCompte('b@demo.invalid', compteB, 'mot-de-passe-b')
      ..ajouterAdhesion(compteA, orgX)
      ..ajouterAdhesion(compteA, orgY)
      ..ajouterAdhesion(compteB, orgX)
      ..definirInstantane(orgX, [
        {'id': 'e1', 'siteId': 's1', 'contenu': 'Tableau électrique TGBT'},
      ]);
    await serveur.demarrer();
    client = ClientSync(serveur.adresse, appareilId: appareil);
  });

  tearDown(() async {
    client.fermer();
    await serveur.arreter();
    racine.deleteSync(recursive: true);
  });

  /// Préparation en ligne : connexion, adhésions, profil, instantané.
  Future<ProfilTablette> preparer(
    String compte,
    String email,
    String mdp,
  ) async {
    final jetons = await client.connecter(email, mdp);
    final adhesions = await client.lireAdhesions(jetons.acces);
    final profil = await ProfilTablette.preparer(
      racine: racine,
      compteId: compte,
      pin: '123456',
      contact: Contact(
        serveurMs: adhesions.serveurMs,
        jetons: jetons,
        organisations: adhesions.organisations,
      ),
      coffre: coffre,
      horloge: horloge,
      parametres: argon2Test,
    );
    await profil.deverrouiller('123456');
    await telechargerInstantanes(profil, client);
    return profil;
  }

  Future<CommandeTablette> saisirPhotos(
    ProfilTablette profil,
    List<Uint8List> photos,
  ) => saisir(
    profil,
    organisationId: orgX,
    type: 'cloture',
    corps: {'etatFinal': 'Bon'},
    photos: photos,
    reducteur: _ReducteurIdentite(),
  );

  test('préparation en ligne : instantané téléchargé et chiffré', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');

    expect(profil.organisations, {orgX, orgY});
    final lignes = profil.consultation(orgX).select('SELECT * FROM elements');
    expect(lignes.single['id'], 'e1');
    profil.fermer();
  });

  test('coupure pendant la 2e photo sur 3 : reprise à l\'octet, aucun '
      'doublon, la commande attend ses photos', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    final photos = [_photo(300000, 3), _photo(700000, 5), _photo(200000, 7)];
    final commande = await saisirPhotos(profil, photos);
    final moteur = MoteurEnvoi(client, tailleMorceau: 100000);

    // Coupure au morceau qui franchit 300 000 + 350 000 octets : 2e photo.
    serveur.couperApres(650000);
    final premier = await moteur.synchroniser(profil);
    expect(premier.interrompu, isTrue);
    expect(serveur.appliquees, isEmpty, reason: 'jamais appliquée à moitié');
    expect(serveur.fichiers[commande.fichiers[0]]!.complet, isTrue);
    expect(serveur.fichiers[commande.fichiers[1]]!.complet, isFalse);
    expect(serveur.fichiers[commande.fichiers[1]]!.octets.length, 300000);
    expect(profil.file.enAttente, hasLength(1));

    final second = await moteur.synchroniser(profil);
    expect(second.appliquees, 1);
    // Seule la suite de la 2e photo et la 3e partent : 400 000 + 200 000.
    expect(second.octetsEnvoyes, 600000);
    expect(serveur.appliquees, hasLength(1));
    for (var i = 0; i < 3; i++) {
      final recu = serveur.fichiers[commande.fichiers[i]]!;
      expect(recu.complet, isTrue);
      expect(recu.octets.toBytes(), photos[i]);
    }
    expect(profil.file.commandes, isEmpty);
    profil.fermer();
  });

  test('renvoi identique après application (réponse perdue) : une seule '
      'application ; autre contenu sous le même identifiant refusé', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    final commande = await saisirPhotos(profil, [_photo(1000, 3)]);
    await MoteurEnvoi(client).synchroniser(profil);
    expect(serveur.appliquees, hasLength(1));

    final reponse = await client.envoyerCommande(
      profil.jetons!.acces,
      commande,
    );
    expect(reponse.resultat, Resultat.alreadyApplied);
    expect(serveur.appliquees, hasLength(1));

    final autre = await client.envoyerCommande(
      profil.jetons!.acces,
      CommandeTablette(
        operationId: commande.operationId,
        acteurId: compteA,
        organisationId: orgX,
        type: 'cloture',
        corps: const {'etatFinal': 'Critique'},
        saisieServeurMs: 0,
        heureGarantie: true,
        fichiers: commande.fichiers,
      ),
    );
    expect(autre.resultat, Resultat.rejected);
    expect(autre.code, 'operation_reutilisee');
    profil.fermer();
  });

  test('réponse du renouvellement perdue : la fenêtre de grâce garde la '
      'session (T-03)', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    serveur.perdreReponseRenouvellement = true;

    final perdu = await MoteurEnvoi(client).synchroniser(profil);
    expect(perdu.interrompu, isTrue);

    final repris = await MoteurEnvoi(client).synchroniser(profil);
    expect(repris.contact, isTrue);
    expect(repris.sessionRefusee, isNull);

    // Hors fenêtre, le rejeu d'un ancien jeton révoque la famille.
    final ancien = profil.jetons!.renouvellement;
    await client.renouveler(ancien);
    serveur.heure = () => DateTime.now().toUtc().add(ServeurFactice.grace * 2);
    await expectLater(
      client.renouveler(ancien),
      throwsA(isA<SessionRefusee>()),
    );
    profil.fermer();
  });

  test(
    'le titulaire révoque sa session sur l\'appareil : consultation '
    'détruite au contact, saisies gardées puis envoyées après connexion',
    () async {
      final profil = await preparer(
        compteA,
        'a@demo.invalid',
        'mot-de-passe-a',
      );
      await saisirPhotos(profil, [_photo(5000, 3)]);
      profil.verrouiller();
      serveur.revoquerAppareil(compteA, appareil);

      final bilan = await MoteurEnvoi(client).synchroniser(profil);
      expect(bilan.sessionRefusee, 'session_revoquee');
      expect(profil.revoque, isTrue);
      expect(coffre.cles.containsKey('poc03.$compteA.consultation'), isFalse);
      expect(profil.file.enAttente, hasLength(1));
      expect(serveur.appliquees, isEmpty);
      expect(
        (await MoteurEnvoi(client).synchroniser(profil)).sessionRefusee,
        'connexion_requise',
        reason: 'plus aucun essai sous la session révoquée',
      );

      // Le même compte se reconnecte : nouveau PIN, file envoyée sous SON nom.
      final jetons = await client.connecter('a@demo.invalid', 'mot-de-passe-a');
      final adhesions = await client.lireAdhesions(jetons.acces);
      await profil.reprendreEnLigne(
        nouveauPin: '222222',
        contact: Contact(
          serveurMs: adhesions.serveurMs,
          jetons: jetons,
          organisations: adhesions.organisations,
        ),
        parametres: argon2Test,
      );
      final reprise = await MoteurEnvoi(client).synchroniser(profil);
      expect(reprise.appliquees, 1);
      expect(serveur.appliquees.single['acteurId'], compteA);
      profil.fermer();
    },
  );

  test('l\'administrateur coupe son organisation sur l\'appareil : seule '
      'elle est effacée, la session reste, ses saisies sont refusées et '
      'gardées', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    profil.remplacerInstantane(orgY, [
      {'id': 'y', 'siteId': 's', 'contenu': 'y'},
    ]);
    final enX = await saisirPhotos(profil, [_photo(2000, 3)]);
    final enY = await saisir(
      profil,
      organisationId: orgY,
      type: 'commentaire',
      corps: const {'texte': 'autre organisation'},
    );
    profil.verrouiller();
    serveur.couperOrganisation(orgX, appareil);

    final bilan = await MoteurEnvoi(client).synchroniser(profil);

    expect(bilan.contact, isTrue);
    expect(bilan.rejetees, 1);
    expect(bilan.appliquees, 1);
    expect(profil.organisations, {orgY});
    expect(
      File('${racine.path}/profils/$compteA/consultation_$orgX.db')
          .existsSync(),
      isFalse,
    );
    expect(serveur.appliquees.single['operationId'], enY.operationId);
    final gardee = profil.file.commandes.single;
    expect(gardee.operationId, enX.operationId);
    expect(gardee.etat, EtatEnvoi.rejetee);
    expect(gardee.detail, 'acces_coupe');
    expect(profil.jetons, isNotNull, reason: 'session du compte intacte');
    profil.fermer();
  });

  test('24 h dépassées : consultation bloquée, saisies envoyées au retour du '
      'réseau, puis délai relancé', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    await saisirPhotos(profil, [_photo(1000, 3)]);
    profil.verrouiller();
    horloge.avancer(const Duration(hours: 25));
    expect(
      (await profil.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.expire,
    );

    final bilan = await MoteurEnvoi(client).synchroniser(profil);
    expect(bilan.appliquees, 1);
    expect(
      (await profil.deverrouiller('123456')).resultat,
      ResultatDeverrouillage.ouvert,
    );
    profil.fermer();
  });

  test('serveur injoignable : rien n\'est perdu', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    await saisirPhotos(profil, [_photo(1000, 3)]);
    final injoignable = ClientSync(
      Uri.parse('http://127.0.0.1:1'),
      appareilId: appareil,
    );

    final bilan = await MoteurEnvoi(injoignable).synchroniser(profil);
    injoignable.fermer();

    expect(bilan.interrompu, isTrue);
    expect(bilan.contact, isFalse);
    expect(profil.file.enAttente, hasLength(1));
    profil.fermer();
  });

  test('deux profils : chaque file part sous la session de son titulaire, '
      'en arrière-plan sans PIN', () async {
    final a = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    final b = await preparer(compteB, 'b@demo.invalid', 'mot-de-passe-b');
    await saisirPhotos(a, [_photo(1000, 3)]);
    await saisirPhotos(b, [_photo(1000, 5)]);
    a.fermer();
    b.fermer();

    ecrireConfiguration(racine, ConfigurationEnvoi(serveur.adresse, appareil));
    final ok = await executerEnvoiArrierePlan(
      racine,
      coffre: coffre,
      horloge: horloge,
    );

    expect(ok, isTrue);
    expect(serveur.appliquees.map((c) => c['acteurId']).toSet(), {
      compteA,
      compteB,
    });
    expect(serveur.appliquees.map((c) => c['origine']).toSet(), {
      'arriere-plan',
    });
  });

  test('profil illisible (clé Keystore perdue) : les autres profils partent '
      'quand même', () async {
    final a = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    final b = await preparer(compteB, 'b@demo.invalid', 'mot-de-passe-b');
    await saisirPhotos(b, [_photo(1000, 5)]);
    a.fermer();
    b.fermer();
    coffre.cles.remove('poc03.$compteA.envoi');

    final bilans = await envoyerTousLesProfils(
      racine: racine,
      configuration: ConfigurationEnvoi(serveur.adresse, appareil),
      coffre: coffre,
      horloge: horloge,
    );

    expect(bilans[compteA]!.sessionRefusee, 'profil_illisible');
    expect(bilans[compteB]!.appliquees, 1);
  });

  test('photo refusée par le serveur (empreinte différente) : seule sa '
      'commande est écartée, la session reste, la suite part', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    final photo = _photo(3000, 7);
    final refusee = await saisirPhotos(profil, [photo]);
    serveur.fichiers[refusee.fichiers.single] = FichierRecu(
      photo.length,
      'autre-empreinte',
      compteA,
    );
    await saisir(
      profil,
      organisationId: orgX,
      type: 'commentaire',
      corps: const {'texte': 'après la photo refusée'},
    );

    final bilan = await MoteurEnvoi(client).synchroniser(profil);

    expect(bilan.sessionRefusee, isNull);
    expect(bilan.rejetees, 1);
    expect(bilan.appliquees, 1);
    expect(profil.jetons, isNotNull);
    final gardee = profil.file.commandes.single;
    expect(gardee.operationId, refusee.operationId);
    expect(gardee.etat, EtatEnvoi.rejetee);
    expect(gardee.detail, 'fichier_fichier_conflit');
    profil.fermer();
  });

  test('arrière-plan sans configuration : rien à faire', () async {
    expect(
      await executerEnvoiArrierePlan(racine, coffre: coffre, horloge: horloge),
      isTrue,
    );
    expect(lireConfiguration(racine), isNull);
  });

  test('commande d\'un autre auteur jamais envoyée par le moteur, refusée par '
      'le serveur', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    profil.file.ajouter(
      const CommandeTablette(
        operationId: 'op-autre',
        acteurId: compteB,
        organisationId: orgX,
        type: 'commentaire',
        corps: {},
        saisieServeurMs: 0,
        heureGarantie: true,
      ),
    );

    final bilan = await MoteurEnvoi(client).synchroniser(profil);
    expect(bilan.appliquees, 0);
    expect(serveur.appliquees, isEmpty);
    expect(profil.file.enAttente.single.operationId, 'op-autre');

    final refus = await client.envoyerCommande(
      profil.jetons!.acces,
      profil.file.enAttente.single,
    );
    expect(refus.resultat, Resultat.rejected);
    expect(refus.code, 'acteur_different');
    profil.fermer();
  });

  test('photo absente du serveur : la commande attend, la photo repart, puis '
      'appliquée ; conflit gardé pour arbitrage', () async {
    final profil = await preparer(compteA, 'a@demo.invalid', 'mot-de-passe-a');
    final avecPhoto = await saisirPhotos(profil, [_photo(4000, 3)]);
    final fichier = avecPhoto.fichiers.single;
    // La tablette croit la photo arrivée (le serveur l'a purgée, par exemple).
    profil.file.marquerPhotoComplete(fichier);
    final enConflit = await saisir(
      profil,
      organisationId: orgX,
      type: 'cloture',
      corps: const {'etatFinal': 'Bon'},
    );
    serveur.conflits.add(enConflit.operationId);

    final premier = await MoteurEnvoi(client).synchroniser(profil);
    expect(premier.attentes, 1);
    expect(premier.conflits, 1);
    expect(
      profil.file.photosDe(avecPhoto.operationId).single.complete,
      isFalse,
    );

    final second = await MoteurEnvoi(client).synchroniser(profil);
    expect(second.appliquees, 1);
    expect(serveur.fichiers[fichier]!.complet, isTrue);
    final gardee = profil.file.commandes.single;
    expect(gardee.operationId, enConflit.operationId);
    expect(gardee.etat, EtatEnvoi.conflit);
    expect(gardee.detail, 'reaffectee');
    profil.fermer();
  });

  test('erreurs lisibles', () {
    expect(const ReseauIndisponible('x').toString(), 'ReseauIndisponible: x');
    expect(const SessionRefusee('y').toString(), 'SessionRefusee: y');
    expect(const FichierRefuse('z').toString(), 'FichierRefuse: z');
    expect(BilanEnvoi().toString(), contains('appliquees=0'));
  });
}

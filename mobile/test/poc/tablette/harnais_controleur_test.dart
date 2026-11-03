import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/arriere_plan.dart';
import 'package:gestionsite_terrain/poc/tablette/harnais.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';

import 'outils.dart';
import 'serveur_factice.dart';

const compte = '0b6f8a39-6a7c-4c6e-9d43-6c2c1d1f0a01';

class _Reducteur implements ReducteurPhoto {
  @override
  Future<PhotoReduite> reduire(Uint8List octets) async =>
      PhotoReduite(octets, 1, 1);
}

DependancesHarnais _dependances(
  Directory racine, {
  bool securise = true,
  List<String>? programmations,
}) => DependancesHarnais(
  racine: racine,
  coffre: CoffreLogiciel(),
  horloge: HorlogeFactice(),
  reducteur: _Reducteur(),
  infos: () async => InfosAppareil(
    sdk: 34,
    fabricant: 'Google',
    modele: 'émulateur',
    appareilSecurise: securise,
    strongBoxDeclaree: false,
  ),
  photoDEssai: () async => Uint8List.fromList(List.filled(3000, 1)),
  programmerEnvoi: () async => programmations?.add('programmé'),
);

void main() {
  late Directory racine;
  late ServeurFactice serveur;

  setUp(() async {
    racine = Directory.systemTemp.createTempSync('poc03_harnais_');
    serveur = ServeurFactice()
      ..ajouterCompte('a@demo.invalid', compte, 'mot-de-passe-a')
      ..ajouterAdhesion(compte, 'org-x')
      ..definirInstantane('org-x', [
        {'id': 'e1', 'siteId': 's1', 'contenu': 'démo'},
      ]);
    await serveur.demarrer();
  });

  tearDown(() async {
    await serveur.arreter();
    racine.deleteSync(recursive: true);
  });

  test(
    'parcours du harnais : préparation, saisie, envoi, PIN oublié',
    () async {
      final programmations = <String>[];
      final controleur = ControleurHarnais(
        _dependances(racine, programmations: programmations),
      );
      final adresse = serveur.adresse.toString();

      await controleur.afficherInfos();
      await controleur.etat(); // aucun profil : échec signalé
      await controleur.deverrouiller('123456');
      await controleur.preparer(
        serveur: adresse,
        appareilId: 'appareil-test',
        email: 'a@demo.invalid',
        motDePasse: 'mot-de-passe-a',
        compteId: compte,
        pin: '123456',
      );
      await controleur.saisirPhoto('org-x');
      await controleur.etat();
      await controleur.synchroniser(adresse, 'appareil-test');
      controleur.verrouiller();
      await controleur.deverrouiller('999999');
      // PIN oublié : reconnexion en ligne avec un nouveau PIN.
      await controleur.preparer(
        serveur: adresse,
        appareilId: 'appareil-test',
        email: 'a@demo.invalid',
        motDePasse: 'mot-de-passe-a',
        compteId: compte,
        pin: '654321',
      );
      await controleur.programmerEnvoi(adresse, 'appareil-test');

      final journal = controleur.journal.reversed.join('\n');
      expect(journal, contains('StrongBox déclarée : non'));
      expect(journal, contains('État : échec'));
      expect(journal, contains('Préparation : OK'));
      expect(journal, contains('Instantané : 1 élément(s)'));
      expect(journal, contains('Enregistrée sur la tablette'));
      expect(journal, contains('appliquees=1'));
      expect(journal, contains('Résultat : pinFaux (essais restants : 4)'));
      expect(journal, contains('Envoi en arrière-plan programmé : OK'));
      expect(serveur.appliquees, hasLength(1));
      expect(programmations, ['programmé']);
      expect(lireConfiguration(racine)!.appareilId, 'appareil-test');
      controleur.dispose();
    },
  );

  test('mesures sans serveur : Keystore, Argon2id, déverrouillage', () async {
    final controleur = ControleurHarnais(_dependances(racine));

    await controleur.mesurerPerformances();

    final journal = controleur.journal.reversed.join('\n');
    expect(journal, contains('niveau logiciel'));
    expect(journal, contains('Argon2id m=19 Mio t=2 : médiane'));
    expect(journal, contains('Déverrouillage complet : médiane'));
    expect(journal, contains('PIN faux détecté'));
    expect(journal, contains('Mesures : OK'));
    expect(Directory('${racine.path}/mesures/profils').listSync(), isEmpty);
    controleur.dispose();
  });

  test('profil existant rechargé et repris ; tablette sans verrouillage '
      'd\'écran refusée', () async {
    final premier = ControleurHarnais(_dependances(racine));
    await premier.preparer(
      serveur: serveur.adresse.toString(),
      appareilId: 'appareil-test',
      email: 'a@demo.invalid',
      motDePasse: 'mot-de-passe-a',
      compteId: compte,
      pin: '123456',
    );
    final coffre = premier.dependances.coffre;
    premier.dispose();

    final second = ControleurHarnais(
      DependancesHarnais(
        racine: racine,
        coffre: coffre,
        horloge: HorlogeFactice(),
        reducteur: _Reducteur(),
        infos: premier.dependances.infos,
        photoDEssai: premier.dependances.photoDEssai,
        programmerEnvoi: () async {},
      ),
    );
    await second.preparer(
      serveur: serveur.adresse.toString(),
      appareilId: 'appareil-test',
      email: 'a@demo.invalid',
      motDePasse: 'mot-de-passe-a',
      compteId: compte,
      pin: '777777',
    );
    expect(second.journal.reversed.join('\n'), contains('Résultat : ouvert'));
    second.dispose();

    final sansVerrou = ControleurHarnais(_dependances(racine, securise: false));
    await sansVerrou.preparer(
      serveur: serveur.adresse.toString(),
      appareilId: 'appareil-test',
      email: 'a@demo.invalid',
      motDePasse: 'mot-de-passe-a',
      compteId: compte,
      pin: '123456',
    );
    expect(
      sansVerrou.journal.first,
      contains('verrouillage d\'écran de la tablette absent'),
    );
    sansVerrou.dispose();
  });
}

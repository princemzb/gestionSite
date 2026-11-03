/// POC-03 (S04-01, expérimental) : écran technique SANS DESIGN, pour les
/// essais manuels sur la tablette (docs/poc/POC-03.md, « essais sur
/// l'appareil réel »). Ce n'est pas une maquette : les écrans réels attendent
/// la validation du porteur (S13-03).
library;

import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';

import 'arriere_plan.dart';
import 'client_sync.dart';
import 'cryptographie.dart';
import 'envoi.dart';
import 'plateforme.dart';
import 'profil.dart';
import 'saisie.dart';
import 'verrou.dart';

/// Dépendances du harnais (Android réel, ou doubles dans les tests).
class DependancesHarnais {
  const DependancesHarnais({
    required this.racine,
    required this.coffre,
    required this.horloge,
    required this.reducteur,
    required this.infos,
    required this.photoDEssai,
    required this.programmerEnvoi,
    this.ecranEteint,
  });

  final Directory racine;
  final Coffre coffre;
  final HorlogeAppareil horloge;
  final ReducteurPhoto reducteur;
  final Future<InfosAppareil> Function() infos;
  final Future<Uint8List> Function() photoDEssai;
  final Future<void> Function() programmerEnvoi;
  final Stream<void>? ecranEteint;
}

class ActionImpossible implements Exception {
  const ActionImpossible(this.motif);

  final String motif;

  @override
  String toString() => motif;
}

/// Actions du harnais, sans interface (testées seules).
class ControleurHarnais extends ChangeNotifier {
  ControleurHarnais(this.dependances) {
    verrou = VerrouAutomatique(
      horloge: dependances.horloge,
      ecranEteint: dependances.ecranEteint,
      verrouiller: (motif) {
        profil?.verrouiller();
        _noter('Verrouillage automatique : $motif');
      },
    );
  }

  final DependancesHarnais dependances;
  late final VerrouAutomatique verrou;
  final journal = <String>[];
  ProfilTablette? profil;

  void _noter(String ligne) {
    journal.insert(0, ligne);
    notifyListeners();
  }

  ClientSync _client(String serveur, String appareilId) =>
      ClientSync(Uri.parse(serveur), appareilId: appareilId);

  ProfilTablette _profil() =>
      profil ?? (throw const ActionImpossible('aucun profil'));

  /// Exécute une action et note sa durée (mesures manuelles sur la tablette).
  Future<void> _essayer(String action, Future<void> Function() travail) async {
    final chrono = Stopwatch()..start();
    try {
      await travail();
      _noter('$action : OK (${chrono.elapsedMilliseconds} ms)');
    } on Exception catch (erreur) {
      _noter('$action : échec ($erreur)');
    } on StateError catch (erreur) {
      _noter('$action : échec (${erreur.message})');
    }
  }

  /// Mesures sans serveur (Keystore, Argon2id, déverrouillage complet), à
  /// lancer dans une version « profile » : `flutter test` ne mesure qu'en
  /// mode débogage, bien plus lent. Lignes « RAPPORT | » dans logcat.
  Future<void> mesurerPerformances() => _essayer('Mesures', () async {
    void rapport(String ligne) {
      _noter(ligne);
      debugPrint('RAPPORT | $ligne');
    }

    int mediane(List<int> v) => (List.of(v)..sort())[v.length ~/ 2];
    final i = await dependances.infos();
    rapport('Appareil ${i.fabricant} ${i.modele}, SDK ${i.sdk}');
    final cles = <int>[];
    NiveauCle? niveau;
    for (var n = 0; n < 3; n++) {
      final t = Stopwatch()..start();
      niveau = await dependances.coffre.creerCle(
        'poc03.mesure',
        deverrouillageExige: true,
      );
      cles.add(t.elapsedMilliseconds);
    }
    await dependances.coffre.supprimerCle('poc03.mesure');
    rapport('Clé Keystore : niveau ${niveau!.name}, création $cles ms');

    final argon = <int>[];
    for (var n = 0; n < 5; n++) {
      final t = Stopwatch()..start();
      deriverCleDuPin('123456', octetsAleatoires(16), ParametresArgon2.owasp);
      argon.add(t.elapsedMilliseconds);
    }
    rapport('Argon2id m=19 Mio t=2 : médiane ${mediane(argon)} ms $argon');

    final racine = Directory('${dependances.racine.path}/mesures');
    if (racine.existsSync()) {
      racine.deleteSync(recursive: true);
    }
    const compte = '00000000-0000-4000-8000-000000000003';
    var t = Stopwatch()..start();
    final profil = await ProfilTablette.preparer(
      racine: racine,
      compteId: compte,
      pin: '123456',
      contact: Contact(
        serveurMs: DateTime.now().millisecondsSinceEpoch,
        jetons: const Jetons('mesure', 'mesure'),
        organisations: const {'org-mesure'},
      ),
      coffre: dependances.coffre,
      horloge: dependances.horloge,
    );
    rapport('Préparation d\'un profil : ${t.elapsedMilliseconds} ms');
    final ouvertures = <int>[];
    for (var n = 0; n < 5; n++) {
      t = Stopwatch()..start();
      await profil.deverrouiller('123456');
      profil.consultation('org-mesure');
      ouvertures.add(t.elapsedMilliseconds);
      profil.verrouiller();
    }
    rapport(
      'Déverrouillage complet : médiane ${mediane(ouvertures)} ms $ouvertures',
    );
    t = Stopwatch()..start();
    await profil.deverrouiller('000000');
    rapport('PIN faux détecté en ${t.elapsedMilliseconds} ms');
    await profil.retirer();
  });

  Future<void> afficherInfos() => _essayer('Infos', () async {
    final i = await dependances.infos();
    _noter(
      'Appareil ${i.fabricant} ${i.modele}, Android SDK ${i.sdk}, '
      'verrouillage d\'écran : ${i.appareilSecurise ? 'oui' : 'NON'}, '
      'StrongBox déclarée : ${i.strongBoxDeclaree ? 'oui' : 'non'}',
    );
  });

  /// Connexion en ligne puis préparation (ou reprise après PIN oublié,
  /// blocage ou révocation) du profil.
  Future<void> preparer({
    required String serveur,
    required String appareilId,
    required String email,
    required String motDePasse,
    required String compteId,
    required String pin,
  }) => _essayer('Préparation', () async {
    final infos = await dependances.infos();
    if (!infos.appareilSecurise) {
      throw const ActionImpossible(
        'verrouillage d\'écran de la tablette absent (T-02)',
      );
    }

    final client = _client(serveur, appareilId);
    try {
      final jetons = await client.connecter(email, motDePasse);
      final adhesions = await client.lireAdhesions(jetons.acces);
      final contact = Contact(
        serveurMs: adhesions.serveurMs,
        jetons: jetons,
        organisations: adhesions.organisations,
      );
      final existant = profil;
      if (existant != null && existant.compteId == compteId) {
        await existant.reprendreEnLigne(nouveauPin: pin, contact: contact);
      } else {
        existant?.fermer();
        profil = ProfilTablette.presents(dependances.racine).contains(compteId)
            ? await _rechargerEtReprendre(compteId, pin, contact)
            : await ProfilTablette.preparer(
                racine: dependances.racine,
                compteId: compteId,
                pin: pin,
                contact: contact,
                coffre: dependances.coffre,
                horloge: dependances.horloge,
              );
      }
      await deverrouiller(pin);
      final elements = await telechargerInstantanes(_profil(), client);
      _noter('Instantané : $elements élément(s)');
    } finally {
      client.fermer();
    }
  });

  Future<ProfilTablette> _rechargerEtReprendre(
    String compteId,
    String pin,
    Contact contact,
  ) async {
    final charge = await ProfilTablette.charger(
      racine: dependances.racine,
      compteId: compteId,
      coffre: dependances.coffre,
      horloge: dependances.horloge,
    );
    await charge.reprendreEnLigne(nouveauPin: pin, contact: contact);
    return charge;
  }

  Future<void> deverrouiller(
    String pin,
  ) => _essayer('Déverrouillage', () async {
    final p = profil;
    if (p == null) {
      throw const ActionImpossible('aucun profil');
    }

    final r = await p.deverrouiller(pin);
    _noter(
      'Résultat : ${r.resultat.name} (essais restants : ${r.essaisRestants ?? '-'})',
    );
    if (r.resultat == ResultatDeverrouillage.ouvert) {
      await verrou.demarrer();
    }
  });

  void verrouiller() {
    profil?.verrouiller();
    verrou.arreter();
    _noter('Verrouillé');
  }

  Future<void> etat() => _essayer('État', () async {
    final p = profil;
    if (p == null) {
      throw const ActionImpossible('aucun profil');
    }

    final limite = await p.etatLimite();
    _noter(
      'Ouvert : ${p.deverrouille}, bloqué : ${p.bloque}, révoqué : ${p.revoque}, '
      'consommé : ${limite.consomme.inMinutes} min, expiré : ${limite.expire}, '
      'heure garantie : ${limite.heureGarantie}, '
      'organisations : ${p.organisations.join(', ')}, '
      'file : ${p.file.commandes.map((c) => '${c.type}/${c.etat.name}').join(', ')}',
    );
  });

  Future<void> saisirPhoto(String organisationId) =>
      _essayer('Saisie avec photo', () async {
        final commande = await saisir(
          _profil(),
          organisationId: organisationId,
          type: 'cloture',
          corps: const {'etatFinal': 'Bon', 'origine': 'harnais POC-03'},
          photos: [await dependances.photoDEssai()],
          reducteur: dependances.reducteur,
        );
        _noter('Enregistrée sur la tablette : ${commande.operationId}');
      });

  Future<void> synchroniser(String serveur, String appareilId) => _essayer(
    'Synchronisation',
    () async {
      final client = _client(serveur, appareilId);
      try {
        _noter('Bilan : ${await MoteurEnvoi(client).synchroniser(_profil())}');
      } finally {
        client.fermer();
      }
    },
  );

  Future<void> programmerEnvoi(String serveur, String appareilId) =>
      _essayer('Envoi en arrière-plan programmé', () async {
        ecrireConfiguration(
          dependances.racine,
          ConfigurationEnvoi(Uri.parse(serveur), appareilId),
        );
        await dependances.programmerEnvoi();
      });

  @override
  void dispose() {
    unawaited(verrou.fermer());
    profil?.fermer();
    super.dispose();
  }
}

/// Écran technique : champs bruts, boutons et journal. Aucun design.
class HarnaisPoc03 extends StatefulWidget {
  const HarnaisPoc03({required this.dependances, super.key});

  final DependancesHarnais dependances;

  @override
  State<HarnaisPoc03> createState() => _HarnaisPoc03State();
}

class _HarnaisPoc03State extends State<HarnaisPoc03>
    with WidgetsBindingObserver {
  late final ControleurHarnais _controleur;
  final _serveur = TextEditingController(text: 'http://10.0.2.2:8765');
  final _appareil = TextEditingController(text: 'appareil-harnais');
  final _email = TextEditingController(text: 'a@demo.invalid');
  final _motDePasse = TextEditingController();
  final _compte = TextEditingController(
    text: '0b6f8a39-6a7c-4c6e-9d43-6c2c1d1f0a01',
  );
  final _organisation = TextEditingController(text: 'org-x');
  final _pin = TextEditingController();

  @override
  void initState() {
    super.initState();
    _controleur = ControleurHarnais(widget.dependances);
    WidgetsBinding.instance.addObserver(this);
    // Version « profile » lancée par adb : mesures dès l'ouverture (POC-03.md).
    if (const bool.fromEnvironment('POC03_MESURES')) {
      unawaited(_controleur.mesurerPerformances());
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    unawaited(_controleur.verrou.cycleDeVie(state));
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _controleur.dispose();
    for (final c in [
      _serveur,
      _appareil,
      _email,
      _motDePasse,
      _compte,
      _organisation,
      _pin,
    ]) {
      c.dispose();
    }
    super.dispose();
  }

  Widget _champ(
    String libelle,
    TextEditingController c, {
    bool masque = false,
  }) => TextField(
    controller: c,
    obscureText: masque,
    decoration: InputDecoration(labelText: libelle),
  );

  Widget _bouton(String libelle, Future<void> Function() action) =>
      OutlinedButton(
        onPressed: () => unawaited(action()),
        child: Text(libelle),
      );

  @override
  Widget build(BuildContext context) {
    return Listener(
      onPointerDown: (_) => unawaited(_controleur.verrou.activite()),
      child: Scaffold(
        appBar: AppBar(
          title: const Text(
            'POC-03 — écran technique sans design (expérimental)',
          ),
        ),
        body: ListenableBuilder(
          listenable: _controleur,
          builder: (context, _) => ListView(
            padding: const EdgeInsets.all(8),
            children: [
              _champ('Serveur', _serveur),
              _champ('Identifiant d\'appareil', _appareil),
              _champ('E-mail (démo)', _email),
              _champ('Mot de passe (démo)', _motDePasse, masque: true),
              _champ('Identifiant du compte', _compte),
              _champ('Organisation', _organisation),
              _champ('PIN (6 chiffres)', _pin, masque: true),
              Wrap(
                spacing: 8,
                children: [
                  _bouton('Infos', _controleur.afficherInfos),
                  _bouton('Mesures', _controleur.mesurerPerformances),
                  _bouton(
                    'Connexion et préparation',
                    () => _controleur.preparer(
                      serveur: _serveur.text,
                      appareilId: _appareil.text,
                      email: _email.text,
                      motDePasse: _motDePasse.text,
                      compteId: _compte.text,
                      pin: _pin.text,
                    ),
                  ),
                  _bouton(
                    'Déverrouiller',
                    () => _controleur.deverrouiller(_pin.text),
                  ),
                  _bouton(
                    'Verrouiller / changer d\'utilisateur',
                    () async => _controleur.verrouiller(),
                  ),
                  _bouton('État', _controleur.etat),
                  _bouton(
                    'Saisie avec photo',
                    () => _controleur.saisirPhoto(_organisation.text),
                  ),
                  _bouton(
                    'Synchroniser',
                    () =>
                        _controleur.synchroniser(_serveur.text, _appareil.text),
                  ),
                  _bouton(
                    'Programmer l\'envoi en arrière-plan',
                    () => _controleur.programmerEnvoi(
                      _serveur.text,
                      _appareil.text,
                    ),
                  ),
                ],
              ),
              for (final ligne in _controleur.journal) Text(ligne),
            ],
          ),
        ),
      ),
    );
  }
}

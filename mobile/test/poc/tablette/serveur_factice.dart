// POC-03 (S04-01, expérimental) : serveur factice des routes que la tablette
// attend d'US-13/US-14 (elles n'existent pas encore dans l'API). Utilisé dans
// les tests (en mémoire, même processus) et sur le Mac pour l'émulateur
// (tool/poc03/serveur_hote.dart), avec des routes de pilotage /controle/*.
//
// Il reproduit les décisions du porteur du 30/10 : révocation de la session de
// l'appareil par le titulaire (toutes organisations), coupure d'une seule
// organisation sur l'appareil par son administrateur ; et le protocole
// proposé : commandes idempotentes, photos envoyées par morceaux avec reprise.
import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:pointycastle/export.dart';

String _hexSha256(List<int> octets) => SHA256Digest()
    .process(Uint8List.fromList(octets))
    .map((o) => o.toRadixString(16).padLeft(2, '0'))
    .join();

String _jeton() {
  final aleatoire = Random.secure();
  return base64Url.encode(
    List<int>.generate(24, (_) => aleatoire.nextInt(256)),
  );
}

class _Session {
  _Session(this.compteId, this.appareilId, this.famille);

  final String compteId;
  final String appareilId;
  final String famille;
  String? remplacePar;
  String? accesRemplacant;
  DateTime? remplaceLe;
}

class FichierRecu {
  FichierRecu(this.total, this.empreinte, this.compteId);

  final int total;
  final String empreinte;
  final String compteId;
  final BytesBuilder octets = BytesBuilder();
  bool complet = false;

  /// Morceaux acceptés (mesure : une reprise ne renvoie pas tout).
  int morceaux = 0;
}

class ServeurFactice {
  ServeurFactice({this.pilotage = false, this.executerAdb});

  /// Routes /controle/* (Mac seulement, jamais dans l'application).
  final bool pilotage;

  /// Action adb demandée par l'essai sur l'émulateur (coupure réseau, écran…).
  final Future<void> Function(String action, Map<String, Object?> options)?
  executerAdb;

  late final HttpServer _http;
  final _comptes = <String, ({String id, String motDePasse})>{};
  final _adhesions = <String, Set<String>>{};
  final _coupures = <String>{};
  final _famillesRevoquees = <String>{};
  final _sessionsRevoquees = <String>{};
  final _sessions = <String, _Session>{};
  final _acces = <String, _Session>{};
  final _operations = <String, ({String empreinte, String resultat})>{};
  final _instantanes = <String, List<Map<String, Object?>>>{};

  /// Commandes appliquées (une entrée par application réelle).
  final appliquees = <Map<String, Object?>>[];
  final fichiers = <String, FichierRecu>{};
  final journal = <String>[];

  /// Heure du serveur (modifiable dans les essais).
  DateTime Function() heure = () => DateTime.now().toUtc();

  /// Coupure simulée : connexion détruite au morceau qui atteint ce nombre
  /// d'octets de photo (morceau perdu, comme sur un vrai réseau).
  int? couperApresOctets;

  void couperApres(int octets) {
    couperApresOctets = octets;
    _octetsDepuisReglage = 0;
  }

  /// Opérations auxquelles le serveur répond par un conflit (ex. réaffectée).
  final conflits = <String>{};

  /// Lenteur de chaque morceau (laisse le temps à une vraie coupure).
  Duration delaiMorceau = Duration.zero;

  /// Réponse du renouvellement perdue après validation (rotation faite).
  bool perdreReponseRenouvellement = false;
  int _octetsDepuisReglage = 0;

  /// Fenêtre de grâce : l'ancien jeton rejoué peu après une rotation (réponse
  /// perdue) redonne la même session au lieu de la révoquer (T-03).
  static const grace = Duration(seconds: 60);

  int get port => _http.port;

  Uri get adresse => Uri.parse('http://${_http.address.host}:$port');

  Future<void> demarrer({InternetAddress? adresse, int port = 0}) async {
    _http = await HttpServer.bind(
      adresse ?? InternetAddress.loopbackIPv4,
      port,
    );
    _http.listen(_traiter);
  }

  Future<void> arreter() => _http.close(force: true);

  void _noter(String evenement) =>
      journal.add('${DateTime.now().toUtc().toIso8601String()} $evenement');

  // --- Données de démonstration (aucune donnée réelle) ---

  void ajouterCompte(String email, String id, String motDePasse) =>
      _comptes[email] = (id: id, motDePasse: motDePasse);

  void ajouterAdhesion(String compteId, String organisationId) =>
      _adhesions.putIfAbsent(compteId, () => {}).add(organisationId);

  void definirInstantane(String organisationId, List<Map<String, Object?>> e) =>
      _instantanes[organisationId] = e;

  /// Le titulaire révoque SA session sur l'appareil (toutes organisations).
  void revoquerAppareil(String compteId, String appareilId) {
    _sessionsRevoquees.add('$compteId|$appareilId');
    for (final s in _sessions.values) {
      if (s.compteId == compteId && s.appareilId == appareilId) {
        _famillesRevoquees.add(s.famille);
      }
    }
    _noter('revocation appareil compte=$compteId');
  }

  /// L'administrateur coupe SON organisation sur l'appareil, sans toucher à la
  /// session du compte.
  void couperOrganisation(String organisationId, String appareilId) {
    _coupures.add('$appareilId|$organisationId');
    _noter('coupure organisation=$organisationId');
  }

  Set<String> _accessibles(String compteId, String appareilId) =>
      (_adhesions[compteId] ?? const <String>{})
          .where((o) => !_coupures.contains('$appareilId|$o'))
          .toSet();

  Map<String, Object?> _nouvelleSession(String compteId, String appareilId) {
    final famille = _jeton();
    final session = _Session(compteId, appareilId, famille);
    final renouvellement = _jeton();
    final acces = _jeton();
    _sessions[renouvellement] = session;
    _acces[acces] = session;
    return {'jetonAcces': acces, 'jetonRenouvellement': renouvellement};
  }

  // --- Routes ---

  Future<void> _traiter(HttpRequest requete) async {
    final reponse = requete.response;
    try {
      final chemin = requete.uri.path;
      final segments = requete.uri.pathSegments;
      if (pilotage && chemin.startsWith('/controle/')) {
        await _controle(requete);
      } else if (requete.method == 'POST' && chemin == '/auth/login') {
        await _connexion(requete);
      } else if (requete.method == 'POST' && chemin == '/auth/refresh') {
        await _renouvellement(requete);
      } else if (requete.method == 'GET' && chemin == '/me') {
        _moi(requete);
      } else if (segments.length == 3 &&
          segments[0] == 'sync' &&
          segments[1] == 'files') {
        await _fichier(requete, segments[2]);
      } else if (segments.length == 4 &&
          segments[0] == 'organizations' &&
          segments[2] == 'sync' &&
          segments[3] == 'snapshot') {
        _instantane(requete, segments[1]);
      } else if (requete.method == 'POST' &&
          segments.length == 4 &&
          segments[0] == 'organizations' &&
          segments[2] == 'sync' &&
          segments[3] == 'commands') {
        await _commande(requete, segments[1]);
      } else {
        _json(reponse, HttpStatus.notFound, {'code': 'introuvable'});
      }
    } on _ConnexionDetruite {
      return;
    }

    await reponse.close();
  }

  void _json(HttpResponse reponse, int statut, Map<String, Object?> corps) {
    reponse
      ..statusCode = statut
      ..headers.contentType = ContentType.json
      ..write(jsonEncode(corps));
  }

  Future<Map<String, Object?>> _corps(HttpRequest requete) async =>
      jsonDecode(await utf8.decoder.bind(requete).join())
          as Map<String, Object?>;

  _Session? _session(HttpRequest requete) {
    final entete = requete.headers.value(HttpHeaders.authorizationHeader);
    if (entete == null || !entete.startsWith('Bearer ')) {
      return null;
    }

    final session = _acces[entete.substring(7)];
    if (session == null || _famillesRevoquees.contains(session.famille)) {
      return null;
    }

    return session;
  }

  Future<void> _connexion(HttpRequest requete) async {
    final corps = await _corps(requete);
    final compte = _comptes[corps['email']];
    if (compte == null || compte.motDePasse != corps['motDePasse']) {
      _json(requete.response, HttpStatus.unauthorized, {
        'code': 'identifiants',
      });
      return;
    }

    final appareilId = corps['appareilId']! as String;
    _sessionsRevoquees.remove('${compte.id}|$appareilId');
    _noter('connexion compte=${compte.id}');
    _json(
      requete.response,
      HttpStatus.ok,
      _nouvelleSession(compte.id, appareilId),
    );
  }

  Future<void> _renouvellement(HttpRequest requete) async {
    final corps = await _corps(requete);
    final ancien = corps['jetonRenouvellement']! as String;
    final session = _sessions[ancien];
    if (session == null || session.appareilId != corps['appareilId']) {
      _json(requete.response, HttpStatus.unauthorized, {
        'code': 'jeton_invalide',
      });
      return;
    }

    if (_famillesRevoquees.contains(session.famille)) {
      final code =
          _sessionsRevoquees.contains(
            '${session.compteId}|${session.appareilId}',
          )
          ? 'session_revoquee'
          : 'jeton_invalide';
      _json(requete.response, HttpStatus.unauthorized, {'code': code});
      return;
    }

    if (session.remplacePar != null) {
      if (heure().difference(session.remplaceLe!) <= grace) {
        _noter('renouvellement rejoué dans la fenêtre de grâce');
        _json(requete.response, HttpStatus.ok, {
          'jetonAcces': session.accesRemplacant,
          'jetonRenouvellement': session.remplacePar,
        });
      } else {
        _famillesRevoquees.add(session.famille); // rejeu : famille révoquée
        _json(requete.response, HttpStatus.unauthorized, {
          'code': 'jeton_invalide',
        });
      }
      return;
    }

    final nouveau = _jeton();
    final acces = _jeton();
    final suite = _Session(
      session.compteId,
      session.appareilId,
      session.famille,
    );
    _sessions[nouveau] = suite;
    _acces[acces] = suite;
    session
      ..remplacePar = nouveau
      ..accesRemplacant = acces
      ..remplaceLe = heure();
    if (perdreReponseRenouvellement) {
      perdreReponseRenouvellement = false;
      await _detruire(requete);
    }

    _json(requete.response, HttpStatus.ok, {
      'jetonAcces': acces,
      'jetonRenouvellement': nouveau,
    });
  }

  void _moi(HttpRequest requete) {
    final session = _session(requete);
    if (session == null) {
      _json(requete.response, HttpStatus.unauthorized, {
        'code': 'jeton_invalide',
      });
      return;
    }

    _noter('adhesions relues compte=${session.compteId}');
    _json(requete.response, HttpStatus.ok, {
      'compteId': session.compteId,
      'heureServeur': heure().toIso8601String(),
      'organisations': _accessibles(
        session.compteId,
        session.appareilId,
      ).toList(),
    });
  }

  void _instantane(HttpRequest requete, String organisationId) {
    final session = _session(requete);
    if (session == null) {
      _json(requete.response, HttpStatus.unauthorized, {
        'code': 'jeton_invalide',
      });
      return;
    }

    if (!_accessibles(
      session.compteId,
      session.appareilId,
    ).contains(organisationId)) {
      _json(requete.response, HttpStatus.forbidden, {'code': 'acces_coupe'});
      return;
    }

    _json(requete.response, HttpStatus.ok, {
      'elements': _instantanes[organisationId] ?? const [],
    });
  }

  Future<void> _fichier(HttpRequest requete, String fichierId) async {
    final session = _session(requete);
    if (session == null) {
      _json(requete.response, HttpStatus.unauthorized, {
        'code': 'jeton_invalide',
      });
      return;
    }

    final existant = fichiers[fichierId];
    if (existant != null && existant.compteId != session.compteId) {
      _json(requete.response, HttpStatus.notFound, {'code': 'introuvable'});
      return;
    }

    if (requete.method == 'GET') {
      if (existant == null) {
        _json(requete.response, HttpStatus.notFound, {'code': 'introuvable'});
      } else {
        _json(requete.response, HttpStatus.ok, {
          'recu': existant.octets.length,
          'total': existant.total,
          'complet': existant.complet,
        });
      }
      return;
    }

    final plage = RegExp(r'^bytes (\d+)-(\d+)/(\d+)$')
        .firstMatch(requete.headers.value('content-range') ?? '');
    final empreinte = requete.headers.value('x-empreinte');
    if (requete.method != 'PUT' || plage == null || empreinte == null) {
      _json(requete.response, HttpStatus.badRequest, {'code': 'plage'});
      return;
    }

    final debut = int.parse(plage.group(1)!);
    final fin = int.parse(plage.group(2)!);
    final total = int.parse(plage.group(3)!);
    final fichier = fichiers.putIfAbsent(
      fichierId,
      () => FichierRecu(total, empreinte, session.compteId),
    );
    if (fichier.empreinte != empreinte || fichier.total != total) {
      _json(requete.response, HttpStatus.conflict, {'code': 'fichier_conflit'});
      return;
    }

    final morceau = BytesBuilder();
    await requete.forEach(morceau.add);
    _octetsDepuisReglage += morceau.length;
    final seuil = couperApresOctets;
    if (seuil != null && _octetsDepuisReglage >= seuil) {
      // Coupure pendant ce morceau : il est perdu en entier, sans réponse.
      couperApresOctets = null;
      _noter('coupure simulée fichier=$fichierId');
      await _detruire(requete);
    }

    if (delaiMorceau > Duration.zero) {
      await Future<void>.delayed(delaiMorceau);
    }

    if (fichier.complet || fin < fichier.octets.length) {
      // Déjà reçu : renvoi sans effet.
      _json(requete.response, HttpStatus.ok, {'recu': fichier.octets.length});
      return;
    }

    if (debut != fichier.octets.length || morceau.length != fin - debut + 1) {
      _json(requete.response, HttpStatus.requestedRangeNotSatisfiable, {
        'recu': fichier.octets.length,
      });
      return;
    }

    fichier
      ..octets.add(morceau.takeBytes())
      ..morceaux += 1;
    if (fichier.octets.length == fichier.total) {
      if (_hexSha256(fichier.octets.toBytes()) != fichier.empreinte) {
        fichiers.remove(fichierId);
        _json(requete.response, HttpStatus.conflict, {'code': 'empreinte'});
        return;
      }

      fichier.complet = true;
      _noter('fichier complet $fichierId morceaux=${fichier.morceaux}');
    }

    _json(requete.response, HttpStatus.ok, {'recu': fichier.octets.length});
  }

  Future<void> _commande(HttpRequest requete, String organisationId) async {
    final session = _session(requete);
    if (session == null) {
      _json(requete.response, HttpStatus.unauthorized, {
        'code': 'jeton_invalide',
      });
      return;
    }

    final texte = await utf8.decoder.bind(requete).join();
    final corps = jsonDecode(texte) as Map<String, Object?>;
    if (!_accessibles(
      session.compteId,
      session.appareilId,
    ).contains(organisationId)) {
      _json(requete.response, HttpStatus.forbidden, {'code': 'acces_coupe'});
      return;
    }

    if (corps['acteurId'] != session.compteId) {
      _json(requete.response, HttpStatus.ok, {
        'resultat': 'rejected',
        'code': 'acteur_different',
      });
      return;
    }

    final operationId = corps['operationId']! as String;
    final empreinte = _hexSha256(utf8.encode(texte));
    final connue = _operations[operationId];
    if (connue != null) {
      if (connue.empreinte != empreinte) {
        _json(requete.response, HttpStatus.conflict, {
          'code': 'operation_reutilisee',
        });
      } else {
        _json(requete.response, HttpStatus.ok, {'resultat': 'alreadyApplied'});
      }
      return;
    }

    if (conflits.contains(operationId)) {
      _json(requete.response, HttpStatus.ok, {
        'resultat': 'conflict',
        'code': 'reaffectee',
      });
      return;
    }

    final manquants = (corps['fichiers']! as List<Object?>)
        .where((f) => fichiers[f]?.complet != true)
        .toList();
    if (manquants.isNotEmpty) {
      _json(requete.response, HttpStatus.ok, {'resultat': 'fichiersManquants'});
      return;
    }

    _operations[operationId] = (empreinte: empreinte, resultat: 'applied');
    appliquees.add({
      ...corps,
      'organisationId': organisationId,
      'origine': requete.headers.value('x-origine'),
      'recuLe': heure().toIso8601String(),
    });
    _noter(
      'commande appliquee $operationId origine=${requete.headers.value('x-origine')}',
    );
    _json(requete.response, HttpStatus.ok, {'resultat': 'applied'});
  }

  Future<void> _controle(HttpRequest requete) async {
    final action = requete.uri.pathSegments[1];
    final corps = requete.method == 'POST'
        ? await _corps(requete)
        : const <String, Object?>{};
    switch (action) {
      case 'etat':
        _json(requete.response, HttpStatus.ok, etat());
      case 'compte':
        ajouterCompte(
          corps['email']! as String,
          corps['id']! as String,
          corps['motDePasse']! as String,
        );
        for (final o
            in (corps['organisations']! as List<Object?>).cast<String>()) {
          ajouterAdhesion(corps['id']! as String, o);
          definirInstantane(o, [
            {
              'id': 'element-$o',
              'siteId': 'site-$o',
              'contenu': 'Instantané démo $o',
            },
          ]);
        }
        _json(requete.response, HttpStatus.ok, const {});
      case 'revoquer-appareil':
        revoquerAppareil(
          corps['compteId']! as String,
          corps['appareilId']! as String,
        );
        _json(requete.response, HttpStatus.ok, const {});
      case 'couper-organisation':
        couperOrganisation(
          corps['organisationId']! as String,
          corps['appareilId']! as String,
        );
        _json(requete.response, HttpStatus.ok, const {});
      case 'lenteur':
        delaiMorceau = Duration(milliseconds: corps['ms']! as int);
        _json(requete.response, HttpStatus.ok, const {});
      case 'adb':
        final executer = executerAdb;
        if (executer == null) {
          _json(requete.response, HttpStatus.notImplemented, const {});
        } else {
          // Réponse immédiate : l'action (ex. coupure réseau) suit, différée.
          unawaited(executer(corps['action']! as String, corps));
          _json(requete.response, HttpStatus.accepted, const {});
        }
      default:
        _json(requete.response, HttpStatus.notFound, const {});
    }
  }

  Map<String, Object?> etat() => {
    'appliquees': appliquees,
    'fichiers': {
      for (final e in fichiers.entries)
        e.key: {
          'recu': e.value.octets.length,
          'total': e.value.total,
          'complet': e.value.complet,
          'morceaux': e.value.morceaux,
        },
    },
    'journal': journal,
  };

  Future<void> _detruire(HttpRequest requete) async {
    final socket = await requete.response.detachSocket(writeHeaders: false);
    socket.destroy();
    throw const _ConnexionDetruite();
  }
}

class _ConnexionDetruite implements Exception {
  const _ConnexionDetruite();
}

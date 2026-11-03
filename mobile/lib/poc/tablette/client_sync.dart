/// POC-03 (S04-01, expérimental) : client HTTP des routes dont la tablette
/// aura besoin (US-13, US-14). Elles n'existent pas encore côté API : le POC
/// les exerce contre un serveur factice (test/poc/tablette/serveur_factice.dart)
/// et POC-03.md en donne la liste.
library;

import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'file_envoi.dart';
import 'profil.dart';

/// Réseau absent, coupé, lent, portail captif ou serveur indisponible : rien
/// n'est perdu, l'envoi reprendra.
class ReseauIndisponible implements Exception {
  const ReseauIndisponible(this.cause);

  final Object cause;

  @override
  String toString() => 'ReseauIndisponible: $cause';
}

/// Session refusée définitivement (`session_revoquee`, `jeton_invalide`) : on
/// arrête les essais, une connexion par mot de passe est nécessaire (T-03).
class SessionRefusee implements Exception {
  const SessionRefusee(this.code);

  final String code;

  @override
  String toString() => 'SessionRefusee: $code';
}

/// Résultat d'une commande : appliquée, déjà appliquée (rejeu), conflit,
/// refusée, ou en attente de ses photos.
enum Resultat { applied, alreadyApplied, conflict, rejected, fichiersManquants }

class ReponseCommande {
  const ReponseCommande(this.resultat, {this.code});

  final Resultat resultat;
  final String? code;
}

class Adhesions {
  const Adhesions(this.serveurMs, this.organisations);

  final int serveurMs;
  final Set<String> organisations;
}

class ClientSync {
  ClientSync(
    this.base, {
    required this.appareilId,
    this.origine = 'premier-plan',
    this.delai = const Duration(seconds: 20),
  }) : _http = HttpClient()..connectionTimeout = const Duration(seconds: 10);

  final Uri base;
  final String appareilId;

  /// `premier-plan` ou `arriere-plan` (mesure du POC).
  final String origine;
  final Duration delai;
  final HttpClient _http;

  Future<({int statut, Map<String, Object?> json, HttpHeaders entetes})>
  _requete(
    String methode,
    String chemin, {
    String? jeton,
    Object? json,
    Uint8List? octets,
    Map<String, String> entetes = const {},
  }) async {
    try {
      final requete = await _http
          .openUrl(methode, base.resolve(chemin))
          .timeout(delai);
      requete.headers
        ..set('X-Appareil', appareilId)
        ..set('X-Origine', origine);
      if (jeton != null) {
        requete.headers.set(HttpHeaders.authorizationHeader, 'Bearer $jeton');
      }
      entetes.forEach(requete.headers.set);
      if (json != null) {
        requete.headers.contentType = ContentType.json;
        requete.add(utf8.encode(jsonEncode(json)));
      } else if (octets != null) {
        requete.headers.contentType = ContentType.binary;
        requete.contentLength = octets.length;
        requete.add(octets);
      }
      final reponse = await requete.close().timeout(delai);
      final corps = await reponse.transform(utf8.decoder).join().timeout(delai);
      if (reponse.statusCode == HttpStatus.tooManyRequests ||
          reponse.statusCode >= 500) {
        throw ReseauIndisponible('HTTP ${reponse.statusCode}');
      }

      final Map<String, Object?> donnees;
      if (corps.isEmpty) {
        donnees = const {};
      } else {
        final decode = jsonDecode(corps);
        if (decode is! Map<String, Object?>) {
          throw const FormatException('objet JSON attendu');
        }
        donnees = decode;
      }
      if (reponse.statusCode == HttpStatus.unauthorized) {
        // Jeton d'accès expiré : passager, renouvelé au prochain passage.
        // Tout autre refus arrête les essais (T-03), sans rien effacer ici.
        final code = (donnees['code'] as String?) ?? 'jeton_invalide';
        throw code == 'jeton_expire'
            ? ReseauIndisponible(code)
            : SessionRefusee(code);
      }

      return (
        statut: reponse.statusCode,
        json: donnees,
        entetes: reponse.headers,
      );
    } on IOException catch (erreur) {
      throw ReseauIndisponible(erreur); // coupure, TLS, connexion refusée
    } on TimeoutException catch (erreur) {
      throw ReseauIndisponible(erreur);
    } on FormatException catch (erreur) {
      throw ReseauIndisponible(erreur); // page de portail captif, proxy
    }
  }

  Jetons _jetons(Map<String, Object?> json) => Jetons(
    json['jetonAcces']! as String,
    json['jetonRenouvellement']! as String,
  );

  /// Connexion par mot de passe (préparation, PIN oublié, après révocation).
  Future<Jetons> connecter(String email, String motDePasse) async {
    final r = await _requete(
      'POST',
      '/auth/login',
      json: {
        'email': email,
        'motDePasse': motDePasse,
        'appareilId': appareilId,
      },
    );
    if (r.statut != HttpStatus.ok) {
      throw SessionRefusee((r.json['code'] as String?) ?? 'identifiants');
    }

    return _jetons(r.json);
  }

  /// Renouvellement (rotation) ; 401 : session de l'appareil refusée.
  Future<Jetons> renouveler(String jetonRenouvellement) async {
    final r = await _requete(
      'POST',
      '/auth/refresh',
      json: {
        'jetonRenouvellement': jetonRenouvellement,
        'appareilId': appareilId,
      },
    );
    if (r.statut != HttpStatus.ok) {
      throw ReseauIndisponible('HTTP ${r.statut}');
    }

    return _jetons(r.json);
  }

  /// Adhésions relues : organisations accessibles DEPUIS CET APPAREIL.
  Future<Adhesions> lireAdhesions(String jetonAcces) async {
    final r = await _requete(
      'GET',
      '/me?appareil=${Uri.encodeQueryComponent(appareilId)}',
      jeton: jetonAcces,
    );
    if (r.statut != HttpStatus.ok) {
      throw ReseauIndisponible('HTTP ${r.statut}');
    }

    return Adhesions(
      DateTime.parse(r.json['heureServeur']! as String).millisecondsSinceEpoch,
      (r.json['organisations']! as List<Object?>).cast<String>().toSet(),
    );
  }

  /// Instantané de consultation d'une organisation.
  Future<List<Map<String, Object?>>> instantane(
    String jetonAcces,
    String organisationId,
  ) async {
    final r = await _requete(
      'GET',
      '/organizations/$organisationId/sync/snapshot',
      jeton: jetonAcces,
    );
    if (r.statut != HttpStatus.ok) {
      throw SessionRefusee((r.json['code'] as String?) ?? 'http_${r.statut}');
    }

    return (r.json['elements']! as List<Object?>).cast<Map<String, Object?>>();
  }

  /// Octets déjà reçus par le serveur pour ce fichier (reprise).
  Future<int> octetsRecus(String jetonAcces, String fichierId) async {
    final r = await _requete(
      'GET',
      '/sync/files/$fichierId',
      jeton: jetonAcces,
    );
    if (r.statut == HttpStatus.notFound) {
      return 0;
    }

    return r.json['recu']! as int;
  }

  /// Envoie la tranche [debut, debut + octets.length[ ; renvoie le total reçu.
  Future<int> envoyerMorceau(
    String jetonAcces,
    String fichierId,
    Uint8List octets, {
    required int debut,
    required int total,
    required String empreinte,
  }) async {
    final r = await _requete(
      'PUT',
      '/sync/files/$fichierId',
      jeton: jetonAcces,
      octets: octets,
      entetes: {
        'Content-Range': 'bytes $debut-${debut + octets.length - 1}/$total',
        'X-Empreinte': empreinte,
      },
    );
    if (r.statut == HttpStatus.conflict) {
      throw SessionRefusee((r.json['code'] as String?) ?? 'fichier_conflit');
    }

    if (r.statut != HttpStatus.ok) {
      throw ReseauIndisponible('HTTP ${r.statut}');
    }

    return r.json['recu']! as int;
  }

  Future<ReponseCommande> envoyerCommande(
    String jetonAcces,
    CommandeTablette commande,
  ) async {
    final r = await _requete(
      'POST',
      '/organizations/${commande.organisationId}/sync/commands',
      jeton: jetonAcces,
      json: commande.versServeur(appareilId),
    );
    if (r.statut != HttpStatus.ok) {
      return ReponseCommande(
        Resultat.rejected,
        code: (r.json['code'] as String?) ?? 'http_${r.statut}',
      );
    }

    final resultat = Resultat.values.asNameMap()[r.json['resultat']];
    if (resultat == null) {
      throw const ReseauIndisponible('résultat inconnu');
    }

    return ReponseCommande(resultat, code: r.json['code'] as String?);
  }

  void fermer() => _http.close(force: true);
}

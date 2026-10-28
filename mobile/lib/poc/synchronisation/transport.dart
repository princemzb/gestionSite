import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'commande.dart';

/// Résultat renvoyé par le serveur pour une commande.
enum Resultat { applied, alreadyApplied, conflict, rejected }

class ReponseServeur {
  const ReponseServeur(
    this.resultat, {
    this.version,
    this.texteCourant,
    this.code,
  });

  final Resultat resultat;
  final int? version;
  final String? texteCourant;
  final String? code;
}

/// Réseau absent, coupé, lent ou serveur indisponible : la commande reste en
/// file et sera renvoyée telle quelle (même identifiant d'opération).
class TransportIndisponible implements Exception {
  const TransportIndisponible(this.cause);

  final Object cause;

  @override
  String toString() => 'TransportIndisponible: $cause';
}

// ignore: one_member_abstracts
abstract interface class Transport {
  Future<ReponseServeur> envoyer(Commande commande, String jetonAcces);
}

/// Transport HTTP réel vers l'API (POC : route /poc/sync/…).
class TransportHttp implements Transport {
  TransportHttp(
    this.baseUrl, {
    HttpClient? client,
    this.delai = const Duration(seconds: 30),
  }) : _client =
           client ??
           (HttpClient()..connectionTimeout = const Duration(seconds: 10));

  final Uri baseUrl;
  final Duration delai;
  final HttpClient _client;

  /// Statuts passagers : la commande reste en file (session expirée à
  /// renouveler, délai dépassé, serveur surchargé ou en panne), jamais refusée.
  static bool _passager(int statut) =>
      statut == HttpStatus.unauthorized ||
      statut == HttpStatus.requestTimeout ||
      statut == HttpStatus.tooManyRequests ||
      statut >= 500;

  @override
  Future<ReponseServeur> envoyer(Commande commande, String jetonAcces) async {
    try {
      final requete = await _client
          .postUrl(
            baseUrl.resolve(
              '/poc/sync/organizations/${commande.organisationId}/commands',
            ),
          )
          .timeout(delai);
      requete.headers
        ..contentType = ContentType.json
        ..set(HttpHeaders.authorizationHeader, 'Bearer $jetonAcces');
      requete.write(jsonEncode(commande.versServeur()));
      final reponse = await requete.close().timeout(delai);
      final corps = await reponse.transform(utf8.decoder).join().timeout(delai);
      if (_passager(reponse.statusCode)) {
        throw TransportIndisponible('HTTP ${reponse.statusCode}');
      }

      final json = corps.isEmpty
          ? <String, Object?>{}
          : jsonDecode(corps) as Map<String, Object?>;
      if (reponse.statusCode != HttpStatus.ok) {
        return ReponseServeur(
          Resultat.rejected,
          code: (json['code'] as String?) ?? 'http_${reponse.statusCode}',
        );
      }

      return ReponseServeur(
        Resultat.values.byName(json['resultat']! as String),
        version: json['version'] as int?,
        texteCourant: json['texteCourant'] as String?,
      );
    } on IOException catch (erreur) {
      throw TransportIndisponible(erreur); // coupure, TLS, connexion refusée
    } on TimeoutException catch (erreur) {
      throw TransportIndisponible(erreur);
    } on FormatException catch (erreur) {
      throw TransportIndisponible(erreur); // page de portail captif, proxy
    } on TypeError catch (erreur) {
      throw TransportIndisponible(erreur); // réponse JSON inattendue
    } on ArgumentError catch (erreur) {
      throw TransportIndisponible(erreur); // résultat inconnu
    }
  }

  void fermer() => _client.close(force: true);
}

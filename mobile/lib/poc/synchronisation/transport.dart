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

/// Réseau absent, coupé ou serveur indisponible : la commande reste en file et
/// sera renvoyée telle quelle (même identifiant d'opération).
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
  TransportHttp(this.baseUrl, {HttpClient? client})
    : _client = client ?? HttpClient();

  final Uri baseUrl;
  final HttpClient _client;

  @override
  Future<ReponseServeur> envoyer(Commande commande, String jetonAcces) async {
    try {
      final requete = await _client.postUrl(
        baseUrl.resolve(
          '/poc/sync/organizations/${commande.organisationId}/commands',
        ),
      );
      requete.headers
        ..contentType = ContentType.json
        ..set(HttpHeaders.authorizationHeader, 'Bearer $jetonAcces');
      requete.write(jsonEncode(commande.versServeur()));
      final reponse = await requete.close();
      final corps = await reponse.transform(utf8.decoder).join();
      final json = corps.isEmpty
          ? <String, Object?>{}
          : jsonDecode(corps) as Map<String, Object?>;
      if (reponse.statusCode >= 500) {
        throw TransportIndisponible('HTTP ${reponse.statusCode}');
      }

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
    } on SocketException catch (erreur) {
      throw TransportIndisponible(erreur);
    } on HttpException catch (erreur) {
      throw TransportIndisponible(erreur);
    }
  }

  void fermer() => _client.close(force: true);
}

/// POC-03 (S04-01, expérimental) : interface Dart du greffon natif
/// `packages/coffre_appareil` (Keystore, horloge monotone, écran, photos).
library;

import 'package:flutter/services.dart';

/// Où vit une clé du Keystore : StrongBox (puce dédiée), TEE (zone isolée du
/// processeur), « matériel » (Android 11 : sécurisée sans précision),
/// logiciel (aucune protection matérielle) ou inconnu.
enum NiveauCle { strongbox, tee, materiel, logiciel, inconnu }

/// Clé absente du Keystore (détruite, ou jamais créée sur cet appareil).
class CleIntrouvable implements Exception {
  const CleIntrouvable();

  @override
  String toString() => 'CleIntrouvable';
}

/// Chiffré altéré, ou produit par une autre clé.
class DechiffrementRefuse implements Exception {
  const DechiffrementRefuse();

  @override
  String toString() => 'DechiffrementRefuse';
}

/// Clés non extractibles de l'appareil : elles chiffrent les clés des bases,
/// jamais exportées.
abstract interface class Coffre {
  /// Crée (ou remplace) la clé [alias]. Avec [deverrouillageExige], elle n'est
  /// utilisable que tablette déverrouillée.
  Future<NiveauCle> creerCle(String alias, {required bool deverrouillageExige});

  Future<Uint8List> chiffrer(String alias, Uint8List donnees);

  /// Lève [CleIntrouvable] ou [DechiffrementRefuse].
  Future<Uint8List> dechiffrer(String alias, Uint8List donnees);

  /// Destruction irréversible : tout ce qu'elle protégeait devient illisible.
  Future<void> supprimerCle(String alias);
}

/// Lecture des horloges : [monotoneMs] depuis le démarrage, sommeil compris,
/// non modifiable par l'utilisateur ; [demarrages] (compteur système, -1 si
/// inconnu) ; [muraleMs] heure de la tablette, modifiable.
class InstantAppareil {
  const InstantAppareil({
    required this.monotoneMs,
    required this.demarrages,
    required this.muraleMs,
  });

  final int monotoneMs;
  final int demarrages;
  final int muraleMs;
}

// ignore: one_member_abstracts
abstract interface class HorlogeAppareil {
  Future<InstantAppareil> lire();
}

class PhotoReduite {
  const PhotoReduite(this.octets, this.largeur, this.hauteur);

  final Uint8List octets;
  final int largeur;
  final int hauteur;
}

/// Photo illisible ou trop grande.
class PhotoRefusee implements Exception {
  const PhotoRefusee(this.motif);

  final String motif;

  @override
  String toString() => 'PhotoRefusee: $motif';
}

// ignore: one_member_abstracts
abstract interface class ReducteurPhoto {
  /// Réorientée, réduite à 2560 px au plus, JPEG qualité 85 sans métadonnées.
  Future<PhotoReduite> reduire(Uint8List octets);
}

class InfosAppareil {
  const InfosAppareil({
    required this.sdk,
    required this.fabricant,
    required this.modele,
    required this.appareilSecurise,
    required this.strongBoxDeclaree,
  });

  final int sdk;
  final String fabricant;
  final String modele;

  /// Verrouillage d'écran (PIN, schéma, mot de passe) configuré sur la tablette.
  final bool appareilSecurise;
  final bool strongBoxDeclaree;
}

/// Implémentation Android par canaux de méthode.
class PlateformeAndroid implements Coffre, HorlogeAppareil, ReducteurPhoto {
  const PlateformeAndroid();

  static const canal = MethodChannel('fr.gestionsite/coffre');
  static const canalEcran = EventChannel('fr.gestionsite/coffre/ecran');

  Future<InfosAppareil> infos() async {
    final m = (await canal.invokeMapMethod<String, Object?>('infos'))!;
    return InfosAppareil(
      sdk: m['sdk']! as int,
      fabricant: m['fabricant']! as String,
      modele: m['modele']! as String,
      appareilSecurise: m['appareilSecurise']! as bool,
      strongBoxDeclaree: m['strongBoxDeclaree']! as bool,
    );
  }

  /// Dossier privé de l'application, exclu des sauvegardes.
  Future<String> dossier() async =>
      (await canal.invokeMethod<String>('dossier'))!;

  /// Un événement à chaque extinction de l'écran : instant de la diffusion sur
  /// l'horloge monotone (ms).
  Stream<int> get ecranEteint =>
      canalEcran.receiveBroadcastStream().map((instant) => instant! as int);

  @override
  Future<InstantAppareil> lire() async {
    final m = (await canal.invokeMapMethod<String, Object?>('horloge'))!;
    return InstantAppareil(
      monotoneMs: m['monotoneMs']! as int,
      demarrages: m['demarrages']! as int,
      muraleMs: m['muraleMs']! as int,
    );
  }

  @override
  Future<NiveauCle> creerCle(
    String alias, {
    required bool deverrouillageExige,
  }) async {
    final niveau = await _appeler<String>('creerCle', {
      'alias': alias,
      'deverrouillageExige': deverrouillageExige,
    });
    return NiveauCle.values.byName(niveau!);
  }

  @override
  Future<Uint8List> chiffrer(String alias, Uint8List donnees) async =>
      (await _appeler<Uint8List>('chiffrer', {
        'alias': alias,
        'donnees': donnees,
      }))!;

  /// Copie modifiable : le canal rend une vue en lecture seule, que
  /// `effacer` ne pourrait pas remettre à zéro (constaté sur l'émulateur).
  @override
  Future<Uint8List> dechiffrer(String alias, Uint8List donnees) async =>
      Uint8List.fromList(
        (await _appeler<Uint8List>('dechiffrer', {
          'alias': alias,
          'donnees': donnees,
        }))!,
      );

  @override
  Future<void> supprimerCle(String alias) =>
      _appeler<void>('supprimerCle', {'alias': alias});

  @override
  Future<PhotoReduite> reduire(Uint8List octets) async {
    final m = (await _appeler<Map<Object?, Object?>>('reduirePhoto', {
      'octets': octets,
      'coteMax': 2560,
      'qualite': 85,
    }))!;
    return PhotoReduite(
      m['octets']! as Uint8List,
      m['largeur']! as int,
      m['hauteur']! as int,
    );
  }

  Future<T?> _appeler<T>(String methode, Map<String, Object?> arguments) async {
    try {
      return await canal.invokeMethod<T>(methode, arguments);
    } on PlatformException catch (erreur) {
      switch (erreur.code) {
        case 'cle_absente':
          throw const CleIntrouvable();
        case 'dechiffrement':
          throw const DechiffrementRefuse();
        case 'argument' when methode == 'reduirePhoto':
          throw PhotoRefusee(erreur.message ?? 'photo refusée');
        default:
          rethrow;
      }
    }
  }
}

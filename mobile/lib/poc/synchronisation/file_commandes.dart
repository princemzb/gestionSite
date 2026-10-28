import 'dart:convert';
import 'dart:io';

import 'commande.dart';

/// File de commandes d'un profil : un fichier par compte, jamais partagé entre
/// collègues d'une même tablette. Écriture atomique (fichier temporaire puis
/// renommage) : une coupure d'alimentation laisse l'ancienne ou la nouvelle file,
/// jamais un mélange. Limite du POC : fichier non chiffré (chiffrement par profil
/// à démontrer avec POC-03).
class FileCommandes {
  FileCommandes._(this.acteurId, this._fichier, this._commandes);

  final String acteurId;
  final File _fichier;
  final List<Commande> _commandes;

  /// Écritures sérialisées : saisie et synchronisation simultanées ne se chevauchent pas.
  Future<void> _derniereEcriture = Future<void>.value();

  /// Ouvre (ou crée) la file du profil [acteurId] dans [dossier].
  static Future<FileCommandes> ouvrir(
    Directory dossier,
    String acteurId,
  ) async {
    if (!RegExp(r'^[0-9a-fA-F-]{36}$').hasMatch(acteurId)) {
      throw ArgumentError.value(
        acteurId,
        'acteurId',
        'identifiant de compte attendu',
      );
    }

    final fichier = File('${dossier.path}/file_$acteurId.json');
    final commandes = <Commande>[];
    if (fichier.existsSync()) {
      final contenu = jsonDecode(await fichier.readAsString()) as List<Object?>;
      commandes.addAll(
        contenu.map((c) => Commande.fromJson(c! as Map<String, Object?>)),
      );
    }

    return FileCommandes._(acteurId, fichier, commandes);
  }

  List<Commande> get commandes => List.unmodifiable(_commandes);

  List<Commande> get enAttente =>
      _commandes.where((c) => c.etat == EtatCommande.enAttente).toList();

  /// Ajoute une commande saisie par le titulaire du profil, durablement avant
  /// tout envoi (« enregistré sur la tablette »).
  Future<void> ajouter(Commande commande) async {
    if (commande.acteurId != acteurId) {
      throw StateError('Commande d\'un autre auteur refusée dans ce profil.');
    }

    await _modifier((liste) => liste.add(commande));
  }

  Future<void> retirer(String operationId) => _modifier(
    (liste) => liste.removeWhere((c) => c.operationId == operationId),
  );

  Future<void> remplacer(Commande commande) => _modifier((liste) {
    final index = liste.indexWhere(
      (c) => c.operationId == commande.operationId,
    );
    liste[index] = commande;
  });

  /// Applique le changement à une copie, l'écrit durablement, puis seulement
  /// l'adopte en mémoire : un échec d'écriture laisse mémoire et disque identiques.
  Future<void> _modifier(void Function(List<Commande>) changement) {
    final ecriture = _derniereEcriture.then((_) async {
      final nouvelle = [..._commandes];
      changement(nouvelle);
      final temporaire = File('${_fichier.path}.tmp');
      await temporaire.writeAsString(
        jsonEncode(nouvelle.map((c) => c.toJson()).toList()),
        flush: true,
      );
      await temporaire.rename(_fichier.path);
      _commandes
        ..clear()
        ..addAll(nouvelle);
    });
    _derniereEcriture = ecriture.catchError((Object _) {});
    return ecriture;
  }
}

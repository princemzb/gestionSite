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

    _commandes.add(commande);
    await _enregistrer();
  }

  Future<void> retirer(String operationId) async {
    _commandes.removeWhere((c) => c.operationId == operationId);
    await _enregistrer();
  }

  Future<void> remplacer(Commande commande) async {
    final index = _commandes.indexWhere(
      (c) => c.operationId == commande.operationId,
    );
    _commandes[index] = commande;
    await _enregistrer();
  }

  Future<void> _enregistrer() async {
    final temporaire = File('${_fichier.path}.tmp');
    await temporaire.writeAsString(
      jsonEncode(_commandes.map((c) => c.toJson()).toList()),
      flush: true,
    );
    await temporaire.rename(_fichier.path);
  }
}

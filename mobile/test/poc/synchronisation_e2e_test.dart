// POC PREP-06 (expérimental) : scénario de bout en bout contre l'API réelle.
// Lancé par scripts/poc/prep06.sh, qui prépare l'API et fournit les variables ;
// ignoré dans les autres exécutions.
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/synchronisation/commande.dart';
import 'package:gestionsite_terrain/poc/synchronisation/file_commandes.dart';
import 'package:gestionsite_terrain/poc/synchronisation/identifiants.dart';
import 'package:gestionsite_terrain/poc/synchronisation/synchroniseur.dart';
import 'package:gestionsite_terrain/poc/synchronisation/transport.dart';

/// Transport réel dont la réponse est perdue une fois, APRÈS validation serveur.
class CoupureApresValidation implements Transport {
  CoupureApresValidation(this.reel);

  final Transport reel;
  bool coupe = true;

  @override
  Future<ReponseServeur> envoyer(Commande commande, String jetonAcces) async {
    final reponse = await reel.envoyer(commande, jetonAcces);
    if (coupe) {
      coupe = false;
      throw const TransportIndisponible('réponse perdue après validation');
    }

    return reponse;
  }
}

void main() {
  final env = Platform.environment;
  final api = env['PREP06_API'];
  final ignorer = api == null
      ? 'scénario lancé par scripts/poc/prep06.sh'
      : false;

  String v(String cle) => env[cle]!;
  void rapport(String ligne) => stdout.writeln('RAPPORT | $ligne');

  Commande nouvelle(
    String acteur,
    String note,
    int base,
    String texte,
    String appareil,
  ) => Commande(
    operationId: nouvelIdentifiant(),
    deviceId: appareil,
    acteurId: acteur,
    organisationId: v('PREP06_ORGANISATION'),
    siteId: v('PREP06_SITE'),
    noteId: note,
    baseVersion: base,
    texte: texte,
    observeLe: DateTime.now().toUtc(),
  );

  test('PREP-06 contre l\'API réelle', () async {
    final transport = TransportHttp(Uri.parse(api!));
    final dossier = Directory.systemTemp.createTempSync('prep06_e2e_');
    final (marc, jetonMarc) = (v('PREP06_MARC'), v('PREP06_MARC_JETON'));
    final (lea, jetonLea) = (v('PREP06_LEA'), v('PREP06_LEA_JETON'));
    try {
      // 1. Coupure après validation : la réponse se perd, le renvoi identique est reconnu.
      final fileMarc = await FileCommandes.ouvrir(dossier, marc);
      final note = nouvelIdentifiant();
      await fileMarc.ajouter(
        nouvelle(marc, note, 0, 'Fuite constatée', 'tablette-1'),
      );
      final coupure = await Synchroniseur(
        fileMarc,
        CoupureApresValidation(transport),
        jetonMarc,
      ).synchroniser();
      final reprise = await Synchroniseur(
        fileMarc,
        transport,
        jetonMarc,
      ).synchroniser();
      expect(
        (coupure.interrompu, reprise.appliquees, fileMarc.commandes.length),
        (true, 1, 0),
      );
      rapport(
        'Coupure après validation : réponse perdue, commande gardée, renvoi reconnu (alreadyApplied), file vide — OK',
      );

      // 2. Doublon : même opération envoyée trois fois.
      final doublon = nouvelle(
        marc,
        nouvelIdentifiant(),
        0,
        'Doublon',
        'tablette-1',
      );
      final resultats = [
        for (var i = 0; i < 3; i++)
          (await transport.envoyer(doublon, jetonMarc)).resultat,
      ];
      expect(resultats, [
        Resultat.applied,
        Resultat.alreadyApplied,
        Resultat.alreadyApplied,
      ]);
      rapport('Doublon : 3 envois, 1 application ($resultats) — OK');

      // 3. Deux profils sur la même tablette : files séparées, chacun sa session.
      final fileLea = await FileCommandes.ouvrir(dossier, lea);
      await fileLea.ajouter(
        nouvelle(lea, nouvelIdentifiant(), 0, 'Saisie de Léa', 'tablette-1'),
      );
      await fileMarc.ajouter(
        nouvelle(marc, nouvelIdentifiant(), 0, 'Saisie de Marc', 'tablette-1'),
      );
      await Synchroniseur(fileMarc, transport, jetonMarc).synchroniser();
      expect(fileLea.enAttente, hasLength(1));
      final sousMauvaiseSession = await transport.envoyer(
        fileLea.enAttente.single,
        jetonMarc,
      );
      expect(
        (sousMauvaiseSession.resultat, sousMauvaiseSession.code),
        (Resultat.rejected, 'acteur_different'),
      );
      expect(
        (await Synchroniseur(
          fileLea,
          transport,
          jetonLea,
        ).synchroniser()).appliquees,
        1,
      );
      rapport(
        'Deux profils : file de Léa intacte après la synchro de Marc ; envoi sous la session de Marc refusé (acteur_different) — OK',
      );

      // 4. Conflit : deux appareils modifient la même note à partir de la version 1.
      final partagee = nouvelIdentifiant();
      expect(
        (await transport.envoyer(
          nouvelle(marc, partagee, 0, 'Version initiale', 'tablette-1'),
          jetonMarc,
        )).resultat,
        Resultat.applied,
      );
      await fileMarc.ajouter(
        nouvelle(marc, partagee, 1, 'Bâche posée', 'tablette-1'),
      );
      await fileLea.ajouter(
        nouvelle(lea, partagee, 1, 'Seau installé', 'tablette-2'),
      );
      await Synchroniseur(fileMarc, transport, jetonMarc).synchroniser();
      final bilanLea = await Synchroniseur(
        fileLea,
        transport,
        jetonLea,
      ).synchroniser();
      expect(bilanLea.conflits, 1);
      expect(fileLea.commandes.single.etat, EtatCommande.conflit);
      rapport(
        'Conflit : saisie de Léa conservée en conflit (${fileLea.commandes.single.detail}), version de Marc intacte — OK',
      );
    } finally {
      transport.fermer();
      dossier.deleteSync(recursive: true);
    }
  }, skip: ignorer);
}

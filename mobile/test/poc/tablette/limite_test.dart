import 'package:flutter_test/flutter_test.dart';
import 'package:gestionsite_terrain/poc/tablette/limite.dart';
import 'package:gestionsite_terrain/poc/tablette/plateforme.dart';

const _h = 3600 * 1000;

InstantAppareil _instant(int monotone, int murale, {int demarrages = 7}) =>
    InstantAppareil(
      monotoneMs: monotone,
      demarrages: demarrages,
      muraleMs: murale,
    );

void main() {
  final contact = DernierContact.maintenant(
    5 * _h,
    _instant(10 * _h, 100 * _h),
  );

  test('même démarrage : temps monotone écoulé, heure garantie', () {
    final etat = evaluerLimite(
      contact: contact,
      pointage: null,
      maintenant: _instant(33 * _h, 123 * _h),
    );

    expect(etat.consomme, const Duration(hours: 23));
    expect(etat.expire, isFalse);
    expect(etat.restant, const Duration(hours: 1));
    expect(etat.heureGarantie, isTrue);
    expect(etat.pointage.consommeMs, 23 * _h);
  });

  test('reculer l\'heure de la tablette ne prolonge pas le délai', () {
    final etat = evaluerLimite(
      contact: contact,
      pointage: null,
      maintenant: _instant(
        35 * _h,
        90 * _h,
      ), // 25 h réelles, heure reculée de 10 h
    );

    expect(etat.expire, isTrue);
    expect(etat.restant, Duration.zero);
  });

  test(
    'avancer l\'heure raccourcit le délai, et le recul suivant ne rend rien',
    () {
      final avance = evaluerLimite(
        contact: contact,
        pointage: null,
        maintenant: _instant(11 * _h, 130 * _h), // +1 h réelle, +30 h affichées
      );
      expect(avance.expire, isTrue);

      final recul = evaluerLimite(
        contact: contact,
        pointage: avance.pointage,
        maintenant: _instant(12 * _h, 102 * _h),
      );
      expect(recul.expire, isTrue);
    },
  );

  test('redémarrage : pointage précédent + temps depuis le démarrage', () {
    final avant = evaluerLimite(
      contact: contact,
      pointage: null,
      maintenant: _instant(30 * _h, 120 * _h), // 20 h consommées
    );

    // Redémarré (compteur 8), monotone repart de zéro ; heure reculée de 2 jours.
    final apres = evaluerLimite(
      contact: contact,
      pointage: avant.pointage,
      maintenant: _instant(3 * _h, 75 * _h, demarrages: 8),
    );
    expect(apres.consomme, const Duration(hours: 23));
    expect(apres.heureGarantie, isFalse);

    final ensuite = evaluerLimite(
      contact: contact,
      pointage: apres.pointage,
      maintenant: _instant(5 * _h, 77 * _h, demarrages: 8),
    );
    expect(ensuite.consomme, const Duration(hours: 25));
    expect(ensuite.expire, isTrue);
  });

  test(
    'redémarrage sans pointage : seul le temps depuis le démarrage compte',
    () {
      final etat = evaluerLimite(
        contact: contact,
        pointage: null,
        maintenant: _instant(2 * _h, 50 * _h, demarrages: 8),
      );

      expect(etat.consomme, const Duration(hours: 2));
      expect(etat.heureGarantie, isFalse);
    },
  );

  test('compteur de démarrages inconnu : heure non garantie, temps compté '
      'une seule fois d\'après le monotone', () {
    final inconnu = DernierContact.maintenant(
      0,
      _instant(10 * _h, 100 * _h, demarrages: -1),
    );
    final etat = evaluerLimite(
      contact: inconnu,
      pointage: null,
      maintenant: _instant(12 * _h, 102 * _h, demarrages: -1),
    );
    expect(etat.heureGarantie, isFalse);
    expect(etat.consomme, const Duration(hours: 2));

    // Évaluations répétées : jamais de double comptage.
    final ensuite = evaluerLimite(
      contact: inconnu,
      pointage: etat.pointage,
      maintenant: _instant(13 * _h, 103 * _h, demarrages: -1),
    );
    expect(ensuite.consomme, const Duration(hours: 3));

    // Monotone revenu en arrière : redémarrage, pointage + temps depuis.
    final redemarre = evaluerLimite(
      contact: inconnu,
      pointage: ensuite.pointage,
      maintenant: _instant(1 * _h, 60 * _h, demarrages: -1),
    );
    expect(redemarre.consomme, const Duration(hours: 4));
  });

  test('sérialisation du contact et du pointage', () {
    final copie = DernierContact.depuisJson(contact.versJson());
    expect(copie.versJson(), contact.versJson());

    const pointage = Pointage(demarrages: 1, monotoneMs: 2, consommeMs: 3);
    expect(
      Pointage.depuisJson(pointage.versJson()).versJson(),
      pointage.versJson(),
    );
  });
}

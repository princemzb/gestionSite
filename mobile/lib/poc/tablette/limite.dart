/// POC-03 (S04-01, expérimental) : limite hors connexion de 24 h (RG-23,
/// R13-04), comptée depuis le dernier contact réussi du profil (renouvellement
/// accepté ET adhésion relue).
///
/// L'heure de la tablette est modifiable par l'utilisateur : elle ne peut que
/// raccourcir le délai, jamais le prolonger. Le temps consommé est mesuré avec
/// l'horloge monotone (`elapsedRealtime`, sommeil compris). Après un
/// redémarrage, l'horloge monotone repart de zéro : on reprend le dernier
/// pointage enregistré, le temps « éteint » n'est alors connu que par l'heure
/// de la tablette (heure marquée non garantie, R14-06).
library;

import 'plateforme.dart';

/// Relevé pris au dernier contact réussi.
class DernierContact {
  const DernierContact({
    required this.serveurMs,
    required this.monotoneMs,
    required this.demarrages,
    required this.muraleMs,
  });

  factory DernierContact.depuisJson(Map<String, Object?> json) =>
      DernierContact(
        serveurMs: json['serveurMs']! as int,
        monotoneMs: json['monotoneMs']! as int,
        demarrages: json['demarrages']! as int,
        muraleMs: json['muraleMs']! as int,
      );

  factory DernierContact.maintenant(int serveurMs, InstantAppareil instant) =>
      DernierContact(
        serveurMs: serveurMs,
        monotoneMs: instant.monotoneMs,
        demarrages: instant.demarrages,
        muraleMs: instant.muraleMs,
      );

  /// Heure du serveur au contact (base de l'heure de saisie, R14-06).
  final int serveurMs;
  final int monotoneMs;
  final int demarrages;
  final int muraleMs;

  Map<String, Object?> versJson() => {
    'serveurMs': serveurMs,
    'monotoneMs': monotoneMs,
    'demarrages': demarrages,
    'muraleMs': muraleMs,
  };
}

/// Temps déjà consommé, relevé à chaque évaluation : il survit à un
/// redémarrage de la tablette.
class Pointage {
  const Pointage({
    required this.demarrages,
    required this.monotoneMs,
    required this.consommeMs,
  });

  factory Pointage.depuisJson(Map<String, Object?> json) => Pointage(
    demarrages: json['demarrages']! as int,
    monotoneMs: json['monotoneMs']! as int,
    consommeMs: json['consommeMs']! as int,
  );

  final int demarrages;
  final int monotoneMs;
  final int consommeMs;

  Map<String, Object?> versJson() => {
    'demarrages': demarrages,
    'monotoneMs': monotoneMs,
    'consommeMs': consommeMs,
  };
}

class EtatLimite {
  const EtatLimite({
    required this.consomme,
    required this.limite,
    required this.heureGarantie,
    required this.pointage,
  });

  final Duration consomme;
  final Duration limite;

  /// Faux après un redémarrage hors réseau (R14-06).
  final bool heureGarantie;

  /// À enregistrer pour la prochaine évaluation.
  final Pointage pointage;

  bool get expire => consomme >= limite;

  Duration get restant => expire ? Duration.zero : limite - consomme;
}

const limiteHorsConnexion = Duration(hours: 24);

bool _memeDemarrage(int a, int b) => a >= 0 && a == b;

/// Évalue le temps hors connexion consommé depuis [contact].
EtatLimite evaluerLimite({
  required DernierContact contact,
  required Pointage? pointage,
  required InstantAppareil maintenant,
  Duration limite = limiteHorsConnexion,
}) {
  final int monotone;
  final bool garantie;
  if (_memeDemarrage(maintenant.demarrages, contact.demarrages) &&
      maintenant.monotoneMs >= contact.monotoneMs) {
    monotone = maintenant.monotoneMs - contact.monotoneMs;
    garantie = true;
  } else {
    // Redémarré depuis le contact : dernier pointage + temps depuis le démarrage.
    final dejaCompte = pointage == null
        ? 0
        : _memeDemarrage(maintenant.demarrages, pointage.demarrages)
        ? pointage.consommeMs - pointage.monotoneMs
        : pointage.consommeMs;
    monotone = dejaCompte + maintenant.monotoneMs;
    garantie = false;
  }

  // L'heure de la tablette ne peut que raccourcir le délai (avancée), jamais
  // l'allonger (reculée : différence négative ou plus petite, ignorée).
  final mural = maintenant.muraleMs - contact.muraleMs;
  final precedent = pointage?.consommeMs ?? 0;
  final consomme = [monotone, mural, precedent].reduce((a, b) => a > b ? a : b);

  return EtatLimite(
    consomme: Duration(milliseconds: consomme),
    limite: limite,
    heureGarantie: garantie,
    pointage: Pointage(
      demarrages: maintenant.demarrages,
      monotoneMs: maintenant.monotoneMs,
      consommeMs: consomme,
    ),
  );
}

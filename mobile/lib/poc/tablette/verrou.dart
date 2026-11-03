/// POC-03 (S04-01, expérimental) : verrouillage automatique du profil ouvert
/// (R13-05) : 5 minutes sans activité, extinction de l'écran, ou 30 secondes
/// hors de l'application.
///
/// Les durées se mesurent sur l'horloge monotone de l'appareil : un processus
/// gelé en arrière-plan, ou l'heure changée, ne retarde pas le verrouillage,
/// vérifié au retour dans l'application.
library;

import 'dart:async';

import 'package:flutter/widgets.dart';

import 'plateforme.dart';

class VerrouAutomatique {
  VerrouAutomatique({
    required this.horloge,
    required this.verrouiller,
    Stream<void>? ecranEteint,
    this.inactivite = const Duration(minutes: 5),
    this.horsApplication = const Duration(seconds: 30),
  }) {
    _abonnement = ecranEteint?.listen((_) => _declencher('ecran_eteint'));
  }

  final HorlogeAppareil horloge;

  /// Appelé une fois par verrouillage, avec le motif.
  final void Function(String motif) verrouiller;
  final Duration inactivite;
  final Duration horsApplication;

  StreamSubscription<void>? _abonnement;
  Timer? _minuterie;
  int? _derniereActiviteMs;
  int? _sortieMs;
  bool _actif = false;

  /// Profil ouvert : la surveillance commence.
  Future<void> demarrer() async {
    _actif = true;
    await activite();
  }

  /// Profil fermé : plus rien à surveiller.
  void arreter() {
    _actif = false;
    _minuterie?.cancel();
    _sortieMs = null;
  }

  /// Toucher, saisie : l'inactivité repart de zéro.
  Future<void> activite() async {
    if (!_actif) {
      return;
    }

    _derniereActiviteMs = (await horloge.lire()).monotoneMs;
    _armer(inactivite, 'inactivite');
  }

  Future<void> cycleDeVie(AppLifecycleState etat) async {
    if (!_actif) {
      return;
    }

    switch (etat) {
      case AppLifecycleState.hidden:
      case AppLifecycleState.paused:
        _sortieMs ??= (await horloge.lire()).monotoneMs;
        _armer(horsApplication, 'hors_application');
      case AppLifecycleState.detached:
        _declencher('detache');
      case AppLifecycleState.resumed:
        final maintenant = (await horloge.lire()).monotoneMs;
        final sortie = _sortieMs;
        _sortieMs = null;
        if (sortie != null &&
            maintenant - sortie >= horsApplication.inMilliseconds) {
          _declencher('hors_application');
        } else if (_derniereActiviteMs != null &&
            maintenant - _derniereActiviteMs! >= inactivite.inMilliseconds) {
          _declencher('inactivite');
        } else {
          await activite();
        }
      case AppLifecycleState.inactive:
        break; // dialogue système, volet de notifications : rien à faire
    }
  }

  void _armer(Duration duree, String motif) {
    _minuterie?.cancel();
    _minuterie = Timer(duree, () => _declencher(motif));
  }

  void _declencher(String motif) {
    if (!_actif) {
      return;
    }

    arreter();
    verrouiller(motif);
  }

  Future<void> fermer() async {
    arreter();
    await _abonnement?.cancel();
  }
}

/// POC-03 (S04-01, expérimental) : verrouillage automatique du profil ouvert
/// (R13-05, révisée par le porteur le 03/11/2026) :
/// - 5 minutes sans activité dans l'application ;
/// - 5 minutes de grâce après l'extinction de l'écran ou après avoir quitté
///   l'application (au lieu d'immédiat et de 30 s).
///
/// Les durées se mesurent sur l'horloge monotone de l'appareil et sont
/// contrôlées au retour dans l'application : Android peut geler le processus
/// en arrière-plan (constaté sur la Lenovo Tab M11, la minuterie ne s'y
/// déclenche pas), et l'heure de la tablette peut être changée. La minuterie
/// reste armée pour verrouiller au plus tôt quand le processus n'est pas gelé.
///
/// Le contenu de l'écran est masqué dès la sortie (aperçu des applications
/// récentes vide, captures refusées) par `FLAG_SECURE`, posé dans
/// `MainActivity` : la grâce ne laisse donc rien voir hors de l'application.
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
    this.grace = const Duration(minutes: 5),
  }) {
    _abonnement = ecranEteint?.listen((_) => _sortir('ecran_eteint'));
  }

  final HorlogeAppareil horloge;

  /// Appelé une fois par verrouillage, avec le motif.
  final void Function(String motif) verrouiller;

  /// Sans toucher l'écran, application au premier plan.
  final Duration inactivite;

  /// Après l'extinction de l'écran ou la sortie de l'application.
  final Duration grace;

  StreamSubscription<void>? _abonnement;
  Timer? _minuterie;
  int? _derniereActiviteMs;
  int? _sortieMs;
  String? _motifSortie;
  bool _actif = false;

  /// Profil ouvert : la surveillance commence.
  Future<void> demarrer() async {
    _actif = true;
    _sortieMs = null;
    _motifSortie = null;
    await activite();
  }

  /// Profil fermé : plus rien à surveiller.
  void arreter() {
    _actif = false;
    _minuterie?.cancel();
    _sortieMs = null;
    _motifSortie = null;
  }

  /// Toucher, saisie : l'inactivité repart de zéro (sauf hors application).
  Future<void> activite() async {
    if (!_actif || _sortieMs != null) {
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
        await _sortir('hors_application');
      case AppLifecycleState.detached:
        _declencher('detache');
      case AppLifecycleState.resumed:
        await _revenir();
      case AppLifecycleState.inactive:
        break; // dialogue système, volet de notifications : rien à faire
    }
  }

  /// Écran éteint ou application quittée : la grâce court depuis la première
  /// sortie ; l'inactivité est suspendue. Un écran éteint pendant la sortie
  /// ne repousse pas l'échéance.
  Future<void> _sortir(String motif) async {
    if (!_actif) {
      return;
    }

    if (motif == 'ecran_eteint') {
      _motifSortie = motif;
    }
    if (_sortieMs != null) {
      return;
    }

    _motifSortie ??= motif;
    _sortieMs = -1; // réservé avant la lecture asynchrone de l'horloge
    final instant = (await horloge.lire()).monotoneMs;
    if (!_actif || _sortieMs != -1) {
      return; // revenu ou verrouillé pendant la lecture
    }

    _sortieMs = instant;
    _armer(grace, null);
  }

  /// Retour au premier plan : contrôle par l'horloge monotone, la minuterie
  /// ayant pu ne pas se déclencher (processus gelé).
  Future<void> _revenir() async {
    final maintenant = (await horloge.lire()).monotoneMs;
    if (!_actif) {
      return;
    }

    final sortie = _sortieMs;
    final motif = _motifSortie;
    _sortieMs = null;
    _motifSortie = null;
    if (sortie != null && sortie >= 0) {
      if (maintenant - sortie >= grace.inMilliseconds) {
        _declencher(motif ?? 'hors_application');
        return;
      }
    } else if (sortie == null &&
        _derniereActiviteMs != null &&
        maintenant - _derniereActiviteMs! >= inactivite.inMilliseconds) {
      _declencher('inactivite');
      return;
    }

    // Revenu avant la fin de la grâce : le retour compte comme une activité.
    await activite();
  }

  /// [motif] nul : celui de la sortie en cours.
  void _armer(Duration duree, String? motif) {
    _minuterie?.cancel();
    _minuterie = Timer(
      duree,
      () => _declencher(motif ?? _motifSortie ?? 'hors_application'),
    );
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

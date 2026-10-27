import 'package:flutter/material.dart';

void main() {
  runApp(const GestionSiteTerrainApp());
}

/// Application terrain (tablettes Android et iPad).
///
/// Écran technique provisoire, volontairement sans design : les écrans réels
/// attendent la validation des maquettes par le porteur du projet.
class GestionSiteTerrainApp extends StatelessWidget {
  const GestionSiteTerrainApp({super.key});

  static const title = 'gestionSite — terrain';

  @override
  Widget build(BuildContext context) {
    return const MaterialApp(
      title: title,
      home: Scaffold(
        body: Center(
          child: Text(
            '$title\nÉcran technique provisoire — maquettes en attente de validation',
            textAlign: TextAlign.center,
          ),
        ),
      ),
    );
  }
}

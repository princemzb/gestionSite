package fr.gestionsite.gestionsite_terrain

import android.os.Bundle
import android.view.WindowManager
import io.flutter.embedding.android.FlutterActivity

/**
 * Contenu protégé (R13-05 révisée le 03/11/2026) : `FLAG_SECURE` posé avant le premier affichage,
 * pour toute la durée de l'activité. Android n'affiche alors qu'un aperçu vide dans les applications
 * récentes et refuse captures et enregistrements d'écran : pendant la grâce de 5 minutes après la
 * sortie, rien du profil n'est visible hors de l'application. Le poser seulement à la sortie serait
 * trop tard (l'aperçu est pris au moment où l'application passe en arrière-plan).
 */
class MainActivity : FlutterActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        window.setFlags(WindowManager.LayoutParams.FLAG_SECURE, WindowManager.LayoutParams.FLAG_SECURE)
        super.onCreate(savedInstanceState)
    }
}

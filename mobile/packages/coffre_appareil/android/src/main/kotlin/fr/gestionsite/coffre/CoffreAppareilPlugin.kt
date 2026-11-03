package fr.gestionsite.coffre

import android.app.Activity
import android.app.KeyguardManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.provider.Settings
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyInfo
import android.security.keystore.KeyProperties
import android.security.keystore.StrongBoxUnavailableException
import android.view.WindowManager
import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.embedding.engine.plugins.activity.ActivityAware
import io.flutter.embedding.engine.plugins.activity.ActivityPluginBinding
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.security.KeyStore
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec
import kotlin.math.max

/**
 * POC-03 (S04-01, expérimental) : services natifs dont la synchronisation hors connexion a besoin.
 *
 * - Clés AES-256-GCM non extractibles du Keystore Android, en StrongBox si l'appareil en a une,
 *   sinon dans l'environnement d'exécution sécurisé (TEE). Elles chiffrent les clés des bases, jamais les données.
 * - Horloge monotone (`elapsedRealtime`, sommeil compris) et compteur de démarrages : la limite de 24 h
 *   ne dépend pas de l'heure de la tablette, modifiable par l'utilisateur.
 * - Événement « écran éteint » : début de la grâce de 5 minutes avant le verrouillage du profil.
 * - Contenu protégé (`FLAG_SECURE`, posé par `MainActivity`) : lu ici pour les essais.
 * - Réduction des photos à 2560 px, réorientées, réencodées en JPEG sans métadonnées (comme le serveur).
 *
 * Les opérations lentes (Keystore, StrongBox, décodage d'image) tournent hors du fil principal.
 */
class CoffreAppareilPlugin : FlutterPlugin, ActivityAware, MethodChannel.MethodCallHandler, EventChannel.StreamHandler {
    private lateinit var contexte: Context
    /** Absente dans le moteur d'arrière-plan de WorkManager. */
    private var activite: Activity? = null
    private lateinit var canal: MethodChannel
    private lateinit var canalEcran: EventChannel
    private lateinit var travail: ExecutorService
    private val principal = Handler(Looper.getMainLooper())
    private var recepteurEcran: BroadcastReceiver? = null

    override fun onAttachedToEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        contexte = binding.applicationContext
        travail = Executors.newSingleThreadExecutor()
        canal = MethodChannel(binding.binaryMessenger, "fr.gestionsite/coffre")
        canal.setMethodCallHandler(this)
        canalEcran = EventChannel(binding.binaryMessenger, "fr.gestionsite/coffre/ecran")
        canalEcran.setStreamHandler(this)
    }

    override fun onDetachedFromEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        canal.setMethodCallHandler(null)
        canalEcran.setStreamHandler(null)
        onCancel(null)
        travail.shutdown()
    }

    override fun onAttachedToActivity(binding: ActivityPluginBinding) {
        activite = binding.activity
    }

    override fun onDetachedFromActivityForConfigChanges() {
        activite = null
    }

    override fun onReattachedToActivityForConfigChanges(binding: ActivityPluginBinding) {
        activite = binding.activity
    }

    override fun onDetachedFromActivity() {
        activite = null
    }

    override fun onMethodCall(call: MethodCall, result: MethodChannel.Result) {
        when (call.method) {
            "infos" -> result.success(infos())
            // null : aucune fenêtre (moteur d'arrière-plan).
            "contenuMasque" -> result.success(
                activite?.window?.attributes?.flags?.let { it and WindowManager.LayoutParams.FLAG_SECURE != 0 },
            )
            "horloge" -> result.success(horloge())
            // Dossier privé de l'application exclu de toute sauvegarde (allowBackup=false en plus).
            "dossier" -> result.success(contexte.noBackupFilesDir.absolutePath)
            else -> travail.execute { executer(call, result) }
        }
    }

    private fun executer(call: MethodCall, result: MethodChannel.Result) {
        try {
            val valeur: Any? = when (call.method) {
                "creerCle" -> creerCle(alias(call), call.argument<Boolean>("deverrouillageExige") == true)
                "niveauCle" -> niveau(cle(alias(call)))
                "chiffrer" -> chiffrer(alias(call), octets(call, "donnees"))
                "dechiffrer" -> dechiffrer(alias(call), octets(call, "donnees"))
                "supprimerCle" -> {
                    keystore().deleteEntry(alias(call))
                    null
                }
                "reduirePhoto" -> reduirePhoto(
                    octets(call, "octets"),
                    call.argument<Int>("coteMax") ?: COTE_MAX,
                    call.argument<Int>("qualite") ?: QUALITE,
                )
                else -> {
                    principal.post { result.notImplemented() }
                    return
                }
            }
            principal.post { result.success(valeur) }
        } catch (erreur: CleAbsente) {
            principal.post { result.error("cle_absente", erreur.message, null) }
        } catch (erreur: javax.crypto.AEADBadTagException) {
            principal.post { result.error("dechiffrement", "Données altérées ou clé différente.", null) }
        } catch (erreur: IllegalArgumentException) {
            principal.post { result.error("argument", erreur.message, null) }
        } catch (erreur: java.security.GeneralSecurityException) {
            // Message seul : jamais de donnée ni de clé dans les journaux.
            principal.post { result.error("keystore", erreur.javaClass.simpleName, null) }
        } catch (erreur: java.security.ProviderException) {
            principal.post { result.error("keystore", erreur.javaClass.simpleName, null) }
        } catch (erreur: java.io.IOException) {
            // EXIF illisible : photo refusée, comme une photo indécodable.
            principal.post { result.error("argument", "photo illisible", null) }
        } catch (erreur: OutOfMemoryError) {
            principal.post { result.error("argument", "photo trop grande", null) }
        } catch (erreur: RuntimeException) {
            // Toujours une réponse : l'appel Dart ne reste jamais en attente, le processus ne s'arrête pas.
            principal.post { result.error("interne", erreur.javaClass.simpleName, null) }
        }
    }

    private fun infos(): Map<String, Any?> {
        val garde = contexte.getSystemService(Context.KEYGUARD_SERVICE) as KeyguardManager
        return mapOf(
            "sdk" to Build.VERSION.SDK_INT,
            "fabricant" to Build.MANUFACTURER,
            "modele" to Build.MODEL,
            "appareilSecurise" to garde.isDeviceSecure,
            "strongBoxDeclaree" to contexte.packageManager.hasSystemFeature(PackageManager.FEATURE_STRONGBOX_KEYSTORE),
        )
    }

    private fun horloge(): Map<String, Any?> = mapOf(
        "monotoneMs" to SystemClock.elapsedRealtime(),
        "demarrages" to Settings.Global.getInt(contexte.contentResolver, Settings.Global.BOOT_COUNT, -1),
        "muraleMs" to System.currentTimeMillis(),
    )

    private fun alias(call: MethodCall): String {
        val alias = call.argument<String>("alias") ?: throw IllegalArgumentException("alias absent")
        require(alias.matches(Regex("^poc03\\.[A-Za-z0-9._-]{1,120}$"))) { "alias invalide" }
        return alias
    }

    private fun octets(call: MethodCall, nom: String): ByteArray =
        call.argument<ByteArray>(nom) ?: throw IllegalArgumentException("$nom absent")

    private fun keystore(): KeyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }

    private fun cle(alias: String): SecretKey =
        keystore().getKey(alias, null) as? SecretKey ?: throw CleAbsente(alias)

    /** Crée (ou remplace) la clé : StrongBox d'abord si l'appareil la déclare, sinon TEE. */
    private fun creerCle(alias: String, deverrouillageExige: Boolean): String {
        keystore().deleteEntry(alias)
        val strongBox = contexte.packageManager.hasSystemFeature(PackageManager.FEATURE_STRONGBOX_KEYSTORE)
        if (strongBox) {
            try {
                generer(alias, deverrouillageExige, true)
                return niveau(cle(alias))
            } catch (_: StrongBoxUnavailableException) {
                // StrongBox déclarée mais indisponible : repli sur le TEE, signalé par le niveau renvoyé.
            }
        }
        generer(alias, deverrouillageExige, false)
        return niveau(cle(alias))
    }

    private fun generer(alias: String, deverrouillageExige: Boolean, strongBox: Boolean) {
        val spec = KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .setRandomizedEncryptionRequired(true)
            // Clé de la base de consultation : inutilisable tant que la tablette est verrouillée.
            // Clé de la file d'envoi : utilisable écran verrouillé, pour l'envoi en arrière-plan.
            .setUnlockedDeviceRequired(deverrouillageExige)
            .setIsStrongBoxBacked(strongBox)
            .build()
        KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE).apply { init(spec) }.generateKey()
    }

    @Suppress("DEPRECATION") // isInsideSecureHardware : seule information disponible sous Android 12.
    private fun niveau(cle: SecretKey): String {
        val info = SecretKeyFactory.getInstance(cle.algorithm, KEYSTORE).getKeySpec(cle, KeyInfo::class.java) as KeyInfo
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            return when (info.securityLevel) {
                KeyProperties.SECURITY_LEVEL_STRONGBOX -> "strongbox"
                KeyProperties.SECURITY_LEVEL_TRUSTED_ENVIRONMENT -> "tee"
                KeyProperties.SECURITY_LEVEL_SOFTWARE -> "logiciel"
                KeyProperties.SECURITY_LEVEL_UNKNOWN_SECURE -> "materiel"
                else -> "inconnu"
            }
        }
        return if (info.isInsideSecureHardware) "materiel" else "logiciel"
    }

    /** IV de 12 octets tiré par le Keystore, placé devant le chiffré (étiquette GCM de 16 octets incluse). */
    private fun chiffrer(alias: String, donnees: ByteArray): ByteArray {
        val chiffre = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, cle(alias)) }
        return chiffre.iv + chiffre.doFinal(donnees)
    }

    private fun dechiffrer(alias: String, donnees: ByteArray): ByteArray {
        require(donnees.size > IV + 16) { "chiffré trop court" }
        val chiffre = Cipher.getInstance(TRANSFORMATION)
        chiffre.init(Cipher.DECRYPT_MODE, cle(alias), GCMParameterSpec(128, donnees, 0, IV))
        return chiffre.doFinal(donnees, IV, donnees.size - IV)
    }

    /** Comme TraitementPhotos côté serveur : réorientée, réduite à [coteMax] px au plus, JPEG sans EXIF. */
    private fun reduirePhoto(octets: ByteArray, coteMax: Int, qualite: Int): Map<String, Any> {
        val bornes = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(octets, 0, octets.size, bornes)
        require(bornes.outWidth > 0 && bornes.outHeight > 0) { "photo illisible" }
        require(bornes.outWidth.toLong() * bornes.outHeight <= PIXELS_MAX) { "photo trop grande" }

        var echantillon = 1
        while (max(bornes.outWidth, bornes.outHeight) / (echantillon * 2) >= coteMax) {
            echantillon *= 2
        }
        val decodee = BitmapFactory.decodeByteArray(octets, 0, octets.size, BitmapFactory.Options().apply { inSampleSize = echantillon })
            ?: throw IllegalArgumentException("photo illisible")

        val orientation = ExifInterface(ByteArrayInputStream(octets))
            .getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)
        val matrice = Matrix()
        when (orientation) {
            ExifInterface.ORIENTATION_ROTATE_90 -> matrice.postRotate(90f)
            ExifInterface.ORIENTATION_ROTATE_180 -> matrice.postRotate(180f)
            ExifInterface.ORIENTATION_ROTATE_270 -> matrice.postRotate(270f)
            ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrice.postScale(-1f, 1f)
            ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrice.postScale(1f, -1f)
            ExifInterface.ORIENTATION_TRANSPOSE -> { matrice.postRotate(90f); matrice.postScale(-1f, 1f) }
            ExifInterface.ORIENTATION_TRANSVERSE -> { matrice.postRotate(270f); matrice.postScale(-1f, 1f) }
        }
        val plusGrand = max(decodee.width, decodee.height)
        if (plusGrand > coteMax) {
            val facteur = coteMax.toFloat() / plusGrand
            matrice.postScale(facteur, facteur)
        }
        val finale = if (matrice.isIdentity) decodee else Bitmap.createBitmap(decodee, 0, 0, decodee.width, decodee.height, matrice, true)

        // Bitmap.compress n'écrit aucune métadonnée : ni position GPS, ni modèle d'appareil.
        val sortie = ByteArrayOutputStream()
        finale.compress(Bitmap.CompressFormat.JPEG, qualite, sortie)
        val resultat = mapOf<String, Any>("octets" to sortie.toByteArray(), "largeur" to finale.width, "hauteur" to finale.height)
        if (finale !== decodee) {
            finale.recycle()
        }
        decodee.recycle()
        return resultat
    }

    override fun onListen(arguments: Any?, events: EventChannel.EventSink) {
        val recepteur = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                if (intent.action == Intent.ACTION_SCREEN_OFF) {
                    // Instant de la diffusion (horloge monotone) : mesure du délai de verrouillage.
                    events.success(SystemClock.elapsedRealtime())
                }
            }
        }
        // Diffusion système protégée : aucune autre application ne peut l'émettre ; récepteur non exporté.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            contexte.registerReceiver(recepteur, IntentFilter(Intent.ACTION_SCREEN_OFF), Context.RECEIVER_NOT_EXPORTED)
        } else {
            contexte.registerReceiver(recepteur, IntentFilter(Intent.ACTION_SCREEN_OFF))
        }
        recepteurEcran = recepteur
    }

    override fun onCancel(arguments: Any?) {
        recepteurEcran?.let { contexte.unregisterReceiver(it) }
        recepteurEcran = null
    }

    private class CleAbsente(alias: String) : java.security.GeneralSecurityException("clé absente : $alias")

    private companion object {
        const val KEYSTORE = "AndroidKeyStore"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV = 12
        const val COTE_MAX = 2560
        const val QUALITE = 85
        const val PIXELS_MAX = 50_000_000L
    }
}

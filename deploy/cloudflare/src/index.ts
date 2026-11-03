// Préproduction (DEP-01, ADR-08) : le Worker relaie chaque requête vers un conteneur unique qui fait
// tourner l'application web (publique, port 8080) et l'API (interne au conteneur). Les secrets sont des
// secrets du Worker (wrangler secret put), transmis au conteneur par variables d'environnement.
// Politique « durable_object » (imposée par Cloudflare pour ce compte) : le Durable Object démarre lui-même
// le conteneur via ctx.container, la bibliothèque @cloudflare/containers ne gérant pas encore ce mode.
// Planificateur des rappels (US-10, S10-02) : l'alarme du Durable Object est réglée sur le prochain seuil réel renvoyé
// par l'API (pas de réveil périodique : chaque réveil garde le conteneur allumé 15 min), avancée à une minute après
// toute écriture (une qualification peut créer un seuil plus proche), plus un déclencheur quotidien de secours.
import { DurableObject } from "cloudflare:workers";

interface Env {
  GESTIONSITE: DurableObjectNamespace<GestionSite>;
  // Variables publiques (wrangler.jsonc)
  R2_COMPARTIMENT: string;
  R2_CLES_COMPARTIMENT: string;
  // Secrets (R2_URL aussi : il contient l'identifiant du compte, hors dépôt)
  BASE_APPLICATIVE: string;
  CLE_SIGNATURE: string;
  R2_URL: string;
  R2_ACCES: string;
  R2_SECRET: string;
  // Jeton distinct pour le compartiment des clés de chiffrement des cookies (moindre privilège).
  R2_CLES_ACCES: string;
  R2_CLES_SECRET: string;
  // Anti-robot du formulaire public (S08-03) : facultatif ici ; absent, seul le dépôt public répond 503.
  TURNSTILE_SECRET?: string;
  // Clé de site (publique) du widget, affichée par le formulaire ; hors dépôt comme le reste du compte.
  TURNSTILE_CLE_SITE?: string;
  // Secret partagé avec l'API pour le point d'entrée interne du planificateur (S10-02) ; absent : aucun rappel planifié.
  PLANIFICATEUR_JETON?: string;
}

const SECRETS_REQUIS = [
  "BASE_APPLICATIVE", "CLE_SIGNATURE", "R2_URL", "R2_ACCES", "R2_SECRET", "R2_CLES_ACCES", "R2_CLES_SECRET",
] as const;
const PORT_WEB = 8080;
const VEILLE_MS = 15 * 60 * 1000; // mise en veille après 15 min sans requête (ADR-08 : démarrage à froid accepté)
const ATTENTE_MAX_MS = 60_000;
// Planificateur : port dédié de l'API (seule sa route y répond), jamais celui que le Worker relaie au public.
const PORT_PLANIFICATEUR = 8082;
const ROUTE_PLANIFICATEUR = "/interne/planificateur/passage";
const APRES_ECRITURE_MS = 60_000; // passage une minute après une écriture, conteneur déjà éveillé
const ECART_MIN_MS = 30_000; // jamais d'alarme immédiate en boucle
const CLE_ORIGINE = "origine"; // adresse publique, pour démarrer le conteneur depuis une alarme (sans requête)
const METHODES_LECTURE = new Set(["GET", "HEAD", "OPTIONS"]);

interface BilanPlanificateur {
  organisations: number;
  notifications: number;
  echecs: number;
  prochainPassage: string | null;
}

const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

export class GestionSite extends DurableObject<Env> {
  private demarrage?: Promise<void>;
  private verification?: Promise<void>;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    if (ctx.container?.running) {
      ctx.waitUntil(ctx.container.setInactivityTimeout(VEILLE_MS));
    }
  }

  async fetch(request: Request): Promise<Response> {
    const conteneur = this.ctx.container;
    const manquants = SECRETS_REQUIS.filter((nom) => !this.env[nom]);
    if (!conteneur || manquants.length > 0) {
      console.error(`Configuration incomplète : ${conteneur ? manquants.join(", ") : "conteneur absent"}`);
      return indisponible();
    }
    try {
      await this.conteneurPret(conteneur, adressePublique(request));
      const reponse = await conteneur.getTcpPort(PORT_WEB).fetch(relayer(request));
      if (this.env.PLANIFICATEUR_JETON && !METHODES_LECTURE.has(request.method)) {
        this.ctx.waitUntil(this.planifierAvant(Date.now() + APRES_ECRITURE_MS).catch((e) => console.error(`Planificateur : ${e}`)));
      }
      return reponse;
    } catch (erreur) {
      console.error(`Relais impossible : ${erreur}`);
      return indisponible();
    }
  }

  // Alarme : un passage du planificateur, puis l'alarme réglée sur le prochain seuil renvoyé par l'API. Une erreur
  // (conteneur qui ne démarre pas, API en échec) est levée : Cloudflare rejoue l'alarme avec un délai croissant.
  async alarm(): Promise<void> {
    const jeton = this.env.PLANIFICATEUR_JETON;
    const conteneur = this.ctx.container;
    const origine = await this.ctx.storage.get<string>(CLE_ORIGINE);
    if (!jeton || !conteneur || !origine) {
      console.warn(`Planificateur inactif : ${raisonInactif(jeton, conteneur)}`);
      return;
    }
    await this.conteneurPret(conteneur, origine);
    const reponse = await conteneur.getTcpPort(PORT_PLANIFICATEUR).fetch(`http://localhost${ROUTE_PLANIFICATEUR}`, {
      method: "POST",
      headers: { "X-Planificateur-Jeton": jeton },
    });
    if (!reponse.ok) {
      await reponse.body?.cancel();
      throw new Error(`Planificateur : réponse ${reponse.status}`);
    }
    const bilan = (await reponse.json()) as BilanPlanificateur;
    console.log(
      `Planificateur : ${bilan.organisations} organisation(s), ${bilan.notifications} notification(s), ` +
        `${bilan.echecs} échec(s), prochain passage ${bilan.prochainPassage ?? "aucun"}`,
    );
    if (bilan.prochainPassage) {
      await this.planifierAvant(Date.parse(bilan.prochainPassage));
    }
  }

  // Déclencheur quotidien de secours (« scheduled » du Worker) : un passage tout de suite, par l'alarme (rejouée en cas d'échec).
  async passageDeSecours(): Promise<void> {
    if (this.env.PLANIFICATEUR_JETON) {
      await this.ctx.storage.setAlarm(Date.now());
    }
  }

  // Alarme avancée à « instant » si elle est plus tardive, absente ou déjà passée ; jamais à moins de 30 s.
  private async planifierAvant(instant: number): Promise<void> {
    const cible = Math.max(instant, Date.now() + ECART_MIN_MS);
    const actuelle = await this.ctx.storage.getAlarm();
    if (actuelle === null || actuelle <= Date.now() || actuelle > cible) {
      await this.ctx.storage.setAlarm(cible);
    }
  }

  private async conteneurPret(conteneur: Container, origine: string): Promise<void> {
    // Un déploiement ne redémarre pas un conteneur déjà lancé : une fois par vie de l'objet (donc après
    // chaque déploiement), arrêter celui qui tourne encore sur une image précédente.
    this.verification ??= this.arreterSiImagePerimee(conteneur);
    await this.verification;
    if (!conteneur.running) {
      this.demarrage ??= this.demarrer(conteneur, origine).finally(() => (this.demarrage = undefined));
    }
    if (this.demarrage) {
      await this.demarrage;
    }
  }

  private async arreterSiImagePerimee(conteneur: Container): Promise<void> {
    try {
      if (!conteneur.running) {
        return;
      }
      const attendue = conteneur.images.application;
      const actuelle = (await conteneur.inspect())?.image;
      if (actuelle !== undefined && actuelle !== attendue) {
        console.log(`Image précédente (${actuelle}) : arrêt du conteneur`);
        await this.arreter(conteneur, "image précédente");
      }
    } catch (erreur) {
      this.verification = undefined; // nouvel essai à la requête suivante
      throw erreur;
    }
  }

  private async arreter(conteneur: Container, raison: string): Promise<void> {
    await conteneur.destroy(raison);
    for (let i = 0; i < 20 && conteneur.running; i++) {
      await pause(250);
    }
  }

  // origine : adresse publique vue par le Worker (liens d'activation), sans la fixer dans le dépôt ; conservée pour
  // qu'une alarme (sans requête) puisse redémarrer le conteneur avec la même adresse.
  private async demarrer(conteneur: Container, origine: string): Promise<void> {
    const env = this.env;
    if ((await this.ctx.storage.get<string>(CLE_ORIGINE)) !== origine) {
      await this.ctx.storage.put(CLE_ORIGINE, origine);
    }
    conteneur.start({
      image: conteneur.images.application,
      instance: "standard-1", // ½ vCPU, 4 Gio : deux processus .NET (le type « basic » n'existe pas dans ce mode)
      enableInternet: true, // Neon et R2
      env: {
        ConnectionStrings__Main: env.BASE_APPLICATIVE,
        Authentification__CleSignature: env.CLE_SIGNATURE,
        ObjectStorage__ServiceUrl: env.R2_URL,
        ObjectStorage__AccessKey: env.R2_ACCES,
        ObjectStorage__SecretKey: env.R2_SECRET,
        ObjectStorage__Bucket: env.R2_COMPARTIMENT,
        ObjectStorage__Region: "auto",
        Web__UrlPublique: origine,
        Web__RelaisHttps: "true", // le Worker est le seul à joindre le conteneur (voir relayer)
        Web__Cles__ServiceUrl: env.R2_URL,
        Web__Cles__AccessKey: env.R2_CLES_ACCES,
        Web__Cles__SecretKey: env.R2_CLES_SECRET,
        Web__Cles__Compartiment: env.R2_CLES_COMPARTIMENT,
        Web__Cles__Region: "auto",
        Turnstile__CleSecrete: env.TURNSTILE_SECRET ?? "",
        Web__CleSiteTurnstile: env.TURNSTILE_CLE_SITE ?? "",
        // Adresse du visiteur pour la limite par adresse : réécrite par Cloudflare à chaque requête, jamais celle du client.
        Web__EnteteAdresseClient: "CF-Connecting-IP",
        // Point d'entrée interne du planificateur (port dédié : voir server/deploy/demarrer.sh).
        Planificateur__Jeton: env.PLANIFICATEUR_JETON ?? "",
      },
    });
    await conteneur.setInactivityTimeout(VEILLE_MS);

    // start() rend la main avant que l'application écoute : attendre que /sante réponde « Disponible »
    // (web et API prêts), sauf si le conteneur s'arrête entre-temps (configuration refusée au démarrage).
    const limite = Date.now() + ATTENTE_MAX_MS;
    while (Date.now() < limite && conteneur.running) {
      try {
        const reponse = await conteneur.getTcpPort(PORT_WEB).fetch("http://conteneur/sante");
        await reponse.body?.cancel();
        if (reponse.ok) {
          return;
        }
      } catch {
        // pas encore à l'écoute
      }
      await pause(500);
    }
    // Conteneur arrêté ou jamais prêt : l'arrêter pour qu'une prochaine requête reparte de zéro.
    if (conteneur.running) {
      await this.arreter(conteneur, "démarrage trop long");
    }
    throw new Error("Le conteneur n'est pas devenu disponible");
  }
}

function raisonInactif(jeton: string | undefined, conteneur: Container | undefined): string {
  if (!jeton) {
    return "PLANIFICATEUR_JETON absent";
  }
  return conteneur ? "adresse publique encore inconnue (aucune requête reçue depuis la création de l'objet)" : "conteneur absent";
}

// Hôte de la requête (seul le nom workers.dev du Worker y mène), toujours en https et sans port
// alternatif : les liens d'activation ne dépendent pas du schéma ou du port choisis par le visiteur.
function adressePublique(request: Request): string {
  const url = new URL(request.url);
  url.protocol = "https:";
  url.port = "";
  return url.origin;
}

function indisponible(): Response {
  return new Response("Service momentanément indisponible, réessayez dans un instant.", {
    status: 503,
    headers: { "Content-Type": "text/plain; charset=utf-8", "Retry-After": "10" },
  });
}

// Le conteneur n'accepte que http (lien déjà chiffré par Cloudflare) : le schéma d'origine passe par
// X-Forwarded-Proto, toujours réécrit ici pour qu'un visiteur ne puisse pas l'imposer.
function relayer(request: Request): Request {
  const url = new URL(request.url);
  const origine = url.protocol.replace(":", "");
  url.protocol = "http:";
  const relayee = new Request(url, request);
  relayee.headers.set("X-Forwarded-Proto", origine);
  return relayee;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    return env.GESTIONSITE.getByName("preproduction").fetch(request);
  },
  // Déclencheur quotidien de secours du planificateur (wrangler.jsonc, « triggers ») : rattrape une alarme perdue.
  async scheduled(_controleur: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(env.GESTIONSITE.getByName("preproduction").passageDeSecours());
  },
} satisfies ExportedHandler<Env>;

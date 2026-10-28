// Préproduction (DEP-01, ADR-08) : le Worker relaie chaque requête vers un conteneur unique qui fait
// tourner l'application web (publique, port 8080) et l'API (interne au conteneur). Les secrets sont des
// secrets du Worker (wrangler secret put), transmis au conteneur par variables d'environnement.
// Politique « durable_object » (imposée par Cloudflare pour ce compte) : le Durable Object démarre lui-même
// le conteneur via ctx.container, la bibliothèque @cloudflare/containers ne gérant pas encore ce mode.
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
}

const SECRETS_REQUIS = ["BASE_APPLICATIVE", "CLE_SIGNATURE", "R2_URL", "R2_ACCES", "R2_SECRET", "R2_CLES_ACCES", "R2_CLES_SECRET"] as const;
const PORT_WEB = 8080;
const VEILLE_MS = 15 * 60 * 1000; // mise en veille après 15 min sans requête (ADR-08 : démarrage à froid accepté)
const ATTENTE_MAX_MS = 60_000;

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
      // Un déploiement ne redémarre pas un conteneur déjà lancé : une fois par vie de l'objet (donc après
      // chaque déploiement), arrêter celui qui tourne encore sur une image précédente.
      this.verification ??= this.arreterSiImagePerimee(conteneur);
      await this.verification;
      if (!conteneur.running) {
        this.demarrage ??= this.demarrer(conteneur, adressePublique(request)).finally(() => (this.demarrage = undefined));
      }
      if (this.demarrage) {
        await this.demarrage;
      }
      return await conteneur.getTcpPort(PORT_WEB).fetch(relayer(request));
    } catch (erreur) {
      console.error(`Relais impossible : ${erreur}`);
      return indisponible();
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

  // origine : adresse publique vue par le Worker (liens d'activation), sans la fixer dans le dépôt.
  private async demarrer(conteneur: Container, origine: string): Promise<void> {
    const env = this.env;
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
} satisfies ExportedHandler<Env>;

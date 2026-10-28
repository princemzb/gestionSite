// Préproduction (DEP-01, ADR-08) : le Worker relaie chaque requête vers un conteneur unique qui fait
// tourner l'application web (publique, port 8080) et l'API (interne au conteneur). Les secrets sont des
// secrets du Worker (wrangler secret put), transmis au conteneur par variables d'environnement.
// Politique « durable_object » (imposée par Cloudflare pour ce compte) : le Durable Object démarre lui-même
// le conteneur via ctx.container, la bibliothèque @cloudflare/containers ne gérant pas encore ce mode.
import { DurableObject } from "cloudflare:workers";

interface Env {
  GESTIONSITE: DurableObjectNamespace<GestionSite>;
  // Variables publiques (wrangler.jsonc)
  URL_PUBLIQUE: string;
  R2_URL: string;
  R2_COMPARTIMENT: string;
  // Secrets
  BASE_APPLICATIVE: string;
  CLE_SIGNATURE: string;
  R2_ACCES: string;
  R2_SECRET: string;
  // Compartiment et jeton distincts pour les clés de chiffrement des cookies (moindre privilège).
  R2_CLES_COMPARTIMENT: string;
  R2_CLES_ACCES: string;
  R2_CLES_SECRET: string;
}

const PORT_WEB = 8080;
const VEILLE_MS = 15 * 60 * 1000; // mise en veille après 15 min sans requête (ADR-08 : démarrage à froid accepté)
const ATTENTE_MAX_MS = 60_000;

export class GestionSite extends DurableObject<Env> {
  private demarrage?: Promise<void>;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    if (ctx.container?.running) {
      ctx.waitUntil(ctx.container.setInactivityTimeout(VEILLE_MS));
    }
  }

  async fetch(request: Request): Promise<Response> {
    const conteneur = this.ctx.container;
    if (!conteneur) {
      return new Response("Conteneur indisponible", { status: 503 });
    }
    if (!conteneur.running) {
      this.demarrage ??= this.demarrer(conteneur).finally(() => (this.demarrage = undefined));
    }
    if (this.demarrage) {
      await this.demarrage;
    }
    return conteneur.getTcpPort(PORT_WEB).fetch(request);
  }

  private async demarrer(conteneur: Container): Promise<void> {
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
        Web__UrlPublique: env.URL_PUBLIQUE,
        Web__Cles__ServiceUrl: env.R2_URL,
        Web__Cles__AccessKey: env.R2_CLES_ACCES,
        Web__Cles__SecretKey: env.R2_CLES_SECRET,
        Web__Cles__Compartiment: env.R2_CLES_COMPARTIMENT,
        Web__Cles__Region: "auto",
      },
    });
    await conteneur.setInactivityTimeout(VEILLE_MS);

    // start() rend la main avant que l'application écoute : attendre la première réponse de /sante.
    const limite = Date.now() + ATTENTE_MAX_MS;
    while (Date.now() < limite) {
      try {
        await conteneur.getTcpPort(PORT_WEB).fetch("http://conteneur/sante");
        return;
      } catch {
        await new Promise((resolve) => setTimeout(resolve, 500));
      }
    }
    throw new Error("Le conteneur n'a pas répondu dans le délai imparti");
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    return env.GESTIONSITE.getByName("preproduction").fetch(request);
  },
} satisfies ExportedHandler<Env>;

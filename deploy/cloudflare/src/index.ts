// Préproduction (DEP-01, ADR-08) : le Worker relaie chaque requête vers un conteneur unique qui fait
// tourner l'application web (publique, port 8080) et l'API (interne au conteneur). Les secrets sont des
// secrets du Worker (wrangler secret put), transmis au conteneur par variables d'environnement.
import { Container, getContainer } from "@cloudflare/containers";

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

export class GestionSite extends Container<Env> {
  defaultPort = 8080;
  sleepAfter = "15m"; // mise en veille après 15 min sans requête (ADR-08 : démarrage à froid accepté)
  enableInternet = true; // Neon et R2

  constructor(ctx: ConstructorParameters<typeof Container<Env>>[0], env: Env) {
    super(ctx, env);
    this.envVars = {
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
    };
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    return getContainer(env.GESTIONSITE, "preproduction").fetch(request);
  },
} satisfies ExportedHandler<Env>;

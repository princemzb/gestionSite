// Préparation commune des vérifications usXX.mjs : variables d'environnement, mode recette (recette.mjs), navigateur,
// pages suivies pour les violations de la politique de sécurité du contenu, activation d'un compte et invitation d'un
// membre par l'administrateur. Les données sont fictives ; le jeton d'activation n'est jamais affiché ni écrit.
import { randomBytes } from "node:crypto";
import { recette } from "./recette.mjs";

/** Prépare la vérification d'une US (« us08 »…) : quitte (code 2) si VERIF_URL, VERIF_ORG ou VERIF_JETON manque. */
export async function preparer(us) {
  const { VERIF_URL: base, VERIF_ORG: org, VERIF_JETON: jeton } = process.env;
  if (!base || !org || !jeton) { console.error("VERIF_URL, VERIF_ORG et VERIF_JETON sont requis."); process.exit(2); }
  const mdp = randomBytes(12).toString("hex") + "-verif";
  const r = recette(us);
  const b = await r.lancer();
  const violations = [];

  /** Relève les violations de CSP signalées à la console de la page. */
  const surveiller = p => {
    p.on("console", m => { if (m.type() === "error" && /Content.Security.Policy/i.test(m.text())) violations.push(`${p.url()} → ${m.text()}`); });
    return p;
  };

  /** Nouvelle page dans son propre contexte (filmé en recette), surveillée pour la CSP. */
  const page = async (libelle, largeur = 1440, hauteur = 900, options = {}) =>
    surveiller(await (await r.contexte({ viewport: { width: largeur, height: hauteur }, ...options }, libelle)).newPage());

  /** Active un compte depuis son lien d'activation (même mot de passe fictif pour tous). */
  const activer = async (p, lien) => {
    await p.goto(lien, { timeout: 90000 });
    await p.fill("input[type=password] >> nth=0", mdp); await p.fill("input[type=password] >> nth=1", mdp);
    await p.click("button[type=submit]"); await p.waitForLoadState();
  };

  /** L'administrateur invite un membre sur le site ; renvoie son e-mail fictif et son lien d'activation. */
  const inviter = async (admin, nom, role) => {
    const email = `${role.toLowerCase()}-${randomBytes(4).toString("hex")}@demo.gestionsite.test`;
    await admin.goto(`${base}/organisations/${org}/membres?inviter=1`);
    await admin.fill("input[name='Saisie.Nom']", nom);
    await admin.fill("input[name='Saisie.Email']", email);
    await admin.click("button:has-text('Continuer')"); await admin.waitForLoadState();
    await admin.selectOption("main select >> nth=0", role);
    await admin.click("button:has-text(\"Créer l'invitation\")"); await admin.waitForLoadState();
    return { email, lien: (await admin.textContent("code.lien-activation")).trim() };
  };

  /** Dernier contrôle (aucune violation relevée), puis fin du parcours (rapport en recette). */
  const finir = async (nom = "aucune violation de la politique de sécurité du contenu", detail = false) => {
    violations.forEach(v => console.log("  violation : " + v.slice(0, 300)));
    await r.ok(nom, violations.length === 0, detail ? violations.join(" | ") : "");
    await r.terminer();
  };

  return { r, ok: r.ok, b, base, org, jeton, mdp, violations, surveiller, page, activer, inviter, finir };
}

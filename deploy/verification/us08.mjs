// Vérification de la préproduction pour US-08 (navigateur réel, Playwright 1.55.0 ; recette visible avec RECETTE=1) :
// écran Sites (lien public et QR), affiche A4 ; invitation d'un technicien et d'une responsable du site ; dépôt public réel
// depuis un téléphone (lien du QR → « Il y a un danger » → description et photo synthétique → défi Turnstile, que la
// préproduction franchit avec les clés de test officielles depuis le 03/11 → envoi) ; accusé corrigé le 02/11 (« Votre
// signalement est enregistré et transmis aux responsables du site. En cas de danger immédiat, appelez les secours (18 ou
// 112). ») ; le technicien voit le dépôt dans « Dépôts reçus » avec sa photo (relais JPEG) ; si VERIF_BASE (chaîne
// applicative, fournie par lancer.sh) est présente, le centre de notifications (S10-01, sans écran) est lu en base : alerte
// de danger enregistrée, notification « DangerSignale » pour la responsable, aucune pour le technicien ; aucune violation
// de la politique de sécurité du contenu.
// Usage : deploy/verification/lancer.sh us08 (ou VERIF_URL=… VERIF_ORG=… VERIF_JETON=… [VERIF_BASE=…] node us08.mjs) ;
// organisation fictive ouverte par « ouvrir-organisation --site » ; jeton jamais passé en argument ni écrit.
import { randomBytes } from "node:crypto";
import { spawnSync } from "node:child_process";
import { crc32, deflateSync } from "node:zlib";
import { recette } from "./recette.mjs";

const { VERIF_URL: base, VERIF_ORG: org, VERIF_JETON: jeton, VERIF_BASE: chaineBase } = process.env;
if (!base || !org || !jeton) { console.error("VERIF_URL, VERIF_ORG et VERIF_JETON sont requis."); process.exit(2); }
const mdp = randomBytes(12).toString("hex") + "-verif";
const r = recette("us08");
const ok = r.ok;
await r.lancer();
const violations = [];
const page = async (libelle, largeur = 1440, hauteur = 900) => {
  const p = await (await r.contexte({ viewport: { width: largeur, height: hauteur }, locale: "fr-FR" }, libelle)).newPage();
  p.on("console", m => { if (m.type() === "error" && /Content.Security.Policy/i.test(m.text())) violations.push(`${p.url()} → ${m.text()}`); });
  return p;
};
const activer = async (p, lien) => {
  await p.goto(lien, { timeout: 90000 });
  await p.fill("input[type=password] >> nth=0", mdp); await p.fill("input[type=password] >> nth=1", mdp);
  await p.click("button[type=submit]"); await p.waitForLoadState();
};
const texte = async (p, selecteur = "body") => ((await p.textContent(selecteur)) ?? "").replace(/\s+/g, " ");

async function finir() {
  violations.forEach(v => console.log("  violation : " + v.slice(0, 300)));
  await ok("aucune violation de la politique de sécurité du contenu", violations.length === 0);
  await r.terminer();
}

// Photo synthétique (PNG 800 × 600, bandes de signalisation jaunes et noires) : aucune donnée réelle.
function photoSynthetique() {
  const [l, h] = [800, 600];
  const brut = Buffer.alloc((l * 3 + 1) * h);
  for (let y = 0; y < h; y++) for (let x = 0; x < l; x++) {
    const jaune = Math.floor((x + y) / 60) % 2 === 0;
    brut.set(jaune ? [250, 204, 21] : [17, 17, 17], y * (l * 3 + 1) + 1 + x * 3);
  }
  const bloc = (type, donnees) => {
    const t = Buffer.concat([Buffer.from(type), donnees]);
    const taille = Buffer.alloc(4); taille.writeUInt32BE(donnees.length);
    const somme = Buffer.alloc(4); somme.writeUInt32BE(crc32(t));
    return Buffer.concat([taille, t, somme]);
  };
  const entete = Buffer.alloc(13); entete.writeUInt32BE(l, 0); entete.writeUInt32BE(h, 4); entete[8] = 8; entete[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), bloc("IHDR", entete), bloc("IDAT", deflateSync(brut)), bloc("IEND", Buffer.alloc(0))]);
}

// 1. Administrateur : activation puis écran Sites (lien public et QR), affiche A4
await r.etape("1. Administrateur : écran Sites, lien public, QR code et affiche");
const admin = await page("Administrateur");
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
await admin.goto(`${base}/organisations/${org}/sites`);
const lien = (await admin.textContent(".carte-site code")).trim();
await ok("écran Sites : lien public du site en https sur l'adresse publique", lien.startsWith(`${base}/s/`));
await ok("écran Sites : QR code affiché", await admin.locator(".carte-site .qr svg").count() === 1);
await admin.screenshot({ path: "us08-sites.png", fullPage: true });
const affiche = await admin.request.get(`${base}${await admin.getAttribute("a:has-text('Imprimer l')", "href")}`);
await ok("affiche A4 servie", affiche.ok() && (await affiche.text()).includes("Un problème ici ?"));

// 2. Administrateur : invitation d'un technicien et d'une responsable du site, qui activent leur compte
await r.etape("2. Administrateur : invitation d'un technicien et d'une responsable du site");
const inviter = async (nom, role) => {
  const email = `${role.toLowerCase()}-${randomBytes(4).toString("hex")}@demo.gestionsite.test`;
  await admin.goto(`${base}/organisations/${org}/membres?inviter=1`);
  await admin.fill("input[name='Saisie.Nom']", nom);
  await admin.fill("input[name='Saisie.Email']", email);
  await admin.click("button:has-text('Continuer')"); await admin.waitForLoadState();
  await admin.selectOption("main select >> nth=0", role);
  await admin.click("button:has-text(\"Créer l'invitation\")"); await admin.waitForLoadState();
  return { email, lien: (await admin.textContent("code.lien-activation")).trim() };
};
const technicien = await inviter("Marc Technicien", "Technicien");
const responsable = await inviter("Rose Responsable", "Responsable");
await ok("invitations créées (technicien, responsable)", [technicien, responsable].every(i => i.lien.startsWith(`${base}/activation?jeton=`)));
const tech = await page("Technicien");
await activer(tech, technicien.lien);
await tech.goto(`${base}/`); await tech.waitForLoadState();
await ok("le technicien arrive sur ses dossiers", tech.url().includes(`/organisations/${org}/dossiers`));
const resp = await page("Responsable");
await activer(resp, responsable.lien);
await ok("la responsable a activé son compte", resp.url().includes(`/organisations/${org}`));

// 3. Visiteur sur téléphone : lien du QR → « Il y a un danger » → description et photo → défi Turnstile → envoi
await r.etape("3. Visiteur sur téléphone : signalement d'un danger avec photo");
const tel = await page("Visiteur (téléphone)", 390, 844);
await tel.goto(lien);
await ok("formulaire public : trois choix", await tel.locator("a.tuile").count() === 3);
await tel.click("text=Il y a un danger"); await tel.waitForLoadState();
const formulaire = await texte(tel);
await ok("formulaire de danger : renvoi aux secours, champ photos",
  formulaire.includes("Vous signalez un danger. En cas de danger immédiat, appelez d'abord les secours (18 ou 112).") && await tel.locator("input[type=file]").count() === 1);
await tel.fill("textarea[name='Saisie.Description']", "Fil électrique dénudé qui pend du plafond, au-dessus des chaises");
await tel.fill("input[name='Saisie.Emplacement']", "Grande salle, près de l'entrée");
await tel.check("input[name='Saisie.ActiviteBloquee']");
await tel.setInputFiles("input[name='photos']", { name: "danger.png", mimeType: "image/png", buffer: photoSynthetique() });
await tel.fill("input[name='Saisie.Nom']", "Visiteur Vérification");
await tel.fill("input[name='Saisie.Contact']", "visiteur@demo.gestionsite.test");
await ok("widget Turnstile chargé", await tel.locator(".cf-turnstile").count() === 1);
// Seules les clés de test « toujours réussi » (1x…AA visible, 1x…BB invisible) laissent passer un navigateur automatisé : avec
// de vraies clés (production), le dépôt réel n'est pas vérifiable ici, ce qui est dit sans échec.
if (!/^1x0{20}(AA|BB)$/.test(await tel.getAttribute(".cf-turnstile", "data-sitekey") ?? "")) {
  console.log("NON VÉRIFIÉ dépôt réel, Dépôts reçus et notifications : clé Turnstile réelle (défi non franchissable par un automate)");
  await finir();
  process.exit(0);
}
const defi = await tel.waitForFunction(() => document.querySelector("input[name='cf-turnstile-response']")?.value.length > 0, null, { timeout: 60000 })
  .then(() => true, () => false);
await ok("défi Turnstile franchi (clés de test de la préproduction)", defi);
await tel.screenshot({ path: "us08-formulaire-tel.png", fullPage: true });
await Promise.all([tel.waitForNavigation({ timeout: 90000 }), tel.click("button:has-text('Envoyer')")]);
const accuse = await texte(tel);
await ok("accusé : « Merci, c'est transmis. »", accuse.includes("Merci, c'est transmis."), accuse.includes("La vérification anti-robot") ? "vérification anti-robot refusée" : "");
await ok("accusé corrigé (02/11) : enregistré, transmis aux responsables, appel des secours",
  accuse.includes("Vous avez signalé un danger. Votre signalement est enregistré et transmis aux responsables du site. En cas de danger immédiat, appelez les secours (18 ou 112)."));
await tel.screenshot({ path: "us08-accuse-tel.png", fullPage: true });

// 4. Technicien : « Dépôts reçus » avec le danger et sa photo
await r.etape("4. Technicien : le dépôt dans « Dépôts reçus », avec sa photo");
await tech.goto(`${base}/organisations/${org}/depots`); await tech.waitForLoadState();
const liste = await texte(tech, "main");
await ok("dépôt listé : danger, activité bloquée, emplacement, une photo",
  liste.includes("Fil électrique dénudé") && liste.includes("Grande salle, près de l'entrée") && liste.includes("Bloque une activité")
  && await tech.locator("tbody tr:has-text('Fil électrique dénudé') .badge.danger").count() === 1
  && (await texte(tech, "tbody tr:has-text('Fil électrique dénudé') td >> nth=4")).trim() === "1");
await Promise.all([tech.waitForNavigation(), tech.click("tbody tr:has-text('Fil électrique dénudé') td.date a")]);
const detail = await texte(tech, "aside.detail-depot");
await ok("détail : danger signalé, déclarant et contact (non vérifié)",
  detail.includes("Danger signalé") && detail.includes("Visiteur Vérification") && detail.includes("visiteur@demo.gestionsite.test (non vérifié)"));
const photo = tech.locator("aside.detail-depot img").first();
await photo.scrollIntoViewIfNeeded();
const affichee = await tech.waitForFunction(i => i.complete && i.naturalWidth > 0, await photo.elementHandle(), { timeout: 30000 }).then(() => true, () => false);
const relais = await tech.request.get(new URL(await photo.getAttribute("src"), base).toString());
await ok("photo du dépôt affichée (relais JPEG)", affichee && relais.status() === 200 && relais.headers()["content-type"] === "image/jpeg");
await tech.screenshot({ path: "us08-depots.png", fullPage: true });

// 5. Centre de notifications (S10-01, sans écran) : lecture en base avec le rôle applicatif, comme l'application
// (organisation et compte de session) ; rien n'est écrit.
if (chaineBase) {
  await r.etape("5. Centre de notifications (en base) : alerte pour la responsable");
  const champs = Object.fromEntries(chaineBase.split(";").filter(Boolean).map(c => [c.slice(0, c.indexOf("=")).trim(), c.slice(c.indexOf("=") + 1)]));
  // Compte de session toujours réécrit (vide si le compte est introuvable : aucune ligne lisible), et comptes dénombrés.
  const sql = `SET app.organisation_id = :'org';
SELECT 'comptes=' || count(*) FROM identite.comptes WHERE normalized_email IN (upper(:'resp'), upper(:'tech'));
SELECT set_config('app.compte_id', coalesce((SELECT id::text FROM identite.comptes WHERE normalized_email = upper(:'resp')), ''), false) \\g /dev/null
SELECT 'responsable=' || coalesce(string_agg(type, ',' ORDER BY type), '') FROM maintenance.notifications;
SELECT set_config('app.compte_id', coalesce((SELECT id::text FROM identite.comptes WHERE normalized_email = upper(:'tech')), ''), false) \\g /dev/null
SELECT 'technicien=' || count(*) FROM maintenance.notifications;
SELECT 'alertes=' || count(*) FROM maintenance.alertes_danger;
`;
  const psql = spawnSync("docker", ["run", "--rm", "-i", "-e", "PGHOST", "-e", "PGDATABASE", "-e", "PGUSER", "-e", "PGPASSWORD", "-e", "PGSSLMODE",
    "postgres:17.6-alpine", "psql", "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-v", `org=${org}`, "-v", `resp=${responsable.email}`, "-v", `tech=${technicien.email}`], {
    input: sql, encoding: "utf8", timeout: 120000,
    env: { ...process.env, PGHOST: champs.Host, PGDATABASE: champs.Database, PGUSER: champs.Username, PGPASSWORD: champs.Password, PGSSLMODE: "require" },
  });
  const lu = Object.fromEntries((psql.stdout ?? "").split("\n").filter(l => l.includes("=")).map(l => l.split("=")));
  await ok("lecture du centre de notifications en base (responsable et technicien retrouvés)", psql.status === 0 && lu.comptes === "2",
    psql.status === 0 ? `comptes : ${lu.comptes}` : (psql.stderr ?? "").split("\n")[0]);
  await ok("alerte de danger enregistrée avec le dépôt", lu.alertes === "1", `alertes : ${lu.alertes}`);
  await ok("notification « DangerSignale » pour la responsable du site", lu.responsable === "DangerSignale", `responsable : ${lu.responsable || "aucune"}`);
  await ok("aucune notification pour le technicien (sans intervention en charge)", lu.technicien === "0", `technicien : ${lu.technicien}`);
} else {
  console.log("NON VÉRIFIÉ centre de notifications (VERIF_BASE absente)");
}
await finir();

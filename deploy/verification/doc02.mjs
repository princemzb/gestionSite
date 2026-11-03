// Vérification de la préproduction pour DOC-02, aide intégrée (navigateur réel, Chromium sans interface via Playwright 1.55) :
// « ? Aide sur cet écran » sur deux écrans (Membres, puis Dossiers sur tablette où le panneau monte du bas), ouverture au
// clavier, fermeture par Échap avec retour du focus, page Aide par rôle, recherche, chapitre, livret PDF, « Dépôts reçus » au
// menu des internes ; aucune violation de la politique de sécurité du contenu.
// Usage, dans un dossier où playwright est installé (captures écrites dans ce dossier) :
//   VERIF_URL=<adresse publique> VERIF_ORG=<organisation> VERIF_JETON=<jeton d'activation> node doc02.mjs
// (organisation fictive ouverte par « ouvrir-organisation --site » ; jeton jamais passé en argument ni écrit).
import { chromium } from "playwright";
import { randomBytes } from "node:crypto";
const { VERIF_URL: base, VERIF_ORG: org, VERIF_JETON: jeton } = process.env;
if (!base || !org || !jeton) { console.error("VERIF_URL, VERIF_ORG et VERIF_JETON sont requis."); process.exit(2); }
const mdp = randomBytes(12).toString("hex") + "-verif";
const ok = (nom, cond) => { console.log((cond ? "OK    " : "ÉCHEC ") + nom); if (!cond) process.exit(1); };
const b = await chromium.launch();
const violations = [];
const page = async (largeur = 1440, hauteur = 900) => {
  const p = await (await b.newContext({ viewport: { width: largeur, height: hauteur } })).newPage();
  p.on("console", m => { if (m.type() === "error" && /Content.Security.Policy/i.test(m.text())) violations.push(`${p.url()} → ${m.text()}`); });
  return p;
};
const activer = async (p, lien) => {
  await p.goto(lien, { timeout: 90000 });
  await p.fill("input[type=password] >> nth=0", mdp); await p.fill("input[type=password] >> nth=1", mdp);
  await p.click("button[type=submit]"); await p.waitForLoadState();
};
const panneauOuvert = p => p.evaluate(() => document.getElementById("aide-panneau")?.matches(":popover-open") ?? false);
// Fin de l'animation d'ouverture (glissement du panneau) avant de mesurer sa position.
const pose = p => p.waitForFunction(() => document.getElementById("aide-panneau").getAnimations().length === 0);

// 1. Administrateur : aide de l'écran Membres, ouverte et fermée au clavier
const admin = await page();
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
await admin.goto(`${base}/organisations/${org}/membres`);
await admin.focus("button.bouton-aide");
await admin.keyboard.press("Enter");
ok("Membres : le panneau s'ouvre au clavier sur la rubrique « Voir qui fait quoi »",
  await panneauOuvert(admin) && (await admin.textContent("#aide-panneau-titre")).includes("Voir qui fait quoi"));
ok("le focus entre dans le panneau (bouton Fermer)", await admin.evaluate(() => document.activeElement?.classList.contains("aide-fermer")));
await pose(admin);
const boite = await admin.locator("#aide-panneau").boundingBox();
ok("ordinateur : panneau latéral à droite, sur toute la hauteur", boite.x + boite.width >= 1439 && boite.height >= 899 && boite.width <= 421);
await admin.screenshot({ path: "doc02-membres-aide.png" });
await admin.keyboard.press("Escape");
ok("Échap ferme le panneau et rend le focus au bouton",
  !(await panneauOuvert(admin)) && await admin.evaluate(() => document.activeElement?.classList.contains("bouton-aide")));

// 2. Page Aide : rôle de la personne en premier, recherche, chapitre, livret
await admin.click("nav.admin-nav a:has-text('Aide')"); await admin.waitForLoadState();
ok("page Aide : tâches de l'administrateur en premier", (await admin.textContent(".aide-role.sien")).includes("Administrateur"));
await admin.screenshot({ path: "doc02-page-aide.png", fullPage: true });
await admin.fill("#aide-q", "clôturer"); await Promise.all([admin.waitForNavigation(), admin.press("#aide-q", "Enter")]);
ok("recherche « clôturer » : rubrique « Clôturer une intervention »", (await admin.textContent(".aide-resultats")).includes("Clôturer une intervention"));
await Promise.all([admin.waitForNavigation(), admin.click(".aide-chapitres a:has-text('Dossiers')")]);
const captures = await admin.evaluate(() => [...document.images].map(i => i.src)); // chargement différé : lues une à une
const servies = await Promise.all(captures.map(async src => (await admin.request.get(src)).headers()["content-type"] === "image/png"));
ok(`chapitre Dossiers lu en entier, ${captures.length} captures servies`,
  await admin.locator("#dossiers-qualifier").count() === 1 && captures.length > 5 && servies.every(Boolean));
const livret = await admin.request.get(`${base}/aide/livret.pdf`);
ok("livret PDF téléchargeable", livret.ok() && livret.headers()["content-type"] === "application/pdf" && (await livret.body()).subarray(0, 5).toString() === "%PDF-");

// 3. Technicien sur tablette : « Dépôts reçus » au menu, aide de l'écran Dossiers qui monte du bas
await admin.goto(`${base}/organisations/${org}/membres?inviter=1`);
await admin.fill("input[name='Saisie.Nom']", "Marc Technicien");
await admin.fill("input[name='Saisie.Email']", `technicien-${randomBytes(4).toString("hex")}@demo.gestionsite.test`);
await admin.click("button:has-text('Continuer')"); await admin.waitForLoadState();
await admin.selectOption("main select >> nth=0", "Technicien");
await admin.click("button:has-text(\"Créer l'invitation\")"); await admin.waitForLoadState();
const lien = (await admin.textContent("code.lien-activation")).trim();
const tablette = await page(800, 1280);
await activer(tablette, lien);
await tablette.goto(`${base}/organisations/${org}/dossiers`);
ok("« Dépôts reçus » au menu du technicien", await tablette.locator("nav.admin-nav a:has-text('Dépôts reçus')").count() === 1);
await tablette.click("button.bouton-aide");
await pose(tablette);
const bas = await tablette.locator("#aide-panneau").boundingBox();
ok("tablette : le panneau monte du bas, sur la largeur", await panneauOuvert(tablette) && Math.abs(bas.y + bas.height - 1280) <= 1 && bas.width >= 799);
ok("rubrique « Suivre les dossiers d'un site »", (await tablette.textContent("#aide-panneau-titre")).includes("Suivre les dossiers"));
await tablette.screenshot({ path: "doc02-dossiers-aide-tablette.png" });
await tablette.click("button.aide-fermer");
ok("le bouton Fermer ferme le panneau", !(await panneauOuvert(tablette)));

ok(`aucune violation de la politique de sécurité du contenu${violations.length ? ` : ${violations.join(" | ")}` : ""}`, violations.length === 0);
await b.close();

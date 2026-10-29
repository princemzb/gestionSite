// Vérification de la préproduction pour US-08 (navigateur réel, Chromium sans interface via Playwright 1.55.0) :
// écran Sites, affiche, formulaire public et widget Turnstile, invitation d'un technicien, Dépôts reçus.
// Le défi Turnstile n'est pas franchissable par un navigateur automatisé (c'est son rôle) : le dépôt réel se vérifie
// depuis un téléphone. Usage : node us08.mjs <dossier> avec preprod-url.txt, preprod-org.txt, preprod-jeton.txt
// (organisation fictive ouverte par « ouvrir-organisation --site »), dans un dossier où playwright est installé.
import { chromium } from "playwright";
import { readFileSync } from "node:fs";
import { randomBytes } from "node:crypto";
const d = process.argv[2];
const base = readFileSync(`${d}/preprod-url.txt`, "utf8").trim();
const org = readFileSync(`${d}/preprod-org.txt`, "utf8").trim();
const jeton = readFileSync(`${d}/preprod-jeton.txt`, "utf8").trim();
const mdp = randomBytes(12).toString("hex") + "-verif";
const ok = (nom, cond) => { console.log((cond ? "OK    " : "ÉCHEC ") + nom); if (!cond) process.exit(1); };
const b = await chromium.launch();

// 1. Administrateur : activation puis écran Sites (lien public et QR)
const admin = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await admin.goto(`${base}/activation?jeton=${encodeURIComponent(jeton)}`, { timeout: 90000 });
await admin.fill("input[type=password] >> nth=0", mdp); await admin.fill("input[type=password] >> nth=1", mdp);
await admin.click("button[type=submit]"); await admin.waitForLoadState();
await admin.goto(`${base}/organisations/${org}/sites`);
const lien = (await admin.textContent(".carte-site code")).trim();
ok("écran Sites : lien public du site en https sur l'adresse publique", lien.startsWith(`${base}/s/`));
ok("écran Sites : QR code affiché", await admin.locator(".carte-site .qr svg").count() === 1);
await admin.screenshot({ path: `${d}/preprod-sites.png`, fullPage: true });
const affiche = await admin.request.get(`${base}${await admin.getAttribute("a:has-text('Imprimer l')", "href")}`);
ok("affiche A4 servie", affiche.ok() && (await affiche.text()).includes("Un problème ici ?"));

// 2. Visiteur sur téléphone : formulaire et widget anti-robot (le défi Turnstile n'est pas franchissable par un navigateur automatisé)
const tel = await (await b.newContext({ viewport: { width: 390, height: 844 }, locale: "fr-FR" })).newPage();
await tel.goto(lien);
ok("formulaire public : trois choix", await tel.locator("a.tuile").count() === 3);
await tel.click("text=Il y a un danger"); await tel.waitForLoadState();
ok("formulaire de danger avec photos", (await tel.textContent("body")).includes("Vous signalez un danger") && await tel.locator("input[type=file]").count() === 1);
ok("widget Turnstile chargé", await tel.locator(".cf-turnstile").count() === 1 && await tel.waitForFunction(() => typeof window.turnstile === "object").then(() => true));

// 3. Administrateur : invitation d'un technicien sur le site
await admin.goto(`${base}/organisations/${org}/membres?inviter=1`);
await admin.fill("input[name='Saisie.Nom']", "Technicien Vérification");
await admin.fill("input[name='Saisie.Email']", `tech-${randomBytes(4).toString("hex")}@demo.gestionsite.test`);
await admin.click("button:has-text('Continuer')"); await admin.waitForLoadState();
await admin.selectOption("main select >> nth=0", "Technicien");
await admin.click("button:has-text(\"Créer l'invitation\")"); await admin.waitForLoadState();
const invitation = (await admin.textContent("code.lien-activation")).trim();
ok("invitation du technicien créée", invitation.startsWith(`${base}/activation?jeton=`));

// 4. Technicien : activation puis Dépôts reçus, avec la photo
const tech = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
await tech.goto(invitation);
await tech.fill("input[type=password] >> nth=0", mdp); await tech.fill("input[type=password] >> nth=1", mdp);
await tech.click("button:has-text('Activer')"); await tech.waitForLoadState();
await tech.goto(`${base}/`); await tech.waitForLoadState();
ok("le technicien arrive sur les dépôts reçus", tech.url().includes(`/organisations/${org}/depots`));
const page = await tech.textContent("body");
ok("écran Dépôts reçus du site (encore vide)", page.includes("Dépôts reçus") && page.includes("Aucun dépôt reçu pour Site vérification"));
await tech.screenshot({ path: `${d}/preprod-depots.png`, fullPage: true });
await b.close();

// Vérification de la préproduction pour US-09 (navigateur réel, Chromium sans interface via Playwright 1.55.0) :
// déclaration interne, qualification, affectation, « Mes interventions » sur téléphone, démarrage, clôture, archives ;
// captures Jour et Nuit ; aucune violation de la politique de sécurité du contenu.
// Usage, dans un dossier où playwright est installé (captures écrites dans ce dossier) :
//   VERIF_URL=<adresse publique> VERIF_ORG=<organisation> VERIF_JETON=<jeton d'activation> node us09.mjs
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
const envoyer = async (p, formulaire) => { await Promise.all([p.waitForNavigation(), p.click(`${formulaire} button[type=submit]`)]); };

// 1. Administrateur : activation, invitation d'une responsable et d'un technicien
const admin = await page();
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
const inviter = async (nom, role) => {
  await admin.goto(`${base}/organisations/${org}/membres?inviter=1`);
  await admin.fill("input[name='Saisie.Nom']", nom);
  await admin.fill("input[name='Saisie.Email']", `${role.toLowerCase()}-${randomBytes(4).toString("hex")}@demo.gestionsite.test`);
  await admin.click("button:has-text('Continuer')"); await admin.waitForLoadState();
  await admin.selectOption("main select >> nth=0", role);
  await admin.click("button:has-text(\"Créer l'invitation\")"); await admin.waitForLoadState();
  return (await admin.textContent("code.lien-activation")).trim();
};
const lienResponsable = await inviter("Rose Responsable", "Responsable");
const lienTechnicien = await inviter("Marc Technicien", "Technicien");
ok("invitations créées (responsable, technicien)", lienResponsable.startsWith(`${base}/activation?jeton=`) && lienTechnicien.startsWith(`${base}/activation?jeton=`));

// Le technicien active son compte d'abord : seul un membre actif peut recevoir une intervention.
const tech = await page(390, 844);
await activer(tech, lienTechnicien);

// 2. Responsable : un bâtiment, puis une déclaration interne
const resp = await page();
await activer(resp, lienResponsable);
ok("la responsable arrive sur les dossiers", resp.url().includes(`/organisations/${org}/dossiers`));
await resp.goto(`${base}/organisations/${org}/inventaire?action=batiment`);
await resp.fill("input[name='StructureSaisie.Nom']", "Église"); await envoyer(resp, "form.formulaire-inventaire");
await resp.goto(`${base}/organisations/${org}/dossiers`);
await resp.click("a:has-text('Déclarer un problème')"); await resp.waitForLoadState();
await resp.selectOption("select[name='Saisie.ZoneId']", { index: 1 });
await resp.fill("textarea[name='Saisie.Description']", "Fuite au plafond de la sacristie");
await resp.check("input[name='Saisie.ActiviteBloquee']");
await envoyer(resp, "form.formulaire-inventaire");
ok("dossier créé « À qualifier »", (await resp.textContent("main")).includes("Fuite au plafond de la sacristie") && await resp.locator("li[aria-current='step']:has-text('Déclaré')").count() === 1);
const fiche = resp.url().split("&")[0];

// 3. Qualification puis affectation au technicien
await resp.check("input[name='Qualification.Priorite'][value='Haute']");
await envoyer(resp, "form:has(button:has-text('Enregistrer la qualification'))");
ok("dossier qualifié (étape « Qualifié »)", await resp.locator("li[aria-current='step']:has-text('Qualifié')").count() === 1);
await resp.goto(`${fiche}&onglet=interventions`);
await resp.fill("input[name='Affectation.Titre']", "Reprendre le solin");
await resp.selectOption("select[name='Affectation.IntervenantCompteId']", { label: "Marc Technicien" });
await envoyer(resp, "form:has(button:has-text('Affecter'))");
ok("intervention affectée, échéance de prise en charge affichée", (await resp.textContent("main")).includes("Reprendre le solin") && (await resp.textContent(".delai-dossier")).includes("Prise en charge"));

// 4. Technicien sur téléphone : Mes interventions, démarrage, clôture
await tech.goto(`${base}/organisations/${org}/interventions`);
ok("« Mes interventions » sur téléphone", (await tech.textContent(".liste-interventions")).includes("Reprendre le solin"));
await tech.screenshot({ path: "us09-mes-interventions-tel.png", fullPage: true });
await tech.click(".ligne-intervention"); await tech.waitForLoadState();
await Promise.all([tech.waitForNavigation(), tech.click("button:has-text('Démarrer')")]);
ok("intervention démarrée", (await tech.textContent(".grille-interventions")).includes("En cours"));
await tech.click("a:has-text('Clôturer')"); await tech.waitForLoadState();
await tech.fill("textarea[name='Cloture.Actions']", "Solin refait au mortier, tuiles reposées");
await tech.check("input[name='Cloture.EtatFinal'][value='Resolu']");
await envoyer(tech, "form:has(button:has-text('Clôturer'))");
ok("dossier clôturé avec sa dernière intervention", await tech.locator("li[aria-current='step']:has-text('Clôturé')").count() === 1);

// 5. Responsable : suivi en colonnes, archives, captures Jour et Nuit
await resp.goto(`${base}/organisations/${org}/dossiers?archives=true`);
ok("dossier dans les archives", (await resp.textContent("main")).includes("Fuite au plafond de la sacristie"));
await resp.goto(`${base}/organisations/${org}/dossiers`);
await resp.screenshot({ path: "us09-suivi-jour.png", fullPage: true });
await resp.goto(`${fiche}&onglet=interventions`);
await resp.screenshot({ path: "us09-dossier-jour.png", fullPage: true });
await resp.evaluate(() => document.documentElement.setAttribute("data-theme", "nuit"));
await resp.screenshot({ path: "us09-dossier-nuit.png", fullPage: true });
violations.forEach(v => console.log("  violation : " + v.slice(0, 300)));
ok("aucune violation de la politique de sécurité du contenu", violations.length === 0);
await b.close();

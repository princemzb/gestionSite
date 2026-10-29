// Vérification de la préproduction pour US-11 (navigateur réel, Chromium sans interface via Playwright 1.55.0) :
// évaluation pas à pas sur téléphone (points, mesure, état « Critique » avec photo), proposition de dossier prérempli,
// lot réparti par état, état du site (note, couverture, points critiques, zones) ; captures Jour et Nuit ; aucune
// violation de la politique de sécurité du contenu.
// Usage, dans un dossier où playwright est installé (captures écrites dans ce dossier) :
//   VERIF_URL=<adresse publique> VERIF_ORG=<organisation> VERIF_JETON=<jeton d'activation> node us11.mjs
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
const cliquer = async (p, selecteur) => { await Promise.all([p.waitForNavigation(), p.click(selecteur)]); };

// 1. Administrateur : activation, invitation d'un technicien, qui active son compte sur téléphone
const admin = await page();
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
await admin.goto(`${base}/organisations/${org}/membres?inviter=1`);
await admin.fill("input[name='Saisie.Nom']", "Marc Technicien");
await admin.fill("input[name='Saisie.Email']", `technicien-${randomBytes(4).toString("hex")}@demo.gestionsite.test`);
await admin.click("button:has-text('Continuer')"); await admin.waitForLoadState();
await admin.selectOption("main select >> nth=0", "Technicien");
await admin.click("button:has-text(\"Créer l'invitation\")"); await admin.waitForLoadState();
const lien = (await admin.textContent("code.lien-activation")).trim();
ok("invitation du technicien créée", lien.startsWith(`${base}/activation?jeton=`));
const tel = await page(390, 844);
await activer(tel, lien);

// 2. Inventaire : bâtiment, tableau divisionnaire et 40 chaises dans les parties communes
const inventaire = `${base}/organisations/${org}/inventaire`;
const envoyer = async () => cliquer(tel, "form.formulaire-inventaire button[type=submit]");
await tel.goto(`${inventaire}?action=batiment`);
await tel.fill("input[name='StructureSaisie.Nom']", "Église"); await envoyer();
await cliquer(tel, ".arbre a:has-text('Parties communes')");
await cliquer(tel, "a:has-text('Ajouter un composant')");
await tel.fill("input[name='ComposantSaisi.Nom']", "Tableau divisionnaire");
await tel.selectOption("select[name='ComposantSaisi.CorpsEtat']", "CourantsForts"); await envoyer();
const ficheTableau = tel.url();
await cliquer(tel, "a:has-text('Ajouter un composant')");
await tel.fill("input[name='ComposantSaisi.Nom']", "Chaises");
await tel.selectOption("select[name='ComposantSaisi.CorpsEtat']", "Mobilier");
await tel.selectOption("select[name='ComposantSaisi.Mode']", "lot");
await tel.fill("input[name='ComposantSaisi.Quantite']", "40"); await tel.fill("input[name='ComposantSaisi.Bon']", "40"); await envoyer();
const ficheChaises = tel.url();
ok("composants créés, non évalués", (await tel.textContent(".fiche-composant")).includes("Non évalué"));

// 3. Évaluation pas à pas du tableau sur téléphone : défaut constaté, mesure, état « Critique » avec photo
await tel.goto(ficheTableau);
await cliquer(tel, ".fiche-composant a:has-text('Évaluer')");
ok("étape 1 : points à observer de la grille Courants forts", (await tel.textContent("main")).includes("Pas de trace d'échauffement (noircissement, odeur)"));
await tel.check("input[name='Saisie.Constats[2]'][value='non']");
await tel.fill("input[name='Saisie.Mesures[0]']", "48,5");
await tel.screenshot({ path: "us11-evaluation-etape1-tel.png", fullPage: true });
await cliquer(tel, "button[value='suivant']");
await tel.check("input[name='Saisie.Etat'][value='Critique']");
await tel.screenshot({ path: "us11-evaluation-etape2-tel.png", fullPage: true });
await cliquer(tel, "button[value='suivant']");
ok("étape 3 : photo exigée pour un état critique", (await tel.textContent("main")).includes("obligatoire : état mauvais ou critique"));
await tel.setInputFiles("input[name='photos']", { name: "constat.png", mimeType: "image/png", buffer: await tel.screenshot() });
await tel.fill("textarea[name='Saisie.Remarque']", "Odeur de brûlé au départ « Sono »");
await cliquer(tel, "button[value='enregistrer']");
const fiche = await tel.textContent(".fiche-composant");
ok("évaluation enregistrée : note 1,0 Critique, défaut et mesure affichés",
  fiche.includes("Évaluation enregistrée : 1,0 · Critique.") && fiche.includes("Pas de trace d'échauffement") && fiche.includes("48,5 °C"));
ok("photo de l'évaluation affichée", await tel.locator(".fiche-composant img[alt='Photo 1 de l\\'évaluation']").evaluate(i => i.complete && i.naturalWidth > 0));
await tel.screenshot({ path: "us11-fiche-evaluee-tel.png", fullPage: true });
await cliquer(tel, "a:has-text('Ouvrir un dossier prérempli')");
ok("déclaration préremplie proposée (constat « Critique »)", (await tel.inputValue("textarea[name='Saisie.Description']")).includes("évalué « Critique »"));

// 4. Lot : répartition par état, reprise dans l'inventaire
await tel.goto(ficheChaises);
await cliquer(tel, ".fiche-composant a:has-text('Évaluer')");
await cliquer(tel, "button[value='suivant']");
await tel.fill("input[name='Saisie.Bon']", "32"); await tel.fill("input[name='Saisie.Degrade']", "8");
await cliquer(tel, "button[value='suivant']");
await cliquer(tel, "button[value='enregistrer']");
ok("lot évalué : note 8,2 et répartition reprise", (await tel.textContent(".fiche-composant")).includes("Évaluation : bon 40 → 32, dégradé 0 → 8"));

// 5. État du site : note, couverture, point critique nommé, zones ; captures Jour, Nuit et téléphone
await tel.goto(`${base}/organisations/${org}/etat`);
await tel.screenshot({ path: "us11-etat-site-tel.png", fullPage: true });
const etat = await tel.textContent("main");
ok("état du site : note, couverture et point critique nommé",
  etat.includes("2 composants évalués sur 2") && etat.includes("Tableau divisionnaire") && await tel.locator(".carte-zone:has-text('Parties communes')").count() === 1);
await tel.setViewportSize({ width: 1440, height: 900 });
await tel.screenshot({ path: "us11-etat-site-jour.png", fullPage: true });
await tel.evaluate(() => document.documentElement.setAttribute("data-theme", "nuit"));
await tel.screenshot({ path: "us11-etat-site-nuit.png", fullPage: true });
violations.forEach(v => console.log("  violation : " + v.slice(0, 300)));
ok("aucune violation de la politique de sécurité du contenu", violations.length === 0);
await b.close();

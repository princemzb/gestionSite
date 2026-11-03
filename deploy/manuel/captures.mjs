// Captures d'écran du manuel d'utilisation (DOC-01, docs/manuel/), prises en préproduction sur une organisation fictive
// (Chromium sans interface, Playwright 1.55.0), thème Jour. Le script joue un scénario complet avec des personnes et des
// lieux inventés : administration, inventaire, évaluations, dossiers, préventif, plans de bâtiment, formulaire public.
// Avant chaque capture, l'adresse de la préproduction est remplacée par « https://gestionsite.exemple », les jetons
// d'activation sont masqués et les QR codes floutés : aucune adresse réelle ni lien utilisable n'entre dans le dépôt.
// Usage, dans un dossier où playwright est installé (captures écrites dans CAPTURES, par défaut ./images) :
//   VERIF_URL=<adresse publique> VERIF_ORG=<organisation> VERIF_JETON=<jeton d'activation> node captures.mjs
// (organisation fictive ouverte par « ouvrir-organisation --site "Église du centre" » ; jeton jamais passé en argument).
// Le dépôt public réel n'est pas joué : le défi anti-robot refuse un navigateur automatisé.
import { chromium } from "playwright";
import { randomBytes } from "node:crypto";
import { mkdirSync } from "node:fs";
import { join } from "node:path";

const { VERIF_URL: base, VERIF_ORG: org, VERIF_JETON: jeton } = process.env;
if (!base || !org || !jeton) { console.error("VERIF_URL, VERIF_ORG et VERIF_JETON sont requis."); process.exit(2); }
const dossierCaptures = process.env.CAPTURES ?? "images";
mkdirSync(dossierCaptures, { recursive: true });
const mdp = randomBytes(12).toString("hex") + "-manuel";
const ok = (nom, cond) => { console.log((cond ? "OK    " : "ÉCHEC ") + nom); if (!cond) process.exit(1); };
const b = await chromium.launch({ args: ["--lang=fr-FR"] }); // boutons de fichier et dates en français
const ADRESSE_FICTIVE = "https://gestionsite.exemple";
const BUREAU = { largeur: 1280, hauteur: 800 };
const TELEPHONE = { largeur: 390, hauteur: 844 };

const ouvrir = async ({ largeur, hauteur }, tactile = false) => {
  const contexte = await b.newContext({ viewport: { width: largeur, height: hauteur }, hasTouch: tactile, locale: "fr-FR", timezoneId: "Europe/Paris" });
  return contexte.newPage();
};
const activer = async (p, lien) => {
  await p.goto(lien, { timeout: 90000 });
  await p.fill("input[type=password] >> nth=0", mdp); await p.fill("input[type=password] >> nth=1", mdp);
  await Promise.all([p.waitForNavigation(), p.click("button[type=submit]")]);
};
const cliquer = async (p, selecteur) => { await Promise.all([p.waitForNavigation(), p.click(selecteur)]); };
const texte = async (p, selecteur = "main") => (await p.textContent(selecteur)) ?? "";
const date = jours => new Date(Date.now() + jours * 86400000).toISOString().slice(0, 10);

// Masquage : adresse réelle remplacée, jetons tronqués, QR floutés (le QR code encode l'adresse de la préproduction).
async function masquer(p) {
  await p.evaluate(([reelle, fictive]) => {
    const parcours = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let n = parcours.nextNode(); n; n = parcours.nextNode()) {
      n.nodeValue = n.nodeValue.split(reelle).join(fictive).replace(/jeton=[\w%-]+/g, "jeton=…");
    }
    for (const qr of document.querySelectorAll(".qr svg, .affiche-qr svg")) qr.style.filter = "blur(14px)";
    for (const champ of document.querySelectorAll("input[value*='jeton=']")) champ.value = "…";
  }, [base.replace(/\/$/, ""), ADRESSE_FICTIVE]);
}
async function capture(p, nom, { pleinePage = false, selecteur } = {}) {
  await masquer(p);
  const chemin = join(dossierCaptures, `${nom}.png`);
  if (selecteur) await p.locator(selecteur).first().screenshot({ path: chemin });
  else await p.screenshot({ path: chemin, fullPage: pleinePage });
  console.log(`      capture ${chemin}`);
}

// Images d'exemple produites par le navigateur : une « photo » neutre, un fond de plan et un rapport PDF fictifs.
const atelier = await ouvrir({ largeur: 800, hauteur: 600 });
await atelier.setContent(`<body style="margin:0;display:grid;place-items:center;height:600px;font:600 34px sans-serif;color:#3D4654;
  background:linear-gradient(135deg,#d9d4cc,#a39a8c)">Photo d'exemple</body>`);
const photo = await atelier.screenshot({ type: "jpeg", quality: 80 });
await atelier.setViewportSize({ width: 2000, height: 1400 });
await atelier.setContent(`<body style="margin:0;background:#fff"><svg width="2000" height="1400" xmlns="http://www.w3.org/2000/svg"
  fill="none" stroke="#222" stroke-width="10"><rect x="100" y="200" width="1400" height="890"/><rect x="1500" y="420" width="380" height="450"/>
  <rect x="100" y="1090" width="420" height="220"/><line x1="560" y1="200" x2="560" y2="1090" stroke-dasharray="30 20" stroke-width="4"/>
  <g font-family="sans-serif" font-size="56" fill="#444" stroke="none"><text x="700" y="660">Nef</text><text x="1580" y="660">Chœur</text>
  <text x="160" y="1220">Sacristie</text><text x="140" y="140">Plan d'exemple (fictif)</text></g></svg></body>`);
const fondPlan = await atelier.screenshot({ type: "png" });
await atelier.setContent(`<body style="font:16px sans-serif;margin:48px"><h1>Rapport de vérification — exemple fictif</h1>
  <p>Document de démonstration pour le manuel de gestionSite. Aucun organisme réel.</p></body>`);
const rapport = await atelier.pdf({ format: "A4" });
await atelier.context().close();

// 1. Connexion et activation de l'administratrice
const anonyme = await ouvrir(BUREAU);
await anonyme.goto(`${base}/connexion`, { timeout: 90000 });
await capture(anonyme, "connexion");
const admin = await ouvrir(BUREAU);
await admin.goto(`${base}/activation?jeton=${encodeURIComponent(jeton)}`, { timeout: 90000 });
await capture(admin, "activation");
await admin.fill("input[type=password] >> nth=0", mdp); await admin.fill("input[type=password] >> nth=1", mdp);
await cliquer(admin, "button[type=submit]");
ok("l'administratrice arrive sur les membres", admin.url().includes("/membres"));

// 2. Invitations en trois étapes (captures sur la première)
const membres = `${base}/organisations/${org}/membres`;
const inviter = async (nom, role, captures = false) => {
  await admin.goto(`${membres}?inviter=1`);
  await admin.fill("input[name='Saisie.Nom']", nom);
  await admin.fill("input[name='Saisie.Email']", `${nom.split(" ")[0].toLowerCase()}-${randomBytes(3).toString("hex")}@demo.gestionsite.test`);
  if (captures) await capture(admin, "membres-inviter-1");
  await cliquer(admin, "button:has-text('Continuer')");
  await admin.selectOption("main select >> nth=0", role);
  if (captures) await capture(admin, "membres-inviter-2");
  await cliquer(admin, "button:has-text(\"Créer l'invitation\")");
  const lien = (await admin.textContent("code.lien-activation")).trim(); // lu avant le masquage de la capture
  if (captures) await capture(admin, "membres-inviter-3");
  return lien;
};
const lienRose = await inviter("Rose Responsable", "Responsable", true);
const lienMarc = await inviter("Marc Technicien", "Technicien");
const lienPaul = await inviter("Paul Pasteur", "Pasteur");
await inviter("Denis Délégué", "Delegue");
ok("quatre invitations créées", [lienRose, lienMarc, lienPaul].every(l => l.includes("/activation?jeton=")));

// 3. Sites : numéro à appeler en cas de danger, lien public, affiche
const sites = `${base}/organisations/${org}/sites`;
await admin.goto(sites);
await admin.fill("input[name='numero']", "01 00 00 00 00");
await cliquer(admin, "form.numero-urgence button");
ok("numéro d'urgence enregistré", (await texte(admin)).includes("Numéro enregistré."));
const lienPublic = (await admin.textContent(".carte-site code.lien-activation")).trim();
await capture(admin, "sites");
await admin.goto(new URL(await admin.getAttribute("a:has-text(\"Imprimer l'affiche\")", "href"), base).toString());
await capture(admin, "affiche", { pleinePage: true });

// 4. Comptes activés : responsable (ordinateur), technicien (téléphone), pasteur (ordinateur)
const rose = await ouvrir(BUREAU);
await activer(rose, lienRose);
const marc = await ouvrir(TELEPHONE, true);
await activer(marc, lienMarc);
const paul = await ouvrir(BUREAU);
await activer(paul, lienPaul);
await admin.goto(membres);
await capture(admin, "membres");

// 5. Inventaire : bâtiments, niveaux, zones et composants
const inventaire = `${base}/organisations/${org}/inventaire`;
const envoyer = async p => cliquer(p, "form.formulaire-inventaire button[type=submit]");
const batiment = async nom => {
  await rose.goto(`${inventaire}?action=batiment`);
  await rose.fill("input[name='StructureSaisie.Nom']", nom); await envoyer(rose);
  const lien = await rose.getAttribute(`.arbre a.arbre-batiment:has-text('${nom}')`, "href");
  return new URL(lien, base).searchParams.get("batiment");
};
const niveau = async (bat, nom, ordre) => {
  await rose.goto(`${inventaire}?action=niveau&pour=${bat}`);
  await rose.fill("input[name='StructureSaisie.Nom']", nom); await rose.fill("input[name='StructureSaisie.Ordre']", ordre); await envoyer(rose);
};
const zone = async (bat, nom, nomNiveau) => {
  await rose.goto(`${inventaire}?action=zone&pour=${bat}`);
  await rose.fill("input[name='StructureSaisie.Nom']", nom);
  if (nomNiveau) await rose.selectOption("select[name='StructureSaisie.NiveauId']", { label: nomNiveau });
  if (nom === "Sacristie") await capture(rose, "inventaire-zone");
  await envoyer(rose);
};
const eglise = await batiment("Église");
await niveau(eglise, "Rez-de-chaussée", "0");
await niveau(eglise, "Tribune", "1");
for (const z of ["Nef", "Chœur", "Sacristie"]) await zone(eglise, z, "Rez-de-chaussée");
await zone(eglise, "Tribune de l'orgue", "Tribune");
const salles = await batiment("Salles paroissiales");
for (const z of ["Grande salle", "Cuisine"]) await zone(salles, z);
const exterieurs = await batiment("Extérieurs");
await zone(exterieurs, "Parvis");

const composant = async (nomZone, champs, captureNom) => {
  await rose.goto(inventaire);
  await cliquer(rose, `.arbre a:has-text('${nomZone}')`);
  await cliquer(rose, "a:has-text('Ajouter un composant')");
  for (const [nom, valeur] of Object.entries(champs)) {
    const champ = rose.locator(`[name='ComposantSaisi.${nom}']`);
    if (await champ.evaluate(e => e.tagName) === "SELECT") await champ.selectOption(valeur); else await champ.fill(valeur);
  }
  if (captureNom) await capture(rose, captureNom, { pleinePage: true });
  await envoyer(rose);
  return rose.url();
};
const ficheTableau = await composant("Sacristie", { Nom: "Tableau divisionnaire", CorpsEtat: "CourantsForts", Marque: "Marque d'exemple", Annee: "2004" });
await composant("Nef", { Nom: "Blocs d'éclairage de sécurité (BAES)", CorpsEtat: "EclairageSecurite", Mode: "lot", Quantite: "12" });
const ficheChaises = await composant("Nef", { Nom: "Chaises", CorpsEtat: "Mobilier", Mode: "lot", Quantite: "120", Bon: "100", Satisfaisant: "15", Degrade: "5" }, "inventaire-composant");
await composant("Nef", { Nom: "Extincteur eau pulvérisée 6 L", CorpsEtat: "SecuriteIncendie", Emplacement: "Près de la porte principale", Annee: "2021" });
await composant("Grande salle", { Nom: "Chaudière gaz", CorpsEtat: "ChauffageVentilation", Annee: "2012" });
await composant("Chœur", { Nom: "Orgue de chœur", CorpsEtat: "EquipementsCulte" });
await rose.goto(ficheTableau);
await capture(rose, "inventaire", { pleinePage: true });
ok("inventaire décrit", (await texte(rose)).includes("Tableau divisionnaire"));

// 6. Évaluations sur téléphone : tableau électrique (défaut, mesure, état mauvais, photo) et lot de chaises
await marc.goto(ficheTableau);
await cliquer(marc, ".fiche-composant a:has-text('Évaluer')");
await marc.check("input[name='Saisie.Constats[0]'][value='oui']");
await marc.check("input[name='Saisie.Constats[1]'][value='oui']");
await marc.check("input[name='Saisie.Constats[2]'][value='non']");
await marc.fill("input[name='Saisie.Mesures[0]']", "41,5");
await capture(marc, "evaluer-etape1-tel", { pleinePage: true });
await cliquer(marc, "button[value='suivant']");
await marc.check("input[name='Saisie.Etat'][value='Mauvais']");
await capture(marc, "evaluer-etape2-tel", { pleinePage: true });
await cliquer(marc, "button[value='suivant']");
await marc.setInputFiles("input[name='photos']", { name: "constat.jpg", mimeType: "image/jpeg", buffer: photo });
await marc.fill("textarea[name='Saisie.Remarque']", "Léger noircissement au départ de la sonorisation.");
await capture(marc, "evaluer-etape3-tel", { pleinePage: true });
await cliquer(marc, "button[value='enregistrer']");
ok("évaluation enregistrée", (await texte(marc, ".fiche-composant")).includes("Évaluation enregistrée"));
await capture(marc, "evaluer-fiche-tel", { pleinePage: true });
await marc.goto(ficheChaises);
await cliquer(marc, ".fiche-composant a:has-text('Évaluer')");
await cliquer(marc, "button[value='suivant']");
await marc.fill("input[name='Saisie.Bon']", "90"); await marc.fill("input[name='Saisie.Satisfaisant']", "22"); await marc.fill("input[name='Saisie.Degrade']", "8");
await capture(marc, "evaluer-lot-tel", { pleinePage: true });
await cliquer(marc, "button[value='suivant']");
await cliquer(marc, "button[value='enregistrer']");
await rose.goto(`${base}/organisations/${org}/etat`);
await capture(rose, "etat-site", { pleinePage: true });
await rose.goto(`${base}/organisations/${org}/grilles?corps=Mobilier`);
await capture(rose, "grilles", { pleinePage: true });

// 7. Dossiers : déclarations internes (responsable, pasteur), danger, qualification, affectation
const dossiers = `${base}/organisations/${org}/dossiers`;
const declarer = async (p, { zone: nomZone, description, nature = "Incident", danger = false, emplacement }, captureNom) => {
  await p.goto(dossiers);
  await cliquer(p, "a:has-text('Déclarer un problème')");
  const valeur = await p.evaluate(nom => [...document.querySelectorAll("select[name='Saisie.ZoneId'] option")].find(o => o.textContent.startsWith(nom))?.value, nomZone);
  await p.selectOption("select[name='Saisie.ZoneId']", valeur);
  await p.selectOption("select[name='Saisie.Nature']", nature);
  if (emplacement) await p.fill("input[name='Saisie.Emplacement']", emplacement);
  await p.fill("textarea[name='Saisie.Description']", description);
  if (danger) await p.check("input[name='Saisie.Danger']");
  if (captureNom) await capture(p, captureNom, { pleinePage: true });
  await envoyer(p);
  return p.url().split("&")[0];
};
const fuite = await declarer(rose, { zone: "Sacristie", description: "Fuite d'eau au plafond de la sacristie après les dernières pluies.", emplacement: "Angle nord, au-dessus de l'armoire" }, "dossier-declarer");
const porte = await declarer(paul, { zone: "Grande salle", description: "La porte de la grande salle frotte et ferme mal." });
await declarer(paul, { zone: "Chœur", nature: "Demande", description: "Prévoir deux pupitres supplémentaires pour la chorale." });
await declarer(rose, { zone: "Nef", description: "Câble électrique dénudé derrière le dernier banc.", danger: true });
const gouttiere = await declarer(rose, { zone: "Parvis", description: "Gouttière décrochée côté parvis." });

const qualifier = async (fiche, priorite, captureNom) => {
  await rose.goto(fiche);
  await rose.check(`input[name='Qualification.Priorite'][value='${priorite}']`);
  if (captureNom) await capture(rose, captureNom, { pleinePage: true });
  await cliquer(rose, "button:has-text('Enregistrer la qualification')");
};
const affecter = async (fiche, titre, { technicien, prestataire, referent }, captureNom) => {
  await rose.goto(`${fiche}&onglet=interventions`);
  await rose.fill("input[name='Affectation.Titre']", titre);
  if (technicien) await rose.selectOption("select[name='Affectation.IntervenantCompteId']", { label: technicien });
  if (prestataire) await rose.fill("input[name='Affectation.Prestataire']", prestataire);
  if (referent) await rose.selectOption("select[name='Affectation.ReferentCompteId']", { label: referent });
  if (captureNom) await capture(rose, captureNom, { pleinePage: true });
  await cliquer(rose, "button:has-text('Affecter')");
};
await qualifier(fuite, "Haute", "dossier-qualifier");
await affecter(fuite, "Reprendre l'étanchéité au-dessus de la sacristie", { technicien: "Marc Technicien" }, "dossier-affecter");
await qualifier(porte, "Basse");
await affecter(porte, "Raboter et régler la porte", { prestataire: "Menuiserie d'exemple", referent: "Marc Technicien" });
await qualifier(gouttiere, "Normale");
await rose.goto(`${porte}&onglet=interventions`);
ok("intervention confiée au prestataire, suivie par le technicien", (await texte(rose)).includes("Menuiserie d'exemple (prestataire) · suivi par Marc Technicien"));

// 8. Technicien sur téléphone : mes interventions, démarrer, attente, reprise, clôture
await marc.goto(`${base}/organisations/${org}/interventions`);
await capture(marc, "mes-interventions-tel", { pleinePage: true });
await cliquer(marc, ".ligne-intervention:has-text('étanchéité')");
await cliquer(marc, "button:has-text('Démarrer')");
await cliquer(marc, "a:has-text('Mettre en attente')");
await marc.selectOption("select[name='Attente.Motif']", "Livraison");
await marc.fill("input[name='Attente.ReexamenLe']", date(7));
await capture(marc, "dossier-attente-tel", { pleinePage: true });
await cliquer(marc, "form:has(button:has-text('Mettre en attente')) button[type=submit]");
ok("intervention en attente", (await texte(marc)).includes("Reprendre"));
await cliquer(marc, "button:has-text('Reprendre')");
await cliquer(marc, "a:has-text('Clôturer')");
await marc.fill("textarea[name='Cloture.Actions']", "Solin refait au mortier, deux tuiles remplacées, plafond séché.");
await marc.check("input[name='Cloture.EtatFinal'][value='Resolu']");
await marc.setInputFiles("input[name='photos']", { name: "apres.jpg", mimeType: "image/jpeg", buffer: photo });
await capture(marc, "dossier-cloture-tel", { pleinePage: true });
await cliquer(marc, "form:has(button:has-text('Clôturer')) button[type=submit]");
ok("dossier clôturé", await marc.locator("li[aria-current='step']:has-text('Clôturé')").count() === 1);
await rose.goto(`${fuite}&onglet=historique`);
await capture(rose, "dossier-historique", { pleinePage: true });
await rose.goto(dossiers);
await capture(rose, "dossiers-suivi", { pleinePage: true });
await rose.goto(`${dossiers}?archives=true`);
await capture(rose, "dossiers-archives");

// 9. Préventif : classement ERP, plans proposés, calendrier, affectation, contrôle sur téléphone, vérification extérieure
const preventif = `${base}/organisations/${org}/preventif`;
await rose.goto(`${preventif}/propositions`);
await cliquer(rose, "nav[aria-label='Bâtiment'] a:has-text('Église')"); // bâtiments rangés par nom : l'église n'est pas le premier
await rose.check("input[name='Classement.Erp']");
await rose.selectOption("select[name='Classement.Type']", "V");
await rose.selectOption("select[name='Classement.Categorie']", "5");
await cliquer(rose, "button:has-text('Enregistrer le classement')");
await capture(rose, "preventif-propositions", { pleinePage: true });
const confirmer = async (titre, cible) =>
  cliquer(rose, `.proposition:has-text("${titre}"):has-text("${cible}") form:has(input[name='cle']) button[type=submit] >> nth=0`);
await confirmer("Inspection avec la grille", "Tableau divisionnaire");
await confirmer("installations électriques", "Tableau divisionnaire");
await confirmer("Nettoyage des chéneaux", "Bâtiment : Église");
await rose.goto(preventif);
await capture(rose, "preventif-calendrier", { pleinePage: true });
const plan = async libelle => new URL(await rose.getAttribute(`.panneau-echeances a:has-text("${libelle}") >> nth=0`, "href"), base).toString();
const planInspection = await plan("Inspection avec la grille");
const planVerification = await plan("installations électriques");
await rose.goto(planInspection);
await rose.selectOption("select[name='Affectation.IntervenantCompteId']", { label: "Marc Technicien" });
await capture(rose, "preventif-fiche-plan", { pleinePage: true });
await cliquer(rose, "button:has-text('Affecter')");
ok("échéance affectée", (await texte(rose)).includes("Affectée à Marc Technicien"));

await marc.goto(preventif);
await capture(marc, "preventif-mes-controles-tel", { pleinePage: true });
await cliquer(marc, ".panneau-echeances a:has-text('Contrôler')");
for (let i = 0; i < 4; i++) {
  const choix = marc.locator(`input[name='Saisie.Constats[${i}]'][value='${i === 1 ? "non" : "oui"}']`);
  if (await choix.count()) await choix.check();
}
await capture(marc, "controler-etape1-tel", { pleinePage: true });
await cliquer(marc, "button[value='suivant']");
await marc.check("input[name='Saisie.Etat'][value='Degrade']");
await cliquer(marc, "button[value='suivant']");
await marc.check("input[name='Saisie.Resultat'][value='AvecObservations']");
await marc.fill("textarea[name='Saisie.CompteRendu']", "Repérage des circuits à refaire.");
await capture(marc, "controler-resultat-tel", { pleinePage: true });
await cliquer(marc, "button[value='enregistrer']");
ok("contrôle enregistré", (await texte(marc)).includes("Contrôle enregistré"));
await capture(marc, "fiche-controle-tel", { pleinePage: true });

await rose.goto(planVerification);
await cliquer(rose, "a:has-text('Faire le contrôle')");
await rose.fill("input[name='Saisie.Prestataire']", "Bureau de contrôle d'exemple");
await rose.fill("textarea[name='Saisie.Observations[0].Libelle']", "Repérage des circuits absent au tableau de la sacristie.");
const sacristie = await rose.evaluate(() => [...document.querySelectorAll("select[name='Saisie.Observations[0].ZoneId'] option")].find(o => o.textContent.includes("Sacristie"))?.value);
await rose.selectOption("select[name='Saisie.Observations[0].ZoneId']", sacristie);
await capture(rose, "verification-observations", { pleinePage: true });
await cliquer(rose, ".actions-evaluation button[value='suivant']"); // le premier « Suivant », masqué, sert à la touche Entrée
await rose.check("input[name='Saisie.Resultat'][value='AvecObservations']");
await rose.setInputFiles("input[name='rapport']", { name: "rapport-exemple.pdf", mimeType: "application/pdf", buffer: rapport });
await capture(rose, "verification-rapport", { pleinePage: true });
await cliquer(rose, "button[value='enregistrer']");
ok("vérification extérieure enregistrée avec rapport", (await texte(rose)).includes("Télécharger le rapport PDF"));
await capture(rose, "fiche-controle", { pleinePage: true });
await rose.goto(`${preventif}/controles`);
await capture(rose, "controles-historique");

// 10. Plans de bâtiment sur tablette : parcours guidé (fond, calibration), atelier, brouillon après conflit
const plans = `${base}/organisations/${org}/plans`;
await rose.setViewportSize({ width: 1280, height: 800 });
await rose.goto(plans);
await cliquer(rose, "nav[aria-label='Bâtiment'] a:has-text('Église')"); // rez-de-chaussée de l'église, premier niveau
await capture(rose, "plans-guide-fond");
await rose.setInputFiles("input[name=fichier]", { name: "plan-exemple.png", mimeType: "image/png", buffer: fondPlan });
await rose.click("button:has-text('Importer ce fond')");
await rose.waitForSelector("[data-panneau=calibration]:not([hidden])", { timeout: 120000 });
await rose.waitForSelector("[data-role=fond] image");
const toucher = async (px, py) => {
  const { x, y } = await rose.evaluate(([px, py]) => {
    const svg = document.querySelector("[data-role=svg]");
    const image = svg.querySelector("[data-role=fond] image");
    const metres = Number(image.getAttribute("width")) / 2000;
    const [vx, vy, vl] = svg.getAttribute("viewBox").split(" ").map(Number);
    const r = svg.getBoundingClientRect();
    return { x: r.left + (px * metres - vx) * r.width / vl, y: r.top + (py * metres - vy) * r.width / vl };
  }, [px, py]);
  await rose.mouse.click(x, y);
};
await toucher(100, 200); await toucher(1500, 200);
await rose.fill("[data-champ=calibration]", "14,00"); await rose.press("[data-champ=calibration]", "Tab");
await rose.click("[data-cible=controle]");
await toucher(100, 200); await toucher(100, 1090);
await rose.fill("[data-champ=controle]", "8,90"); await rose.press("[data-champ=controle]", "Tab");
await capture(rose, "plans-calibration");
await Promise.all([rose.waitForNavigation({ timeout: 30000 }), rose.click("[data-action=valider-calibration]")]);
await rose.waitForFunction(() => document.querySelector("[data-role=resume]")?.textContent.includes("Version"));
await rose.waitForSelector("[data-role=fond] image");
await rose.waitForFunction(() => document.querySelector("[data-role=fond] image")?.getBoundingClientRect().width > 0);
await rose.waitForTimeout(2000); // fond affiché avant la mesure
await rose.click("[data-outil=cote]");
await toucher(560, 200); await toucher(560, 1090);
await capture(rose, "plans-atelier");
await rose.click("[data-action=changer-fond]");
await rose.click("[data-action=grille]");
await rose.waitForTimeout(500);
await rose.evaluate(async () => {
  const d = document.querySelector("#editeur-plan").dataset;
  const chemin = `/organisations/${d.organisation}/sites/${d.site}/plans-batiment/${d.plan}`;
  const lue = await (await fetch(chemin)).json();
  await fetch(chemin, { method: "PUT", headers: { "Content-Type": "application/json", RequestVerificationToken: d.jeton },
    body: JSON.stringify({ commandeId: crypto.randomUUID(), version: lue.version, motif: "Enregistrement concurrent", contenu: lue.contenu }) });
});
await rose.click("[data-action=enregistrer]");
await rose.waitForSelector("[data-role=brouillon]:not([hidden])");
await capture(rose, "plans-conflit");
await rose.click("[data-action=brouillon-abandonner]");
await rose.waitForTimeout(1000);
await paul.goto(plans);
await cliquer(paul, "nav[aria-label='Bâtiment'] a:has-text('Église')");
await cliquer(paul, "nav[aria-label=Niveau] a:has-text('Tribune')");
await capture(paul, "plans-absent");

// 11. Formulaire public sur téléphone (sans compte) : choix puis formulaire de danger rempli (non envoyé)
const fidele = await ouvrir(TELEPHONE, true);
const chemin = new URL(lienPublic).pathname;
await fidele.goto(`${base}${chemin}`, { timeout: 90000 });
await capture(fidele, "public-choix-tel", { pleinePage: true });
await cliquer(fidele, "a.tuile-danger");
await fidele.fill("textarea[name='Saisie.Description']", "De l'eau coule le long du tableau électrique.");
await fidele.fill("input[name='Saisie.Emplacement']", "Couloir de la sacristie");
await fidele.fill("input[name='Saisie.Nom']", "Jeanne Exemple");
await fidele.fill("input[name='Saisie.Contact']", "06 00 00 00 00");
await capture(fidele, "public-formulaire-tel", { pleinePage: true });

ok("captures terminées", true);
await b.close();

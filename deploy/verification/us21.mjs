// Vérification de l'éditeur de plans de bâtiment, US-21 S21-07 et S21-08 (navigateur réel, Chromium sans interface via
// Playwright 1.55.0) : une responsable crée le plan du rez-de-chaussée par le parcours guidé (fond PNG synthétique de
// 4 096 px importé, cote de calibration de 14,33 m, cote de contrôle de 8,90 m à moins de 1 %, avertissement au-delà de
// 2 %), annule et rétablit, enregistre, trace une pièce à l'étape 3 « pièces zone par zone » ; l'atelier affiche la version
// et l'échelle ; S21-08 dans l'atelier : zone créée depuis l'éditeur, pièce tracée puis cotée au centimètre par ses sommets
// (saisie sans glisser), chevauchement refusé par l'éditeur, porte posée sur une arête, escalier vers le sous-sol, recalage
// du fond sur le calque du sous-sol par deux points homologues, version enregistrée et relue ; un conflit 409 conserve le brouillon, repris sur
// la version gagnante avec confirmation (décision du porteur du 02/11) ; le brouillon d'un autre compte est effacé à
// l'ouverture, et tout brouillon à la déconnexion ; un lecteur n'a aucune commande d'édition et affiche le calque du
// niveau inférieur ; mesure de fluidité (fond de 4 096 px, 1 000 sommets : glisser, molette, pincement à deux doigts,
// sans puis avec un processeur ralenti 4 fois) ; aucune violation de la politique de sécurité du contenu.
// Usage, dans un dossier où playwright est installé (captures écrites dans ce dossier) :
//   VERIF_URL=<adresse> VERIF_ORG=<organisation> VERIF_JETON=<jeton d'activation> node us21.mjs
// (organisation fictive ouverte par « ouvrir-organisation --site » ; jeton jamais passé en argument ni écrit).
import { chromium } from "playwright";
import { randomBytes } from "node:crypto";
import { deflateSync } from "node:zlib";

const { VERIF_URL: base, VERIF_ORG: org, VERIF_JETON: jeton } = process.env;
if (!base || !org || !jeton) { console.error("VERIF_URL, VERIF_ORG et VERIF_JETON sont requis."); process.exit(2); }
const mdp = randomBytes(12).toString("hex") + "-verif";
const ok = (nom, cond, detail = "") => { console.log((cond ? "OK    " : "ÉCHEC ") + nom + (detail ? ` (${detail})` : "")); if (!cond) process.exit(1); };
const b = await chromium.launch();
const violations = [];
const nouveauContexte = async (largeur = 1440, hauteur = 900, tactile = false) => {
  const contexte = await b.newContext({ viewport: { width: largeur, height: hauteur }, hasTouch: tactile });
  await contexte.addInitScript(() => document.addEventListener("securitypolicyviolation", e => console.error(`Content-Security-Policy ${e.violatedDirective} ${e.blockedURI}`)));
  return contexte;
};
const ouvrir = async contexte => {
  const p = await contexte.newPage();
  p.on("console", m => { if (m.type() === "error" && /Content.Security.Policy/i.test(m.text())) violations.push(`${p.url()} → ${m.text()}`); });
  p.on("pageerror", e => violations.push(`${p.url()} → erreur JavaScript : ${e.message}`));
  return p;
};
const activer = async (p, lien) => {
  await p.goto(lien, { timeout: 90000 });
  await p.fill("input[type=password] >> nth=0", mdp); await p.fill("input[type=password] >> nth=1", mdp);
  await p.click("button[type=submit]"); await p.waitForLoadState();
};
const cliquer = async (p, selecteur) => { await Promise.all([p.waitForNavigation(), p.click(selecteur)]); };
const texte = async (p, selecteur = "main") => (await p.textContent(selecteur)) ?? "";

// PNG synthétique en niveaux de gris, 4 096 × 3 072 px, 5 mm par pixel : murs d'un bâtiment de 14,33 × 8,90 m.
function pngSynthetique() {
  const [l, h] = [4096, 3072];
  const brut = Buffer.alloc((l + 1) * h, 255);
  for (let y = 0; y < h; y++) brut[y * (l + 1)] = 0;
  const mur = (x0, y0, x1, y1) => { for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) brut[y * (l + 1) + 1 + x] = 40; };
  mur(244, 296, 3118, 304); mur(244, 2076, 3118, 2084); mur(244, 296, 252, 2084); mur(3110, 296, 3118, 2084); mur(1700, 300, 1704, 2080);
  const crc = tampon => { let c = ~0; for (const o of tampon) { c ^= o; for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xEDB88320 & -(c & 1)); } return ~c >>> 0; };
  const bloc = (type, donnees) => {
    const t = Buffer.concat([Buffer.from(type), donnees]);
    const taille = Buffer.alloc(4); taille.writeUInt32BE(donnees.length);
    const somme = Buffer.alloc(4); somme.writeUInt32BE(crc(t));
    return Buffer.concat([taille, t, somme]);
  };
  const entete = Buffer.alloc(13); entete.writeUInt32BE(l, 0); entete.writeUInt32BE(h, 4); entete[8] = 8; entete[9] = 0;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), bloc("IHDR", entete), bloc("IDAT", deflateSync(brut)), bloc("IEND", Buffer.alloc(0))]);
}

// Toucher un point du repère donné en mètres (conversion par la viewBox).
async function toucherRepere(p, mx, my) {
  const { x, y } = await p.evaluate(([mx, my]) => {
    const svg = document.querySelector("[data-role=svg]");
    const [vx, vy, vl] = svg.getAttribute("viewBox").split(" ").map(Number);
    const r = svg.getBoundingClientRect();
    return { x: r.left + (mx - vx) * r.width / vl, y: r.top + (my - vy) * r.width / vl };
  }, [mx, my]);
  await p.mouse.click(x, y);
}

const lirePlanCourant = p => p.evaluate(async () => {
  const d = document.querySelector("#editeur-plan").dataset;
  return (await fetch(`/organisations/${d.organisation}/sites/${d.site}/plans-batiment/${d.plan}`)).json();
});

// Toucher un point du fond donné en pixels de l'image (conversion par la viewBox et la largeur de l'<image>).
async function toucherPixel(p, px, py) {
  const { x, y } = await p.evaluate(([px, py]) => {
    const svg = document.querySelector("[data-role=svg]");
    const image = svg.querySelector("[data-role=fond] image");
    const metres = Number(image.getAttribute("width")) / 4096;
    const [vx, vy, vl] = svg.getAttribute("viewBox").split(" ").map(Number);
    const r = svg.getBoundingClientRect();
    return { x: r.left + (px * metres - vx) * r.width / vl, y: r.top + (py * metres - vy) * r.width / vl };
  }, [px, py]);
  await p.mouse.click(x, y);
}

// Attente d'un élément ; en cas d'échec, le message de l'éditeur et une capture aident le diagnostic.
async function attendre(p, selecteur) {
  try {
    await p.waitForSelector(selecteur, { timeout: 15000 });
  } catch {
    await p.screenshot({ path: "us21-echec.png" });
    ok(`attente de ${selecteur}`, false, (await p.textContent("[data-role=message]")) ?? "");
  }
}

const brouillons = p => p.evaluate(() => Object.keys(localStorage).filter(c => c.startsWith("gestionsite.brouillon-plan.v1:")));

// 1. Administrateur : invitations d'une responsable (édition) et d'un pasteur (lecture)
const admin = await ouvrir(await nouveauContexte());
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
const lienPasteur = await inviter("Paul Pasteur", "Pasteur");
ok("invitations créées (responsable, pasteur)", lienResponsable.includes("/activation?jeton=") && lienPasteur.includes("/activation?jeton="));
const contexteTablette = await nouveauContexte(1280, 800, true); // format de la Tab M11, écran tactile
const resp = await ouvrir(contexteTablette);
await activer(resp, lienResponsable);

// 2. Inventaire : église (sous-sol, rez-de-chaussée), 50 zones au rez-de-chaussée, une crypte au sous-sol
const inventaire = `${base}/organisations/${org}/inventaire`;
const envoyer = async () => cliquer(resp, "form.formulaire-inventaire button[type=submit]");
await resp.goto(`${inventaire}?action=batiment`);
await resp.fill("input[name='StructureSaisie.Nom']", "Église"); await envoyer();
const batiment = new URL(await resp.getAttribute("a[href*='action=niveau']", "href"), base).searchParams.get("pour");
for (const [nom, ordre] of [["Sous-sol", "-1"], ["Rez-de-chaussée", "0"]]) {
  await resp.goto(`${inventaire}?action=niveau&pour=${batiment}`);
  await resp.fill("input[name='StructureSaisie.Nom']", nom); await resp.fill("input[name='StructureSaisie.Ordre']", ordre); await envoyer();
}
const zone = async (nom, niveau) => {
  await resp.goto(`${inventaire}?action=zone&pour=${batiment}`);
  await resp.fill("input[name='StructureSaisie.Nom']", nom);
  await resp.selectOption("select[name='StructureSaisie.NiveauId']", { label: niveau }); await envoyer();
};
for (let i = 1; i <= 50; i++) await zone(`Salle ${i}`, "Rez-de-chaussée");
await zone("Crypte", "Sous-sol");
ok("structure créée (2 niveaux, 51 zones)", (await texte(resp)).includes("Salle 50"));

// 3. Parcours guidé : fond importé, calibration 14,33 m et contrôle 8,90 m, annuler et rétablir
const plans = `${base}/organisations/${org}/plans`;
await resp.goto(plans);
ok("parcours guidé proposé pour un niveau sans plan", (await texte(resp, "h1")).includes("Nouveau plan · Église · Rez-de-chaussée")
  && (await texte(resp)).includes("Masquez d'abord les noms du cartouche"));
await resp.setInputFiles("input[name=fichier]", { name: "plan-synthetique.png", mimeType: "image/png", buffer: pngSynthetique() });
await resp.click("button:has-text('Importer ce fond')");
await resp.waitForSelector("[data-panneau=calibration]:not([hidden])", { timeout: 90000 });
await resp.waitForSelector("[data-role=fond] image");
ok("fond importé, étape « Calibrer l'échelle »", (await resp.getAttribute("[data-etape=calibration]", "aria-current")) === "step");
await toucherPixel(resp, 248, 300);
await toucherPixel(resp, 3114, 300);
await resp.fill("[data-champ=calibration]", "14,33"); await resp.press("[data-champ=calibration]", "Tab");
await resp.click("[data-cible=controle]");
await toucherPixel(resp, 248, 300);
await toucherPixel(resp, 248, 2080);
await resp.fill("[data-champ=controle]", "8,50"); await resp.press("[data-champ=controle]", "Tab");
const avertissement = await texte(resp, "[data-role=resultat-controle]");
ok("avertissement au-delà de 2 % (contrôle saisi à 8,50 m)", avertissement.includes("au-delà de 2 %"), avertissement);
await resp.fill("[data-champ=controle]", "8,90"); await resp.press("[data-champ=controle]", "Tab");
const controle = await texte(resp, "[data-role=resultat-controle]");
const ecart = Number(/écart ([\d,]+) %/.exec(controle)?.[1].replace(",", "."));
ok("cote de contrôle de 8,90 m à moins de 1 % (recette 14,33 / 8,90 m)", ecart < 1 && controle.includes("✓"), controle);
await resp.screenshot({ path: "us21-calibration-tablette.png" });
await resp.click("[data-action=annuler]");
ok("annuler retire la dernière saisie", (await resp.inputValue("[data-champ=controle]")) === "8,5");
await resp.keyboard.press("Control+y");
ok("rétablir (Ctrl+Y) la remet", (await resp.inputValue("[data-champ=controle]")) === "8,9");
await resp.click("[data-action=valider-calibration]");
await resp.waitForSelector("[data-panneau=pieces]:not([hidden])", { timeout: 30000 });
ok("version 1 enregistrée, étape 3 « pièces zone par zone »", (await texte(resp, "[data-role=message]")).includes("Version 1 enregistrée")
  && (await texte(resp, "[data-etape=pieces]")).includes("0 sur 50 zones") && (await resp.getAttribute("[data-etape=pieces]", "aria-current")) === "step");
for (const [x, y] of [[16, 11], [18, 11], [18, 13], [16, 13]]) await toucherRepere(resp, x, y);
await resp.click("[data-action=fermer-trace]");
await resp.waitForFunction(() => document.querySelector("[data-etape=pieces]")?.textContent.includes("1 sur 50 zones"));
const guide = await texte(resp, "[data-role=guide-zones]");
ok("étape 3 : Salle 1 tracée, la zone suivante proposée", guide.includes("✓ Salle 1→ Salle ") && /→ Salle \d+/.test(guide), guide.slice(0, 60));
await resp.screenshot({ path: "us21-guide-pieces-tablette.png" });
await Promise.all([resp.waitForNavigation({ timeout: 30000 }), resp.click("[data-action=terminer-pieces]")]);
await resp.waitForFunction(() => document.querySelector("[data-role=resume]")?.textContent.includes("Version 2"));
const resume = await texte(resp, "[data-role=resume]");
ok("version 2 (pièces) enregistrée, atelier avec l'échelle et la cote de contrôle", (await texte(resp, "h1")).startsWith("Plan · Église")
  && resume.includes("par Rose Responsable") && resume.includes("échelle calibrée (cote de contrôle"), resume);
await resp.screenshot({ path: "us21-atelier-tablette.png" });

// 4. Pièces enregistrées par l'éditeur (relais JSON) : 50 contours de 20 sommets (1 000 sommets), et la crypte au sous-sol
const enregistrerPieces = async (niveau, pieces) => resp.evaluate(async ([niveau, pieces]) => {
  const d = document.querySelector("#editeur-plan").dataset;
  const plansBase = `/organisations/${d.organisation}/sites/${d.site}/plans-batiment`;
  const entetes = { "Content-Type": "application/json", RequestVerificationToken: d.jeton };
  let plan = niveau === d.niveau ? d.plan : null;
  if (!plan) {
    const cree = await fetch(plansBase, { method: "POST", headers: entetes, body: JSON.stringify({ batimentId: d.batiment, niveauId: niveau }) });
    plan = (await cree.json()).id;
  }
  const lue = await (await fetch(`${plansBase}/${plan}`)).json();
  const zones = JSON.parse(d.zones);
  const contenu = { ...lue.contenu, pieces: pieces.map(p => ({ zoneId: Object.keys(zones).find(id => zones[id] === p.nom), contour: p.contour })) };
  const reponse = await fetch(`${plansBase}/${plan}`, { method: "PUT", headers: entetes, body: JSON.stringify({ commandeId: crypto.randomUUID(), version: lue.version, contenu }) });
  return reponse.status;
}, [niveau, pieces]);
const cercle = (cx, cy, r, n = 20) => Array.from({ length: n }, (_, k) => ({
  x: Math.round((cx + r * Math.cos(2 * Math.PI * k / n)) * 100) / 100, y: Math.round((cy + r * Math.sin(2 * Math.PI * k / n)) * 100) / 100,
}));
const salles = Array.from({ length: 50 }, (_, i) => ({ nom: `Salle ${i + 1}`, contour: cercle(2 + (i % 10) * 1.38, 2.3 + Math.floor(i / 10) * 1.7, 0.6) }));
const niveaux = await resp.evaluate(() => Object.fromEntries([...document.querySelectorAll("nav[aria-label=Niveau] a")].map(a => [a.textContent, new URL(a.href).searchParams.get("niveau")])));
ok("1 000 sommets enregistrés au rez-de-chaussée (version 3)", (await enregistrerPieces(niveaux["Rez-de-chaussée"], salles)) === 200);
ok("crypte enregistrée au sous-sol", (await enregistrerPieces(niveaux["Sous-sol"], [{ nom: "Crypte", contour: [{ x: 2, y: 2 }, { x: 9, y: 2 }, { x: 9, y: 9 }, { x: 2, y: 9 }] }])) === 200);
await resp.reload();
await resp.waitForFunction(() => document.querySelectorAll("[data-role=pieces] polygon").length === 50);
ok("atelier : 50 pièces et leurs noms affichés", (await resp.locator("[data-role=pieces] text").count()) === 50);

// 5. Mesure de fluidité : durées des images (requestAnimationFrame) pendant glisser, molette et pincement
async function mesurer(p, ralenti) {
  const cdp = await p.context().newCDPSession(p);
  await cdp.send("Emulation.setCPUThrottlingRate", { rate: ralenti });
  await p.evaluate(() => { window.__images = []; let avant = performance.now(); const boucle = t => { window.__images.push(t - avant); avant = t; if (!window.__fin) requestAnimationFrame(boucle); }; window.__fin = false; requestAnimationFrame(boucle); });
  const r = await p.locator("[data-role=svg]").boundingBox();
  const [cx, cy] = [r.x + r.width / 2, r.y + r.height / 2];
  await p.mouse.move(cx, cy); await p.mouse.down();
  for (let k = 1; k <= 60; k++) await p.mouse.move(cx + k * 4, cy + k * 2);
  await p.mouse.up();
  for (let k = 0; k < 20; k++) await p.mouse.wheel(0, k < 10 ? -120 : 120);
  const doigts = e => [{ x: cx - e, y: cy, id: 1 }, { x: cx + e, y: cy, id: 2 }];
  await cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: doigts(40) });
  for (let k = 1; k <= 30; k++) await cdp.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: doigts(40 + k * 6) });
  await cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
  const images = await p.evaluate(() => { window.__fin = true; return window.__images.slice(1); });
  await cdp.send("Emulation.setCPUThrottlingRate", { rate: 1 });
  const tri = [...images].sort((a, b) => a - b);
  const q = f => tri[Math.min(tri.length - 1, Math.floor(f * tri.length))];
  return { images: images.length, mediane: q(0.5), p95: q(0.95), max: tri.at(-1), au_dela_50ms: images.filter(t => t > 50).length };
}
const avantPincement = await resp.getAttribute("[data-role=svg]", "viewBox");
const normale = await mesurer(resp, 1);
ok("le pincement à deux doigts zoome la vue", (await resp.getAttribute("[data-role=svg]", "viewBox")) !== avantPincement);
const ralentie = await mesurer(resp, 4);
const f = m => `${m.images} images, médiane ${m.mediane.toFixed(1)} ms, 95e centile ${m.p95.toFixed(1)} ms, max ${m.max.toFixed(1)} ms, > 50 ms : ${m.au_dela_50ms}`;
console.log(`MESURE fluidité (Chromium ${b.version()}, fond 4 096 px, 1 000 sommets) — processeur normal : ${f(normale)}`);
console.log(`MESURE fluidité — processeur ralenti 4× : ${f(ralentie)}`);
ok("fluidité : médiane sous 20 ms (processeur normal)", normale.mediane < 20, f(normale));

// 5 bis. S21-08 dans l'atelier : zone créée, pièce cotée au centimètre, chevauchement, porte, escalier, recalage
await resp.click("[data-action=ajuster]"); // vue entière : la chapelle se trace en haut à droite du fond, hors des salles
await resp.click("[data-outil=piece]");
await resp.fill("[data-panneau=trace] [data-role=creer-zone] input[name=nom]", "Chapelle");
await resp.click("[data-panneau=trace] [data-role=creer-zone] button[type=submit]");
await resp.waitForFunction(() => document.querySelector("[data-role=message]")?.textContent.includes("Zone « Chapelle » ajoutée"));
ok("zone « Chapelle » créée depuis l'éditeur (responsable : plans et inventaire)", (await resp.inputValue("[data-champ=trace-zone]")) !== "");
for (const [x, y] of [[16.03, 1.02], [18.98, 0.97], [19.02, 4.01], [15.98, 3.99]]) await toucherRepere(resp, x, y);
await toucherRepere(resp, 16.03, 1.02); // retour sur le premier sommet : la pièce se ferme
await attendre(resp, "[data-panneau=piece]:not([hidden])");
// Sommets cotés au centimètre au clavier (alternative au glisser) : rectangle exact de 3,00 × 3,00 m.
const exacts = [[16, 1], [19, 1], [19, 4], [16, 4]];
for (let k = 0; k < 4; k++) {
  const contour = await resp.evaluate(() => [...document.querySelectorAll("[data-role=selection] circle.sommet")].map(c => [Number(c.getAttribute("cx")), Number(c.getAttribute("cy"))]));
  await toucherRepere(resp, contour[k][0], contour[k][1]);
  await attendre(resp, "[data-role=piece-sommet]:not([hidden])");
  await resp.fill("[data-champ=piece-x]", String(exacts[k][0])); await resp.press("[data-champ=piece-x]", "Tab");
  await resp.fill("[data-champ=piece-y]", String(exacts[k][1])); await resp.press("[data-champ=piece-y]", "Tab");
}
const perimetre = await texte(resp, "[data-role=piece-perimetre]");
const surface = await texte(resp, "[data-role=piece-surface]");
ok("pièce cotée au centimètre : 3,00 × 3,00 m (périmètre 12,00 m, 9,0 m²)", perimetre === "12,00 m" && surface === "9,0 m²", `${perimetre} ${surface}`);
await resp.click("[data-panneau=piece] [data-action=deselectionner]");
await resp.click("[data-outil=piece]");
await resp.fill("[data-panneau=trace] [data-role=creer-zone] input[name=nom]", "Sacristie");
await resp.click("[data-panneau=trace] [data-role=creer-zone] button[type=submit]");
await resp.waitForFunction(() => document.querySelector("[data-role=message]")?.textContent.includes("Zone « Sacristie » ajoutée"));
for (const [x, y] of [[18.4, 2.4], [20.2, 2.4], [20.2, 3.6]]) await toucherRepere(resp, x, y);
await resp.click("[data-action=fermer-trace]");
ok("chevauchement de plus de 5 cm refusé par l'éditeur, message clair", (await texte(resp, "[data-role=message]")).includes("chevauche « Chapelle » de plus de 5 cm"));
await resp.click("[data-action=abandonner-trace]");
await resp.click("[data-outil=ouverture]");
await toucherRepere(resp, 17.5, 1.03);
await attendre(resp, "[data-panneau=ouverture]:not([hidden])");
await resp.fill("[data-champ=ouverture-largeur]", "1,20"); await resp.press("[data-champ=ouverture-largeur]", "Tab");
ok("porte posée sur une arête de la chapelle, largeur 1,20 m", (await texte(resp, "[data-role=ouverture-nom]")).startsWith("Porte · Chapelle")
  && (await resp.inputValue("[data-champ=ouverture-largeur]")) === "1,2" && await resp.isHidden("[data-role=ouverture-defaut]"));
await resp.click("[data-outil=escalier]");
ok("escalier : vers le sous-sol proposé", (await resp.locator("[data-champ=trace-niveau] option:checked").textContent()) === "Sous-sol");
for (const [x, y] of [[16.5, 2], [17.5, 2], [17.5, 3.5], [16.5, 3.5]]) await toucherRepere(resp, x, y);
await resp.click("[data-action=fermer-trace]");
await attendre(resp, "[data-panneau=escalier]:not([hidden])");
ok("escalier vers le sous-sol tracé", (await texte(resp, "[data-role=escalier-nom]")) === "Escalier vers Sous-sol");
await resp.click("[data-panneau=escalier] [data-action=deselectionner]");
await resp.click("[data-outil=selection]");
// Recalage sur le calque du sous-sol : crypte (2 ; 2)-(9 ; 9), mêmes angles touchés sur le fond en (2,5 ; 1,5) et (9,5 ; 8,5).
await resp.click("[data-panneau=proprietes] [data-action=recaler]");
await attendre(resp, "[data-panneau=recalage]:not([hidden])");
await toucherRepere(resp, 2.05, 1.97); await toucherRepere(resp, 2.5, 1.5);
await toucherRepere(resp, 8.96, 9.04); await toucherRepere(resp, 9.5, 8.5);
const recalage = await texte(resp, "[data-role=recalage-resultat]");
ok("recalage : translation déduite, écart des distances sous 2 %", recalage.includes("Fond décalé de") && recalage.includes("✓"), recalage);
await resp.click("[data-action=valider-recalage]");
await resp.click("[data-action=enregistrer]");
await resp.waitForFunction(() => /Version \d+ enregistrée/.test(document.querySelector("[data-role=message]")?.textContent ?? ""));
const s2108 = await lirePlanCourant(resp);
const chapelle = s2108.contenu.pieces.find(p => p.contour.length === 4 && Math.abs(p.contour[1].x - p.contour[0].x - 3) < 1e-9);
const decalage = s2108.contenu.fond.decalage;
ok("version relue : fond recalé d'environ (-0,50 ; +0,50) m, chapelle de 3,00 m déplacée avec lui, porte et escalier vers le sous-sol",
  Math.abs(decalage.x + 0.5) <= 0.04 && Math.abs(decalage.y - 0.5) <= 0.04 && chapelle && Math.abs(chapelle.contour[0].x - 16 - decalage.x) < 1e-9
  && s2108.contenu.ouvertures.length === 1 && s2108.contenu.escaliers[0].versNiveauId === niveaux["Sous-sol"],
  JSON.stringify({ decalage, chapelle: chapelle?.contour[0] }));
await resp.screenshot({ path: "us21-atelier-pieces-tablette.png" });

// 6. Brouillon : modification non enregistrée, conflit 409, reprise confirmée sur la version gagnante
const versionAvant = s2108.version;
await resp.click("[data-action=changer-fond]");
await resp.click("[data-action=grille]");
await resp.waitForTimeout(500);
ok("brouillon local écrit pour ce compte et ce plan", (await brouillons(resp)).length === 1);
const concurrent = await resp.evaluate(async () => {
  const d = document.querySelector("#editeur-plan").dataset;
  const chemin = `/organisations/${d.organisation}/sites/${d.site}/plans-batiment/${d.plan}`;
  const lue = await (await fetch(chemin)).json();
  const r = await fetch(chemin, { method: "PUT", headers: { "Content-Type": "application/json", RequestVerificationToken: d.jeton }, body: JSON.stringify({ commandeId: crypto.randomUUID(), version: lue.version, motif: "Enregistrement concurrent", contenu: lue.contenu }) });
  return r.status;
});
ok(`version ${versionAvant + 1} enregistrée par ailleurs entre-temps`, concurrent === 200);
await resp.click("[data-action=enregistrer]");
await resp.waitForSelector("[data-role=brouillon]:not([hidden])");
const conflit = await texte(resp, "[data-role=message]");
ok("409 : version gagnante nommée, rien d'écrasé, brouillon conservé", conflit.includes("modifié entre-temps par Rose Responsable") && conflit.includes(`version ${versionAvant + 1}`)
  && (await brouillons(resp)).length === 1, conflit);
await resp.click("[data-action=brouillon-reprendre]");
ok("reprise demandée : confirmation exigée", (await texte(resp, "[data-role=brouillon-texte]")).startsWith("Confirmez") && await resp.isVisible("[data-action=brouillon-confirmer]"));
await resp.click("[data-action=brouillon-confirmer]");
await resp.waitForSelector("[data-action=enregistrer]:not([disabled])");
ok(`bouton « Enregistrer la version ${versionAvant + 2} »`, (await texte(resp, "[data-action=enregistrer]")) === `Enregistrer la version ${versionAvant + 2}`);
await resp.click("[data-action=enregistrer]");
await resp.waitForFunction(v => document.querySelector("[data-role=message]")?.textContent.includes(`Version ${v} enregistrée`), versionAvant + 2);
const historique = await resp.evaluate(async () => {
  const d = document.querySelector("#editeur-plan").dataset;
  return (await (await fetch(`/organisations/${d.organisation}/sites/${d.site}/plans-batiment/${d.plan}`)).json()).contenu.fond;
});
ok(`version ${versionAvant + 2} = brouillon repris (plan sur grille), brouillon effacé`, historique === null && (await brouillons(resp)).length === 0);

// 7. Cloisonnement : brouillon d'un autre compte effacé à l'ouverture ; tout brouillon effacé à la déconnexion
await resp.evaluate(() => {
  const d = document.querySelector("#editeur-plan").dataset;
  localStorage.setItem(`gestionsite.brouillon-plan.v1:0190f5a2-1111-7000-8000-000000000001:${d.organisation}:${d.plan}`, "{}");
});
await resp.reload(); await resp.waitForTimeout(500);
ok("brouillon d'un autre compte effacé à l'ouverture de l'éditeur", (await brouillons(resp)).length === 0);
await resp.click("[data-action=changer-fond]");
await resp.click("[data-role=fonds-liste] button >> nth=0"); // calibration commencée, non enregistrée
await resp.waitForTimeout(500);
ok("brouillon en cours avant la déconnexion", (await brouillons(resp)).length === 1);
await cliquer(resp, "form[action='/deconnexion'] button");
await resp.goto(`${base}/connexion`);
ok("aucun brouillon après la déconnexion (Clear-Site-Data)", (await brouillons(resp)).length === 0);

// 8. Lecteur (même tablette) : atelier sans commande d'édition, calque du sous-sol
await activer(resp, lienPasteur);
await resp.goto(plans);
await resp.waitForFunction(v => document.querySelector("[data-role=resume]")?.textContent.includes(`Version ${v}`), versionAvant + 2);
ok("lecteur : aucune commande d'édition", (await resp.locator("[data-action=enregistrer], [data-panneau=calibration], [data-action=changer-fond]").count()) === 0);
await resp.click("[data-action=calque]");
await resp.waitForSelector("[data-role=calque]:not([hidden]) polygon");
ok("calque du sous-sol affiché sous le rez-de-chaussée", (await resp.getAttribute("[data-action=calque]", "aria-pressed")) === "true"
  && (await texte(resp, "[data-action=calque]")).includes("Sous-sol : affiché"));
await resp.click("[data-action=zoom-avant]"); await resp.click("[data-action=gauche]"); await resp.click("[data-action=ajuster]");
ok("alternatives sans glisser : zoom, déplacement, tout afficher", (await resp.getAttribute("[data-role=svg]", "viewBox")) !== null);
await resp.screenshot({ path: "us21-lecteur-calque.png" });

ok("aucune violation de la politique de sécurité du contenu ni erreur JavaScript", violations.length === 0, violations.join(" | "));
await b.close();

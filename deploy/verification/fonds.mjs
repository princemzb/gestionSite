// Vérification des fonds de plan en préproduction (S21-05 import PDF, SOC-04 image de 49 Mpx, SOC-05 rendu PDF dans un
// processus isolé ; navigateur réel, Playwright 1.55.0 ; recette visible avec RECETTE=1) : une responsable importe la
// page 2 d'un PDF synthétique de 3 pages avec une échelle déclarée de 1:100 depuis le parcours guidé de l'éditeur (rendu
// par le lanceur setuid et l'enfant sous seccomp, mesure de la durée), puis, par le relais de l'éditeur : page 4 refusée
// « page_absente », PDF tronqué refusé « pdf_illisible », PNG RVB de 7 000 × 7 000 px (49 Mpx) accepté et réduit à
// 4 096 px pour l'affichage ; aucune violation de la politique de sécurité du contenu.
// Usage : deploy/verification/lancer.sh fonds (ou VERIF_URL=… VERIF_ORG=… VERIF_JETON=… node fonds.mjs).
import { deflateSync } from "node:zlib";
import { preparer } from "./commun.mjs";

const { r, ok, base, org, jeton, page, activer, inviter, finir } = await preparer("fonds");

// PDF synthétique de trois pages A4 paysage : un cadre et un trait différents par page (aucune donnée réelle).
function pdfSynthetique() {
  const objets = [];
  const ajouter = texte => { objets.push(texte); return objets.length; };
  ajouter("<< /Type /Catalog /Pages 2 0 R >>");
  ajouter(""); // Pages, rempli après les pages
  const pages = [];
  for (let n = 1; n <= 3; n++) {
    const dessin = `0 0 0 RG ${2 + n} w 40 40 762 515 re S ${100 * n} 100 m ${100 * n + 300} 450 l S`;
    const contenu = ajouter(`<< /Length ${dessin.length} >>\nstream\n${dessin}\nendstream`);
    pages.push(ajouter(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Contents ${contenu} 0 R >>`));
  }
  objets[1] = `<< /Type /Pages /Kids [${pages.map(p => `${p} 0 R`).join(" ")}] /Count ${pages.length} >>`;
  let sortie = "%PDF-1.4\n";
  const positions = objets.map((corps, i) => { const position = sortie.length; sortie += `${i + 1} 0 obj\n${corps}\nendobj\n`; return position; });
  const xref = sortie.length;
  sortie += `xref\n0 ${objets.length + 1}\n0000000000 65535 f \n${positions.map(p => `${String(p).padStart(10, "0")} 00000 n \n`).join("")}`;
  sortie += `trailer\n<< /Size ${objets.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(sortie, "latin1");
}

// PNG RVB (sans transparence) de 7 000 × 7 000 px, fond blanc et un cadre : le cas qui dépassait la mémoire avant SOC-04.
function pngRvb(cote) {
  const ligne = 1 + cote * 3;
  const brut = Buffer.alloc(ligne * cote, 255);
  for (let y = 0; y < cote; y++) {
    brut[y * ligne] = 0;
    if (y < 8 || y >= cote - 8) brut.fill(30, y * ligne + 1, (y + 1) * ligne);
    else { brut.fill(30, y * ligne + 1, y * ligne + 25); brut.fill(30, (y + 1) * ligne - 24, (y + 1) * ligne); }
  }
  const crc = tampon => { let c = ~0; for (const o of tampon) { c ^= o; for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xEDB88320 & -(c & 1)); } return ~c >>> 0; };
  const bloc = (type, donnees) => {
    const t = Buffer.concat([Buffer.from(type), donnees]);
    const taille = Buffer.alloc(4); taille.writeUInt32BE(donnees.length);
    const somme = Buffer.alloc(4); somme.writeUInt32BE(crc(t));
    return Buffer.concat([taille, t, somme]);
  };
  const entete = Buffer.alloc(13); entete.writeUInt32BE(cote, 0); entete.writeUInt32BE(cote, 4); entete[8] = 8; entete[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), bloc("IHDR", entete), bloc("IDAT", deflateSync(brut)), bloc("IEND", Buffer.alloc(0))]);
}

// Envoi d'un fond par le relais de l'éditeur, depuis la page (session, jeton anti-falsification de la page).
const envoyerFond = (p, nom, type, contenu, champs = {}) => p.evaluate(async ([nom, type, b64, champs]) => {
  const d = document.querySelector("#editeur-plan").dataset;
  const octets = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
  const envoi = new FormData();
  envoi.append("commandeId", crypto.randomUUID());
  envoi.append("fichier", new Blob([octets], { type }), nom);
  for (const [cle, valeur] of Object.entries(champs)) envoi.append(cle, valeur);
  const debut = performance.now();
  const reponse = await fetch(`/organisations/${d.organisation}/sites/${d.site}/plans-batiment/${d.plan}/fonds`,
    { method: "POST", body: envoi, headers: { RequestVerificationToken: d.jeton, Accept: "application/json" }, credentials: "same-origin" });
  return { statut: reponse.status, corps: await reponse.text(), duree: Math.round(performance.now() - debut) };
}, [nom, type, contenu.toString("base64"), champs]);

const listeFonds = p => p.evaluate(async () => {
  const d = document.querySelector("#editeur-plan").dataset;
  return (await fetch(`/organisations/${d.organisation}/sites/${d.site}/plans-batiment/${d.plan}/fonds`, { headers: { Accept: "application/json" } })).json();
});

// 1. Administrateur : invitation d'une responsable
await r.etape("1. Administrateur : invitation d'une responsable");
const admin = await page("Administrateur");
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
const { lien } = await inviter(admin, "Rose Responsable", "Responsable");
await ok("invitation créée", lien.includes("/activation?jeton="));
const resp = await page("Responsable (tablette)", 1280, 800, { hasTouch: true });
resp.on("pageerror", e => console.error(`erreur JavaScript : ${e.message}`));
await activer(resp, lien);

// 2. Inventaire : un bâtiment et un niveau
await r.etape("2. Inventaire : un bâtiment et un niveau");
const inventaire = `${base}/organisations/${org}/inventaire`;
const envoyer = async () => { await Promise.all([resp.waitForNavigation(), resp.click("form.formulaire-inventaire button[type=submit]")]); };
await resp.goto(`${inventaire}?action=batiment`);
await resp.fill("input[name='StructureSaisie.Nom']", "Église"); await envoyer();
const batiment = new URL(await resp.getAttribute("a[href*='action=niveau']", "href"), base).searchParams.get("pour");
await resp.goto(`${inventaire}?action=niveau&pour=${batiment}`);
await resp.fill("input[name='StructureSaisie.Nom']", "Rez-de-chaussée"); await resp.fill("input[name='StructureSaisie.Ordre']", "0"); await envoyer();
await ok("bâtiment et niveau créés", (await resp.textContent("main"))?.includes("Rez-de-chaussée") ?? false);

// 3. Parcours guidé : import de la page 2 d'un PDF de 3 pages, échelle déclarée 1:100 (rendu isolé, SOC-05)
await r.etape("3. Parcours guidé : page 2 d'un PDF de 3 pages, échelle 1:100");
const plans = `${base}/organisations/${org}/plans`;
await resp.goto(plans);
await ok("parcours guidé proposé", (await resp.textContent("h1"))?.includes("Nouveau plan") ?? false);
await resp.setInputFiles("input[name=fichier]", { name: "plan-synthetique.pdf", mimeType: "application/pdf", buffer: pdfSynthetique() });
await resp.fill("input[name=page]", "2");
await resp.fill("input[name=echelle]", "100");
const debut = Date.now();
await resp.click("button:has-text('Importer ce fond')");
await resp.waitForSelector("[data-panneau=calibration]:not([hidden]), [data-role=message].danger", { timeout: 120000 });
const dureePdf = Math.round((Date.now() - debut) / 100) / 10;
const calibration = await resp.isVisible("[data-panneau=calibration]:not([hidden])");
await ok("PDF rendu par le processus isolé, étape « Calibrer l'échelle »", calibration && await resp.isVisible("[data-role=fond] image"),
  `${dureePdf} s ; ${(await resp.textContent("[data-role=message]")) ?? ""}`);
await resp.reload(); await resp.waitForSelector("#editeur-plan[data-plan]:not([data-plan=''])", { timeout: 30000 });
const fonds = await listeFonds(resp);
const pdf = fonds.find(f => f.pdf)?.pdf;
await ok("fond PDF enregistré : page 2 sur 3, échelle 1:100", pdf?.page === 2 && pdf?.pages === 3 && pdf?.echelle === 100, JSON.stringify(pdf ?? fonds));

// 4. Refus : page absente, PDF tronqué
await r.etape("4. Refus : page absente, PDF tronqué");
const absente = await envoyerFond(resp, "plan.pdf", "application/pdf", pdfSynthetique(), { page: "4" });
await ok("page 4 d'un PDF de 3 pages refusée (page_absente)", absente.statut === 422 && absente.corps.includes("page_absente"), `${absente.statut} ${absente.corps.slice(0, 200)}`);
const tronque = pdfSynthetique().subarray(0, 300);
const illisible = await envoyerFond(resp, "plan.pdf", "application/pdf", tronque);
await ok("PDF tronqué refusé (pdf_illisible), application toujours disponible", illisible.statut === 422 && illisible.corps.includes("pdf_illisible"), `${illisible.statut} ${illisible.corps.slice(0, 200)}`);

// 5. Image RVB de 49 Mpx (SOC-04) : acceptée et réduite pour l'affichage
await r.etape("5. Image RVB de 7 000 × 7 000 px (49 Mpx)");
const grande = await envoyerFond(resp, "plan-49mpx.png", "image/png", pngRvb(7000));
let affichage = {};
try { affichage = JSON.parse(grande.corps); } catch { /* corps lu ci-dessous dans le détail */ }
await ok("image de 49 Mpx acceptée et réduite à 4 096 px", grande.statut === 200 && Math.max(affichage.largeurAffichage ?? 0, affichage.hauteurAffichage ?? 0) <= 4096
  && (affichage.largeurAffichage ?? 0) > 0, `${grande.statut} en ${grande.duree} ms ; ${grande.corps.slice(0, 200)}`);
const sante = await (await fetch(`${base}/sante`)).text();
await ok("application disponible après les imports (/sante)", sante.includes("Disponible"), sante.slice(0, 80));

console.log(`MESURE | rendu de la page PDF (bout en bout, parcours guidé) : ${dureePdf} s ; image de 49 Mpx : ${grande.duree} ms`);
await finir();

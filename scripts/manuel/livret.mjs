// Livret PDF A4 du manuel d'utilisation (docs/manuel) : contrôle des chapitres, assemblage HTML, schémas Mermaid,
// impression par Chromium (Playwright), puis seconde impression avec les numéros de page du sommaire.
// Usage (dans scripts/manuel, après « npm ci » et « npx playwright install chromium ») :
//   node livret.mjs              → sortie/manuel-gestionsite.pdf
//   node livret.mjs --verifier   → contrôle seul (ancres, images, liens), sans navigateur
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { assembler, ordonner, pagesDesAncres, remplirPages, verifierManuel } from "./assemblage.mjs";

const ici = dirname(fileURLToPath(import.meta.url));
const manuel = join(ici, "..", "..", "docs", "manuel");
const sortie = join(ici, "sortie");
const chapitres = ordonner(readdirSync(manuel)).map(fichier => ({ fichier, markdown: readFileSync(join(manuel, fichier), "utf8") }));
const erreurs = verifierManuel(chapitres, readdirSync(join(manuel, "images")).filter(n => n.endsWith(".png")));
if (erreurs.length > 0) {
  erreurs.forEach(e => console.error(`ERREUR ${e}`));
  process.exit(1);
}
console.log(`Manuel cohérent : ${chapitres.length} chapitres.`);
if (process.argv.includes("--verifier")) process.exit(0);

const { chromium } = await import("playwright");
mkdirSync(sortie, { recursive: true });
// Date d'édition du manuel (présentation « Édition du … »), la même que celle affichée par l'aide intégrée (DOC-02) ;
// à défaut, le jour de l'impression.
const date = /Édition du ([^.\n]+)\./.exec(chapitres[0]?.markdown ?? "")?.[1]
  ?? new Date().toLocaleDateString("fr-FR", { day: "numeric", month: "long", year: "numeric", timeZone: "Europe/Paris" });
const html = assembler({
  chapitres,
  css: readFileSync(join(ici, "livret.css"), "utf8"),
  edition: `Édition du ${date}. Images prises sur une organisation fictive ; personnes, adresses et numéros inventés.`,
  prefixeImages: `${relative(sortie, manuel)}/`,
  scripts: ["../node_modules/mermaid/dist/mermaid.min.js"],
});
const navigateur = await chromium.launch();
const page = await navigateur.newPage();
const imprimer = async contenu => {
  const fichier = join(sortie, "livret.html");
  writeFileSync(fichier, contenu);
  await page.goto(pathToFileURL(fichier).href, { waitUntil: "networkidle" });
  await page.evaluate(async () => {
    await document.fonts.ready; // polices chargées avant que Mermaid ne mesure les libellés des schémas
    window.mermaid.initialize({ startOnLoad: false, theme: "base", themeVariables: {
      fontFamily: "Inter, sans-serif", primaryColor: "#EEF3FF", primaryBorderColor: "#005DC4", primaryTextColor: "#000740", lineColor: "#3D4654" } });
    await window.mermaid.run();
  });
  return page.pdf({
    format: "A4", printBackground: true, preferCSSPageSize: true, displayHeaderFooter: true, outline: true,
    headerTemplate: "<span></span>",
    footerTemplate: `<div style="width:100%;margin:0 16mm;font:8px Helvetica,Arial,sans-serif;color:#6B7588;display:flex;justify-content:space-between">
      <span>gestionSite — Manuel d'utilisation</span><span>page <span class="pageNumber"></span> / <span class="totalPages"></span></span></div>`,
  });
};
let pdf;
try {
  // Seconde impression avec les numéros de page lus dans la première (mêmes polices, même mise en page).
  pdf = await imprimer(remplirPages(html, await pagesDesAncres(await imprimer(html))));
} finally {
  await navigateur.close();
}
const cible = join(sortie, "manuel-gestionsite.pdf");
writeFileSync(cible, pdf);
console.log(`Livret : ${relative(process.cwd(), cible)} (${Math.round(pdf.length / 1024)} Ko).`);

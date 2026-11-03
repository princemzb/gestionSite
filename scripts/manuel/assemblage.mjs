// Assemblage du manuel d'utilisation (docs/manuel) : contrôle des chapitres et rendu HTML du livret.
// Fonctions pures, sans accès au disque ni au navigateur : livret.mjs les appelle et imprime le PDF.
import { Marked } from "marked";
import { PDFArray, PDFDict, PDFDocument, PDFName, PDFRef, PDFString, PDFHexString } from "pdf-lib";

/** Identifiant d'ancre en fin de titre : « ## Qualifier un dossier {#dossiers-qualifier} ». */
export const MOTIF_ANCRE = /\{#([^}\s]*)\}$/;
const ANCRE_VALIDE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const CHAPITRE = /^\d{2}-[a-z0-9-]+\.md$/;
const EXTERNE = /^(?:https?:|mailto:)/;

/** Sépare l'ancre finale d'un titre : « Qualifier {#dossiers-qualifier} » → texte « Qualifier », ancre « dossiers-qualifier ». */
export function separerAncre(texte) {
  const nu = texte.trimEnd();
  const ancre = MOTIF_ANCRE.exec(nu);
  return ancre ? { texte: nu.slice(0, ancre.index).trimEnd(), ancre: ancre[1] } : { texte: nu, ancre: null };
}

/** Cibles des liens et images Markdown « [texte](cible) » et « ![légende](cible) », lues ligne par ligne sans expression rationnelle. */
export function cibles(markdown) {
  const resultat = [];
  for (const ligne of markdown.split("\n")) {
    let debut = 0; // un « [ » ne sert qu'au lien qui le suit, jamais à un « ]( » situé après un autre lien
    let milieu = ligne.indexOf("](");
    while (milieu !== -1) {
      const fin = ligne.indexOf(")", milieu + 2);
      if (fin === -1) break;
      const crochet = ligne.lastIndexOf("[", milieu);
      const cible = ligne.slice(milieu + 2, fin);
      if (crochet >= debut && cible !== "" && !/\s/.test(cible)) resultat.push({ image: ligne[crochet - 1] === "!", cible });
      debut = fin + 1;
      milieu = ligne.indexOf("](", fin);
    }
  }
  return resultat;
}

/** Chapitres du manuel (« 00-sommaire.md », « 01-principes.md »…), dans l'ordre de leur numéro. */
export function ordonner(fichiers) {
  return fichiers.filter(f => CHAPITRE.test(f)).sort((a, b) => a.localeCompare(b, "fr"));
}

/** Texte Markdown dont les lignes des blocs de code (« ``` ») sont vidées : numéros de ligne conservés. */
export function horsCode(markdown) {
  let code = false;
  return markdown.split("\n").map(ligne => {
    const cloture = ligne.trimStart().startsWith("```");
    const masquee = code || cloture;
    if (cloture) code = !code;
    return masquee ? "" : ligne;
  }).join("\n");
}

/** Titres Markdown (hors blocs de code) avec leur niveau, leur texte et leur ancre éventuelle. */
export function titres(markdown) {
  const resultat = [];
  horsCode(markdown).split("\n").forEach((ligne, index) => {
    const titre = /^(#{1,6})[ \t]/.exec(ligne);
    if (titre) {
      const { texte, ancre } = separerAncre(ligne.slice(titre[1].length).trim());
      resultat.push({ niveau: titre[1].length, texte, ancre, ligne: index + 1 });
    }
  });
  return resultat;
}

/**
 * Contrôle du manuel : chaque titre de niveau 1 à 3 porte une ancre stable ; toute ancre (niveaux 1 à 6) est valide et
 * unique dans tout le manuel ;
 * chaque chapitre commence par un titre de niveau 1 ; images et liens internes existent ; aucune image n'est orpheline.
 * @param {{ fichier: string, markdown: string }[]} chapitres
 * @param {Iterable<string>} images noms des fichiers du dossier images/
 * @returns {string[]} erreurs, vide si le manuel est cohérent
 */
export function verifierManuel(chapitres, images) {
  const erreurs = [];
  const presentes = new Set(images);
  const utilisees = new Set();
  const ancres = new Map(); // ancre → fichier
  const parFichier = new Map(chapitres.map(c => [c.fichier, new Set()]));

  for (const { fichier, markdown } of chapitres) {
    const liste = titres(markdown);
    if (liste[0]?.niveau !== 1) erreurs.push(`${fichier} : le chapitre doit commencer par un titre de niveau 1.`);
    for (const t of liste.filter(t => t.niveau <= 3 || t.ancre !== null)) {
      if (t.ancre === null) {
        erreurs.push(`${fichier}:${t.ligne} : titre sans ancre « ${t.texte} » (ajoutez {#identifiant}).`);
      } else if (!ANCRE_VALIDE.test(t.ancre)) {
        erreurs.push(`${fichier}:${t.ligne} : ancre « ${t.ancre} » invalide (minuscules, chiffres et tirets).`);
      } else if (ancres.has(t.ancre)) {
        erreurs.push(`${fichier}:${t.ligne} : ancre « ${t.ancre} » déjà utilisée dans ${ancres.get(t.ancre)}.`);
      } else {
        ancres.set(t.ancre, fichier);
        parFichier.get(fichier).add(t.ancre);
      }
    }
  }

  for (const { fichier, markdown: source } of chapitres) {
    const markdown = horsCode(source); // un exemple ou un schéma n'est ni une image ni un lien du manuel
    for (const { image, cible } of cibles(markdown)) {
      if (image) {
        const nom = cible.startsWith("images/") ? cible.slice("images/".length) : null;
        if (nom === null || !presentes.has(nom)) erreurs.push(`${fichier} : image introuvable « ${cible} » (attendue dans images/).`);
        else utilisees.add(nom);
        continue;
      }
      if (EXTERNE.test(cible)) continue;
      const [chemin, ancre] = cible.split("#");
      const destination = chemin === "" ? fichier : chemin;
      if (!parFichier.has(destination)) erreurs.push(`${fichier} : lien vers un chapitre inconnu « ${cible} ».`);
      else if (ancre !== undefined && !parFichier.get(destination).has(ancre)) erreurs.push(`${fichier} : ancre inconnue « ${cible} ».`);
    }
  }

  for (const nom of [...presentes].filter(n => !utilisees.has(n)).sort((a, b) => a.localeCompare(b, "fr"))) erreurs.push(`images/${nom} : image utilisée par aucun chapitre.`);
  return erreurs;
}

const echapper = texte => texte.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");

/**
 * Rend un chapitre en HTML : titres avec leur identifiant, liens entre chapitres ramenés à des ancres du livret,
 * images préfixées (captures de téléphone repérées par « -tel »), schémas Mermaid laissés au navigateur.
 * @param {string} markdown
 * @param {{ prefixeImages: string, ancresChapitres: Map<string, string> }} options
 */
export function rendreChapitre(markdown, { prefixeImages, ancresChapitres }) {
  const marked = new Marked({
    walkTokens(jeton) {
      if (jeton.type === "heading") {
        const { ancre } = separerAncre(jeton.text);
        const dernier = jeton.tokens.at(-1);
        if (ancre && dernier?.type === "text") {
          jeton.ancre = ancre;
          dernier.text = separerAncre(dernier.text).texte;
        }
      } else if (jeton.type === "link" && !EXTERNE.test(jeton.href)) {
        const [chemin, ancre] = jeton.href.split("#");
        jeton.href = `#${ancre ?? ancresChapitres.get(chemin) ?? ""}`;
      } else if (jeton.type === "image") {
        jeton.href = prefixeImages + jeton.href;
      }
    },
    renderer: {
      heading({ tokens, depth, ancre }) {
        const id = ancre ? ` id="${ancre}"` : "";
        return `<h${depth}${id}>${this.parser.parseInline(tokens)}</h${depth}>\n`;
      },
      code({ text, lang }) {
        return lang === "mermaid" ? `<pre class="mermaid">${echapper(text)}</pre>\n` : false;
      },
      image({ href, text }) {
        const classe = /-tel\.png$/.test(href) ? "capture telephone" : "capture";
        return `<span class="figure"><img class="${classe}" src="${echapper(href)}" alt="${echapper(text)}"><span class="legende">${echapper(text)}</span></span>`;
      },
    },
  });
  return marked.parse(markdown);
}

/** Sommaire du livret : chapitres et sections (niveaux 1 et 2), chaque entrée avec une place pour son numéro de page. */
export function sommaire(chapitres) {
  const enLigne = new Marked(); // même rendu que les titres des chapitres (gras, code…)
  const entrees = chapitres.flatMap(({ markdown }) => titres(markdown).filter(t => t.niveau <= 2 && t.ancre))
    .map(t => `<li class="niveau-${t.niveau}"><a href="#${t.ancre}"><span class="intitule">${enLigne.parseInline(t.texte)}</span>`
      + `<span class="page" data-ancre="${t.ancre}"></span></a></li>`);
  return `<nav class="sommaire" aria-label="Sommaire"><h1 id="livret-sommaire">Sommaire</h1><ol>\n${entrees.join("\n")}\n</ol></nav>`;
}

/**
 * Document HTML complet du livret : couverture, sommaire, chapitres (le sommaire Markdown « 00 » sert d'introduction).
 * @param {{ chapitres: { fichier: string, markdown: string }[], css: string, edition: string, prefixeImages: string, scripts?: string[] }} options
 */
export function assembler({ chapitres, css, edition, prefixeImages, scripts = [] }) {
  const ancresChapitres = new Map(chapitres.map(c => [c.fichier, titres(c.markdown).find(t => t.ancre)?.ancre ?? ""]));
  const corps = chapitres.map(c => `<section class="chapitre" data-fichier="${c.fichier}">\n${rendreChapitre(c.markdown, { prefixeImages, ancresChapitres })}</section>`);
  const balises = scripts.map(s => `<script src="${echapper(s)}"></script>`).join("\n");
  return `<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<title>gestionSite — Manuel d'utilisation</title>
<style>${css}</style>
${balises}
</head>
<body>
<header class="couverture">
  <div class="couverture-bandeau">
    <span class="marque">gestionSite</span>
    <h1 class="couverture-titre">Manuel d'utilisation</h1>
    <p>Le patrimoine et l'entretien de vos lieux, au même endroit.</p>
  </div>
  <p class="couverture-edition">${echapper(edition)}</p>
</header>
${sommaire(chapitres)}
${corps.join("\n")}
</body>
</html>
`;
}

/** Inscrit les numéros de page dans le sommaire (ancre → page, à partir de 1) ; une ancre sans page reste vide. */
export function remplirPages(html, pages) {
  return html.replace(/<span class="page" data-ancre="([^"]+)"><\/span>/g,
    (balise, ancre) => (pages.has(ancre) ? `<span class="page" data-ancre="${ancre}">${pages.get(ancre)}</span>` : balise));
}

/**
 * Page (à partir de 1) de chaque destination nommée d'un PDF : Chromium en crée une par identifiant ciblé par un lien
 * interne. Lit le dictionnaire /Dests du catalogue ou l'arbre de noms /Names /Dests.
 * @param {Uint8Array} octets
 * @returns {Promise<Map<string, number>>}
 */
export async function pagesDesAncres(octets) {
  const pdf = await PDFDocument.load(octets, { updateMetadata: false });
  const contexte = pdf.context;
  const numeros = new Map(pdf.getPages().map((page, index) => [page.ref.toString(), index + 1]));
  const resultat = new Map();
  const lire = objet => (objet instanceof PDFRef ? contexte.lookup(objet) : objet);
  const texte = nom => (nom instanceof PDFName ? nom.decodeText() : nom instanceof PDFString || nom instanceof PDFHexString ? nom.decodeText() : String(nom));
  const noter = (nom, destination) => {
    let cible = lire(destination);
    if (cible instanceof PDFDict) cible = lire(cible.get(PDFName.of("D")));
    const page = cible instanceof PDFArray ? cible.get(0) : null;
    if (page instanceof PDFRef && numeros.has(page.toString())) resultat.set(texte(nom), numeros.get(page.toString()));
  };
  const parcourir = noeud => {
    const dict = lire(noeud);
    if (!(dict instanceof PDFDict)) return;
    const noms = lire(dict.get(PDFName.of("Names")));
    if (noms instanceof PDFArray) for (let i = 0; i + 1 < noms.size(); i += 2) noter(lire(noms.get(i)), noms.get(i + 1));
    const enfants = lire(dict.get(PDFName.of("Kids")));
    if (enfants instanceof PDFArray) for (let i = 0; i < enfants.size(); i++) parcourir(enfants.get(i));
  };

  const dests = lire(pdf.catalog.get(PDFName.of("Dests")));
  if (dests instanceof PDFDict) for (const [nom, destination] of dests.entries()) noter(nom, destination);
  const names = lire(pdf.catalog.get(PDFName.of("Names")));
  if (names instanceof PDFDict) parcourir(names.get(PDFName.of("Dests")));
  return resultat;
}

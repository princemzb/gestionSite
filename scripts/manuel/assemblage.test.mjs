// Tests de l'assemblage du manuel (node:test) : contrôle des ancres, images et liens, rendu HTML, sommaire et
// numéros de page lus dans un PDF. Le dernier test contrôle le vrai manuel de docs/manuel.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { PDFDocument, PDFName, PDFString } from "pdf-lib";
import { assembler, ordonner, pagesDesAncres, remplirPages, rendreChapitre, sommaire, titres, verifierManuel } from "./assemblage.mjs";

const chapitre = (fichier, markdown) => ({ fichier, markdown });

test("ordonner garde les chapitres numérotés, dans l'ordre", () => {
  assert.deepEqual(ordonner(["10-b.md", "README.md", "02-a.md", "images", "01-x.txt"]), ["02-a.md", "10-b.md"]);
});

test("titres lit niveau, texte et ancre, hors blocs de code", () => {
  const md = "# Titre {#t}\n\n```mermaid\n# pas un titre\n```\n## Sans ancre\n### Avec **gras** {#t-gras}";
  assert.deepEqual(titres(md), [
    { niveau: 1, texte: "Titre", ancre: "t", ligne: 1 },
    { niveau: 2, texte: "Sans ancre", ancre: null, ligne: 6 },
    { niveau: 3, texte: "Avec **gras**", ancre: "t-gras", ligne: 7 },
  ]);
});

test("un manuel cohérent ne donne aucune erreur", () => {
  const chapitres = [
    chapitre("01-a.md", "# A {#a}\n\n![Écran](images/a.png)\n\n[vers B](02-b.md#b-section), [ici](#a), [site](https://exemple.org)\n#### Détail sans ancre"),
    chapitre("02-b.md", "# B {#b}\n\n## Section {#b-section}\n\n[chapitre A](01-a.md)"),
  ];
  assert.deepEqual(verifierManuel(chapitres, ["a.png"]), []);
});

test("verifierManuel signale ancres absentes, invalides ou en double", () => {
  const erreurs = verifierManuel([
    chapitre("01-a.md", "Texte avant le titre\n# A {#a}\n## Sans ancre\n### Mauvaise {#Mauvaise_Ancre}"),
    chapitre("02-b.md", "# B {#a}"),
  ], []);
  assert.deepEqual(erreurs, [
    "01-a.md:3 : titre sans ancre « Sans ancre » (ajoutez {#identifiant}).",
    "01-a.md:4 : ancre « Mauvaise_Ancre » invalide (minuscules, chiffres et tirets).",
    "02-b.md:1 : ancre « a » déjà utilisée dans 01-a.md.",
  ]);
  assert.deepEqual(verifierManuel([chapitre("01-a.md", "## Pas de niveau 1 {#x}")], []),
    ["01-a.md : le chapitre doit commencer par un titre de niveau 1."]);
});

test("verifierManuel signale images et liens cassés, et images orphelines", () => {
  const erreurs = verifierManuel([
    chapitre("01-a.md", "# A {#a}\n![x](images/absente.png) ![y](ailleurs/b.png)\n[z](03-inconnu.md) [w](01-a.md#inconnue) [v](#a)"),
  ], ["orpheline.png"]);
  assert.deepEqual(erreurs, [
    "01-a.md : image introuvable « images/absente.png » (attendue dans images/).",
    "01-a.md : image introuvable « ailleurs/b.png » (attendue dans images/).",
    "01-a.md : lien vers un chapitre inconnu « 03-inconnu.md ».",
    "01-a.md : ancre inconnue « 01-a.md#inconnue ».",
    "images/orpheline.png : image utilisée par aucun chapitre.",
  ]);
});

test("rendreChapitre : identifiants, liens, captures et schémas", () => {
  const html = rendreChapitre([
    "# Chapitre **un** {#un}",
    "## Sans ancre",
    "[deux](02-deux.md) [section](02-deux.md#deux-s) [ici](#un) [web](https://exemple.org) [inconnu](09-x.md)",
    "![Écran <ordinateur>](images/ecran.png) ![Téléphone](images/liste-tel.png)",
    "```mermaid\nflowchart TD\n  A --> B\n```",
    "```\ncode ordinaire\n```",
  ].join("\n\n"), { prefixeImages: "../docs/", ancresChapitres: new Map([["02-deux.md", "deux"]]) });
  assert.match(html, /<h1 id="un">Chapitre <strong>un<\/strong><\/h1>/);
  assert.match(html, /<h2>Sans ancre<\/h2>/);
  assert.match(html, /href="#deux">deux/);
  assert.match(html, /href="#deux-s">section/);
  assert.match(html, /href="#un">ici/);
  assert.match(html, /href="https:\/\/exemple.org"/);
  assert.match(html, /href="#">inconnu/);
  assert.match(html, /<img class="capture" src="..\/docs\/images\/ecran.png" alt="Écran &lt;ordinateur&gt;">/);
  assert.match(html, /<img class="capture telephone" src="..\/docs\/images\/liste-tel.png"/);
  assert.match(html, /<pre class="mermaid">flowchart TD\n {2}A --&gt; B<\/pre>/);
  assert.match(html, /<pre><code>code ordinaire/);
});

test("sommaire et assemblage du livret", () => {
  const chapitres = [chapitre("01-a.md", "# A & co {#a}\n## Section {#a-s}\n### Détail {#a-d}"), chapitre("02-b.md", "Sans titre")];
  const nav = sommaire(chapitres);
  assert.match(nav, /<li class="niveau-1"><a href="#a"><span class="intitule">A &amp; co<\/span><span class="page" data-ancre="a"><\/span>/);
  assert.match(nav, /<li class="niveau-2"><a href="#a-s">/);
  assert.doesNotMatch(nav, /a-d/);
  const html = assembler({ chapitres, css: "body{}", edition: "Édition <test>", prefixeImages: "", scripts: ["m.js"] });
  assert.match(html, /^<!doctype html>/);
  assert.match(html, /<style>body\{\}<\/style>/);
  assert.match(html, /<script src="m.js"><\/script>/);
  assert.match(html, /Édition &lt;test&gt;/);
  assert.match(html, /<section class="chapitre" data-fichier="02-b.md">/);
  assert.match(assembler({ chapitres, css: "", edition: "", prefixeImages: "" }), /<\/style>\n\n<\/head>/);
});

test("remplirPages inscrit les numéros connus", () => {
  const html = '<span class="page" data-ancre="a"></span><span class="page" data-ancre="b"></span>';
  assert.equal(remplirPages(html, new Map([["a", 3]])), '<span class="page" data-ancre="a">3</span><span class="page" data-ancre="b"></span>');
});

test("pagesDesAncres lit le dictionnaire /Dests et l'arbre de noms", async () => {
  const pdf = await PDFDocument.create();
  const [p1, p2, p3] = [pdf.addPage(), pdf.addPage(), pdf.addPage()];
  const contexte = pdf.context;
  const dest = page => contexte.obj([page.ref, PDFName.of("XYZ"), 0, 0, 0]);
  pdf.catalog.set(PDFName.of("Dests"), contexte.obj({ premiere: dest(p1), horsPage: contexte.obj([1, PDFName.of("Fit")]) }));
  const feuille = contexte.register(contexte.obj({ Names: [PDFString.of("deuxieme"), contexte.register(dest(p2)), PDFString.of("troisieme"), contexte.obj({ D: dest(p3) })] }));
  pdf.catalog.set(PDFName.of("Names"), contexte.obj({ Dests: contexte.obj({ Kids: [feuille, contexte.obj(5)] }) }));
  const pages = await pagesDesAncres(await pdf.save());
  assert.deepEqual([...pages].sort(), [["deuxieme", 2], ["premiere", 1], ["troisieme", 3]]);
  const vide = await PDFDocument.create();
  vide.addPage();
  assert.equal((await pagesDesAncres(await vide.save())).size, 0);
});

test("le manuel de docs/manuel est cohérent (ancres, images, liens)", () => {
  const manuel = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "docs", "manuel");
  const chapitres = ordonner(readdirSync(manuel)).map(fichier => chapitre(fichier, readFileSync(join(manuel, fichier), "utf8")));
  assert.ok(chapitres.length >= 13);
  assert.deepEqual(verifierManuel(chapitres, readdirSync(join(manuel, "images"))), []);
});

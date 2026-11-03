// Vérification de la préproduction pour DOC-02, aide intégrée (navigateur réel, Playwright 1.55.0 ; recette visible avec
// RECETTE=1) : « ? Aide sur cet écran » sur deux écrans (Membres, puis Dossiers sur tablette où le panneau monte du bas),
// ouverture au clavier, fermeture par Échap avec retour du focus, page Aide par rôle, recherche, chapitre, livret PDF,
// « Dépôts reçus » au menu des internes ; aucune violation de la politique de sécurité du contenu.
// Usage : deploy/verification/lancer.sh doc02 (ou VERIF_URL=<adresse publique> VERIF_ORG=<organisation>
// VERIF_JETON=<jeton d'activation> node doc02.mjs ; captures dans le dossier courant, ou dans le rapport en recette) ;
// organisation fictive ouverte par « ouvrir-organisation --site » ; jeton jamais passé en argument ni écrit.
import { preparer } from "./commun.mjs";

const { r, ok, base, org, jeton, page, activer, inviter, finir } = await preparer("doc02");
const panneauOuvert = p => p.evaluate(() => document.getElementById("aide-panneau")?.matches(":popover-open") ?? false);
// Fin de l'animation d'ouverture (glissement du panneau) avant de mesurer sa position.
const pose = p => p.waitForFunction(() => document.getElementById("aide-panneau").getAnimations().length === 0);

// 1. Administrateur : aide de l'écran Membres, ouverte et fermée au clavier
await r.etape("1. Administrateur : aide de l'écran Membres, au clavier");
const admin = await page("Administrateur");
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
await admin.goto(`${base}/organisations/${org}/membres`);
await admin.focus("button.bouton-aide");
await admin.keyboard.press("Enter");
await ok("Membres : le panneau s'ouvre au clavier sur la rubrique « Voir qui fait quoi »",
  await panneauOuvert(admin) && (await admin.textContent("#aide-panneau-titre")).includes("Voir qui fait quoi"));
await ok("le focus entre dans le panneau (bouton Fermer)", await admin.evaluate(() => document.activeElement?.classList.contains("aide-fermer")));
await pose(admin);
const boite = await admin.locator("#aide-panneau").boundingBox();
await ok("ordinateur : panneau latéral à droite, sur toute la hauteur", boite.x + boite.width >= 1439 && boite.height >= 899 && boite.width <= 421);
await admin.screenshot({ path: "doc02-membres-aide.png" });
await admin.keyboard.press("Escape");
await ok("Échap ferme le panneau et rend le focus au bouton",
  !(await panneauOuvert(admin)) && await admin.evaluate(() => document.activeElement?.classList.contains("bouton-aide")));

// 2. Page Aide : rôle de la personne en premier, recherche, chapitre, livret
await r.etape("2. Page Aide : rôle en premier, recherche, chapitre, livret");
await Promise.all([admin.waitForNavigation(), admin.click("nav.admin-nav a:has-text('Aide')")]);
await ok("page Aide : tâches de l'administrateur en premier", (await admin.textContent(".aide-role.sien")).includes("Administrateur"));
await admin.screenshot({ path: "doc02-page-aide.png", fullPage: true });
await admin.fill("#aide-q", "clôturer"); await Promise.all([admin.waitForNavigation(), admin.press("#aide-q", "Enter")]);
await ok("recherche « clôturer » : rubrique « Clôturer une intervention »", (await admin.textContent(".aide-resultats")).includes("Clôturer une intervention"));
await Promise.all([admin.waitForNavigation(), admin.click(".aide-chapitres a:has-text('Dossiers')")]);
const captures = await admin.evaluate(() => [...document.images].map(i => i.src)); // chargement différé : lues une à une
const servies = await Promise.all(captures.map(async src => (await admin.request.get(src)).headers()["content-type"] === "image/png"));
await ok(`chapitre Dossiers lu en entier, ${captures.length} captures servies`,
  await admin.locator("#dossiers-qualifier").count() === 1 && captures.length > 5 && servies.every(Boolean));
const livret = await admin.request.get(`${base}/aide/livret.pdf`);
await ok("livret PDF téléchargeable", livret.ok() && livret.headers()["content-type"] === "application/pdf"
  && (await livret.body()).subarray(0, 5).toString() === "%PDF-");

// 3. Technicien sur tablette : « Dépôts reçus » au menu, aide de l'écran Dossiers qui monte du bas
await r.etape("3. Technicien sur tablette : Dépôts reçus au menu, aide des Dossiers");
const { lien } = await inviter(admin, "Marc Technicien", "Technicien");
const tablette = await page("Technicien (tablette)", 800, 1280);
await activer(tablette, lien);
await tablette.goto(`${base}/organisations/${org}/dossiers`);
await ok("« Dépôts reçus » au menu du technicien", await tablette.locator("nav.admin-nav a:has-text('Dépôts reçus')").count() === 1);
await tablette.click("button.bouton-aide");
await pose(tablette);
const bas = await tablette.locator("#aide-panneau").boundingBox();
await ok("tablette : le panneau monte du bas, sur la largeur", await panneauOuvert(tablette) && Math.abs(bas.y + bas.height - 1280) <= 1 && bas.width >= 799);
await ok("rubrique « Suivre les dossiers d'un site »", (await tablette.textContent("#aide-panneau-titre")).includes("Suivre les dossiers"));
await tablette.screenshot({ path: "doc02-dossiers-aide-tablette.png" });
await tablette.click("button.aide-fermer");
await ok("le bouton Fermer ferme le panneau", !(await panneauOuvert(tablette)));

await finir();

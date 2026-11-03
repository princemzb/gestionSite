// Vérification de la préproduction pour US-12 (navigateur réel, Playwright 1.55.0 ; recette visible avec RECETTE=1) :
// un responsable confirme un plan proposé (inspection du tableau électrique) et l'affecte au technicien ; le technicien
// exécute le contrôle sur téléphone pas à pas (maquette A) avec un défaut et un danger : un dossier « À qualifier » est
// créé ; le responsable saisit l'échéance suivante avec le même défaut : le constat rejoint le même dossier (journal
// « Constat d'inspection rattaché ») ; historique et fiche du contrôle ; captures Jour, Nuit et téléphone ; aucune
// violation de la politique de sécurité du contenu. Jamais de mention « conforme » sur l'écran de contrôle.
// Usage : deploy/verification/lancer.sh us12 (ou VERIF_URL=<adresse publique> VERIF_ORG=<organisation>
// VERIF_JETON=<jeton d'activation> node us12.mjs ; captures dans le dossier courant, ou dans le rapport en recette) ;
// organisation fictive ouverte par « ouvrir-organisation --site » ; jeton jamais passé en argument ni écrit.
import { preparer } from "./commun.mjs";

const { r, ok, base, org, jeton, page, activer, inviter, finir } = await preparer("us12");
const cliquer = async (p, selecteur) => { await Promise.all([p.waitForNavigation(), p.click(selecteur)]); };
const texte = async (p, selecteur = "main") => (await p.textContent(selecteur)) ?? "";

// 1. Administrateur : activation, invitation d'un technicien (téléphone) et d'une responsable (planification)
await r.etape("1. Administrateur : activation, invitation d'un technicien (téléphone) et d'une responsable (planification)");
const admin = await page("Administrateur");
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
const lienTechnicien = (await inviter(admin, "Marc Technicien", "Technicien")).lien;
const lienResponsable = (await inviter(admin, "Rose Responsable", "Responsable")).lien;
await ok("invitations créées (technicien, responsable)", lienTechnicien.startsWith(`${base}/activation?jeton=`) && lienResponsable.startsWith(`${base}/activation?jeton=`));
const tel = await page("Technicien (téléphone)", 390, 844);
await activer(tel, lienTechnicien);
const resp = await page("Responsable");
await activer(resp, lienResponsable);

// 2. Inventaire : bâtiment et tableau divisionnaire (soumis à vérification, proposé pour l'électricité)
await r.etape("2. Inventaire : bâtiment et tableau divisionnaire (soumis à vérification, proposé pour l'électricité)");
const inventaire = `${base}/organisations/${org}/inventaire`;
const envoyer = async () => cliquer(tel, "form.formulaire-inventaire button[type=submit]");
await tel.goto(`${inventaire}?action=batiment`);
await tel.fill("input[name='StructureSaisie.Nom']", "Église"); await envoyer();
await cliquer(tel, ".arbre a:has-text('Parties communes')");
await cliquer(tel, "a:has-text('Ajouter un composant')");
await tel.fill("input[name='ComposantSaisi.Nom']", "Tableau divisionnaire");
await tel.selectOption("select[name='ComposantSaisi.CorpsEtat']", "CourantsForts"); await envoyer();
await ok("tableau divisionnaire créé", (await texte(tel, ".fiche-composant")).includes("Tableau divisionnaire"));

// 3. Responsable : plan proposé confirmé, échéance affectée au technicien
await r.etape("3. Responsable : plan proposé confirmé, échéance affectée au technicien");
const preventif = `${base}/organisations/${org}/preventif`;
await resp.goto(`${preventif}/propositions`);
await ok("plan d'inspection proposé, « à confirmer »", (await texte(resp)).includes("Inspection avec la grille du corps d'état") && (await texte(resp)).includes("à confirmer"));
await cliquer(resp, "form:has(input[name='cle'][value^='inspection-interne:']) button[type=submit]");
await ok("proposition confirmée", resp.url().endsWith("issue=ok") && !(await texte(resp)).includes("Inspection avec la grille du corps d'état"));
await resp.goto(preventif);
const fichePlan = new URL(await resp.getAttribute(".panneau-echeances a:has-text('Inspection avec la grille')", "href"), base).toString();
await resp.goto(fichePlan);
await resp.selectOption("select[name='Affectation.IntervenantCompteId']", { label: "Marc Technicien" });
await cliquer(resp, "button:has-text('Affecter')");
await ok("échéance affectée au technicien", (await texte(resp)).includes("Affectée à Marc Technicien"));

// 4. Technicien sur téléphone : « Mes contrôles », contrôle pas à pas avec un défaut et un danger, photo (état mauvais)
await r.etape("4. Technicien sur téléphone : « Mes contrôles », contrôle pas à pas avec un défaut et un danger, photo (état mauvais)");
await tel.goto(preventif);
await ok("« Mes contrôles » du technicien", (await texte(tel)).includes("Mes contrôles · 1"));
await cliquer(tel, ".panneau-echeances a:has-text('Contrôler')");
const etape1 = await texte(tel);
await ok("étape 1 : points de la grille, jamais « conforme »", etape1.includes("Contrôler · étape 1 sur 3") && etape1.includes("Pas de trace d'échauffement") && !etape1.includes("onforme"));
await tel.check("input[name='Saisie.Constats[2]'][value='danger']");
await tel.fill("input[name='Saisie.Mesures[0]']", "48,5");
await tel.screenshot({ path: "us12-controle-etape1-tel.png", fullPage: true });
await cliquer(tel, "button[value='suivant']");
await tel.check("input[name='Saisie.Etat'][value='Mauvais']");
await cliquer(tel, "button[value='suivant']");
const etape3 = await texte(tel);
await ok("étape 3 : photo exigée, « sans observation » exclu", etape3.includes("obligatoire : état mauvais ou critique") && await tel.locator("input[name='Saisie.Resultat'][value='Conforme']").count() === 0);
await tel.check("input[name='Saisie.Resultat'][value='AvecObservations']");
await tel.setInputFiles("input[name='photos']", { name: "constat.png", mimeType: "image/png", buffer: await tel.screenshot() });
await tel.fill("textarea[name='Saisie.CompteRendu']", "Bornier noirci au départ « Sono »");
await tel.screenshot({ path: "us12-controle-etape3-tel.png", fullPage: true });
await cliquer(tel, "button[value='enregistrer']");
const fiche1 = await texte(tel);
await ok("contrôle enregistré : évaluation 3,0 Mauvais, un dossier ouvert", fiche1.includes("Contrôle enregistré ; évaluation : 3,0 · Mauvais ; 1 dossier ouvert."));
await ok("défaut avec danger et mesure affichés", fiche1.includes("Défaut, danger signalé") && fiche1.includes("48,5 °C"));
const photo = tel.locator(".fiche-controle .photos img").first();
await photo.scrollIntoViewIfNeeded();
await tel.waitForFunction(i => i.complete && i.naturalWidth > 0, await photo.elementHandle(), { timeout: 30000 });
const relais = await tel.request.get(new URL(await photo.getAttribute("src"), base).toString());
await ok("photo du contrôle affichée (relais JPEG)", relais.status() === 200 && relais.headers()["content-type"] === "image/jpeg");
await tel.screenshot({ path: "us12-fiche-controle-tel.png", fullPage: true });
const dossier = new URL(await tel.getAttribute("aside a[href*='/dossiers/']", "href"), base).toString();
await resp.goto(dossier);
await ok("dossier créé « À qualifier », origine inspection", (await texte(resp)).includes("À qualifier") && (await texte(resp)).includes("Pas de trace d'échauffement"));

// 5. Responsable : échéance suivante, même défaut → le constat rejoint le dossier ouvert
await r.etape("5. Responsable : échéance suivante, même défaut → le constat rejoint le dossier ouvert");
await resp.goto(fichePlan);
await cliquer(resp, "a:has-text('Faire le contrôle')");
await resp.check("input[name='Saisie.Constats[2]'][value='non']");
await cliquer(resp, "button[value='suivant']");
await resp.check("input[name='Saisie.Etat'][value='Degrade']");
await cliquer(resp, "button[value='suivant']");
await resp.check("input[name='Saisie.Resultat'][value='AvecObservations']");
await cliquer(resp, "button[value='enregistrer']");
const fiche2 = await texte(resp);
await ok("deuxième constat rattaché au dossier ouvert", fiche2.includes("1 constat rattaché à un dossier existant") && fiche2.includes("rattaché au dossier déjà ouvert"));
await ok("même dossier", new URL(await resp.getAttribute("aside a[href*='/dossiers/']", "href"), base).toString() === dossier);
await resp.goto(`${dossier}?onglet=historique`);
await ok("journal du dossier : constat d'inspection rattaché", (await texte(resp)).includes("Constat d'inspection rattaché"));

// 6. Historique des contrôles ; captures Jour et Nuit
await r.etape("6. Historique des contrôles ; captures Jour et Nuit");
await resp.goto(`${preventif}/controles`);
const historique = await texte(resp);
await ok("historique : deux contrôles avec observations", (historique.match(/Avec observations/g) ?? []).length === 2);
await resp.screenshot({ path: "us12-historique-jour.png", fullPage: true });
await resp.goto(fichePlan);
await resp.screenshot({ path: "us12-fiche-plan-jour.png", fullPage: true });
await resp.evaluate(() => document.documentElement.setAttribute("data-theme", "nuit"));
await resp.screenshot({ path: "us12-fiche-plan-nuit.png", fullPage: true });
await finir();

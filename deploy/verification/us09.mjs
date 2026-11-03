// Vérification de la préproduction pour US-09 (navigateur réel, Playwright 1.55.0 ; recette visible avec RECETTE=1) :
// déclaration interne, qualification, affectation, « Mes interventions » sur téléphone, démarrage, clôture, archives ;
// captures Jour et Nuit ; aucune violation de la politique de sécurité du contenu.
// Usage : deploy/verification/lancer.sh us09 (ou VERIF_URL=<adresse publique> VERIF_ORG=<organisation>
// VERIF_JETON=<jeton d'activation> node us09.mjs ; captures dans le dossier courant, ou dans le rapport en recette) ;
// organisation fictive ouverte par « ouvrir-organisation --site » ; jeton jamais passé en argument ni écrit.
import { preparer } from "./commun.mjs";

const { r, ok, base, org, jeton, page, activer, inviter, finir } = await preparer("us09");
const envoyer = async (p, formulaire) => { await Promise.all([p.waitForNavigation(), p.click(`${formulaire} button[type=submit]`)]); };

// 1. Administrateur : activation, invitation d'une responsable et d'un technicien
await r.etape("1. Administrateur : activation, invitation d'une responsable et d'un technicien");
const admin = await page("Administrateur");
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
const lienResponsable = (await inviter(admin, "Rose Responsable", "Responsable")).lien;
const lienTechnicien = (await inviter(admin, "Marc Technicien", "Technicien")).lien;
await ok("invitations créées (responsable, technicien)", lienResponsable.startsWith(`${base}/activation?jeton=`) && lienTechnicien.startsWith(`${base}/activation?jeton=`));

// Le technicien active son compte d'abord : seul un membre actif peut recevoir une intervention.
const tech = await page("Technicien (téléphone)", 390, 844);
await activer(tech, lienTechnicien);

// 2. Responsable : un bâtiment, puis une déclaration interne
await r.etape("2. Responsable : un bâtiment, puis une déclaration interne");
const resp = await page("Responsable");
await activer(resp, lienResponsable);
await ok("la responsable arrive sur les dossiers", resp.url().includes(`/organisations/${org}/dossiers`));
await resp.goto(`${base}/organisations/${org}/inventaire?action=batiment`);
await resp.fill("input[name='StructureSaisie.Nom']", "Église"); await envoyer(resp, "form.formulaire-inventaire");
await resp.goto(`${base}/organisations/${org}/dossiers`);
await resp.click("a:has-text('Déclarer un problème')"); await resp.waitForLoadState();
await resp.selectOption("select[name='Saisie.ZoneId']", { index: 1 });
await resp.fill("textarea[name='Saisie.Description']", "Fuite au plafond de la sacristie");
await resp.check("input[name='Saisie.ActiviteBloquee']");
await envoyer(resp, "form.formulaire-inventaire");
await ok("dossier créé « À qualifier »", (await resp.textContent("main")).includes("Fuite au plafond de la sacristie") && await resp.locator("li[aria-current='step']:has-text('Déclaré')").count() === 1);
const fiche = resp.url().split("&")[0];

// 3. Qualification puis affectation au technicien
await r.etape("3. Qualification puis affectation au technicien");
await resp.check("input[name='Qualification.Priorite'][value='Haute']");
await envoyer(resp, "form:has(button:has-text('Enregistrer la qualification'))");
await ok("dossier qualifié (étape « Qualifié »)", await resp.locator("li[aria-current='step']:has-text('Qualifié')").count() === 1);
// US-10 Q7-A : le délai de prise en charge court dès la qualification, avant toute affectation.
await ok("échéance de prise en charge affichée dès la qualification", (await resp.textContent(".delai-dossier")).includes("Prise en charge"));
await resp.goto(`${fiche}&onglet=interventions`);
await resp.fill("input[name='Affectation.Titre']", "Reprendre le solin");
await resp.selectOption("select[name='Affectation.IntervenantCompteId']", { label: "Marc Technicien" });
await envoyer(resp, "form:has(button:has-text('Affecter'))");
await ok("intervention affectée, échéance de prise en charge affichée", (await resp.textContent("main")).includes("Reprendre le solin") && (await resp.textContent(".delai-dossier")).includes("Prise en charge"));

// 4. Technicien sur téléphone : Mes interventions, démarrage, clôture
await r.etape("4. Technicien sur téléphone : Mes interventions, démarrage, clôture");
await tech.goto(`${base}/organisations/${org}/interventions`);
await ok("« Mes interventions » sur téléphone", (await tech.textContent(".liste-interventions")).includes("Reprendre le solin"));
await tech.screenshot({ path: "us09-mes-interventions-tel.png", fullPage: true });
await tech.click(".ligne-intervention"); await tech.waitForLoadState();
await Promise.all([tech.waitForNavigation(), tech.click("button:has-text('Démarrer')")]);
await ok("intervention démarrée", (await tech.textContent(".grille-interventions")).includes("En cours"));
await tech.click("a:has-text('Clôturer')"); await tech.waitForLoadState();
await tech.fill("textarea[name='Cloture.Actions']", "Solin refait au mortier, tuiles reposées");
await tech.check("input[name='Cloture.EtatFinal'][value='Resolu']");
await envoyer(tech, "form:has(button:has-text('Clôturer'))");
await ok("dossier clôturé avec sa dernière intervention", await tech.locator("li[aria-current='step']:has-text('Clôturé')").count() === 1);

// 5. Responsable : suivi en colonnes, archives, captures Jour et Nuit
await r.etape("5. Responsable : suivi en colonnes, archives, captures Jour et Nuit");
await resp.goto(`${base}/organisations/${org}/dossiers?archives=true`);
await ok("dossier dans les archives", (await resp.textContent("main")).includes("Fuite au plafond de la sacristie"));
await resp.goto(`${base}/organisations/${org}/dossiers`);
await resp.screenshot({ path: "us09-suivi-jour.png", fullPage: true });
await resp.goto(`${fiche}&onglet=interventions`);
await resp.screenshot({ path: "us09-dossier-jour.png", fullPage: true });
await resp.evaluate(() => document.documentElement.setAttribute("data-theme", "nuit"));
await resp.screenshot({ path: "us09-dossier-nuit.png", fullPage: true });
await finir();

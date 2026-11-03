// Vérification de la préproduction pour US-07 (navigateur réel, Playwright 1.55.0 ; recette visible avec RECETTE=1) :
// invitation d'un technicien, écran Inventaire (bâtiment, niveau, zone, lot réparti par état, composant individuel),
// filtres, historique, thèmes Jour et Nuit, aucune violation de la politique de sécurité du contenu.
// Usage : deploy/verification/lancer.sh us07 (ou VERIF_URL=<adresse publique> VERIF_ORG=<organisation>
// VERIF_JETON=<jeton d'activation> node us07.mjs ; captures dans le dossier courant, ou dans le rapport en recette) ;
// organisation fictive ouverte par « ouvrir-organisation --site » ; jeton jamais passé en argument ni écrit.
import { randomBytes } from "node:crypto";
import { recette } from "./recette.mjs";
const { VERIF_URL: base, VERIF_ORG: org, VERIF_JETON: jeton } = process.env;
if (!base || !org || !jeton) { console.error("VERIF_URL, VERIF_ORG et VERIF_JETON sont requis."); process.exit(2); }
const mdp = randomBytes(12).toString("hex") + "-verif";
const r = recette("us07");
const ok = r.ok;
await r.lancer();

// 1. Administrateur : activation puis invitation d'un technicien sur le site
await r.etape("1. Administrateur : activation puis invitation d'un technicien sur le site");
const admin = await (await r.contexte({ viewport: { width: 1440, height: 900 } }, "Administrateur")).newPage();
await admin.goto(`${base}/activation?jeton=${encodeURIComponent(jeton)}`, { timeout: 90000 });
await admin.fill("input[type=password] >> nth=0", mdp); await admin.fill("input[type=password] >> nth=1", mdp);
await admin.click("button[type=submit]"); await admin.waitForLoadState();
await admin.goto(`${base}/organisations/${org}/membres?inviter=1`);
await admin.fill("input[name='Saisie.Nom']", "Technicien Vérification");
await admin.fill("input[name='Saisie.Email']", `tech-${randomBytes(4).toString("hex")}@demo.gestionsite.test`);
await admin.click("button:has-text('Continuer')"); await admin.waitForLoadState();
await admin.selectOption("main select >> nth=0", "Technicien");
await admin.click("button:has-text(\"Créer l'invitation\")"); await admin.waitForLoadState();
const invitation = (await admin.textContent("code.lien-activation")).trim();
await ok("invitation du technicien créée", invitation.startsWith(`${base}/activation?jeton=`));

// 2. Technicien : activation puis inventaire vide
await r.etape("2. Technicien : activation puis inventaire vide");
const contexte = await r.contexte({ viewport: { width: 1440, height: 900 } }, "Technicien");
const tech = await contexte.newPage();
const violations = [];
tech.on("console", m => { if (m.type() === "error" && /Content.Security.Policy/i.test(m.text())) violations.push(`${tech.url()} → ${m.text()}`); });
await tech.goto(invitation);
await tech.fill("input[type=password] >> nth=0", mdp); await tech.fill("input[type=password] >> nth=1", mdp);
await tech.click("button:has-text('Activer')"); await tech.waitForLoadState();
const page = `${base}/organisations/${org}/inventaire`;
await tech.goto(page);
await ok("entrée Inventaire dans la navigation", await tech.locator("nav a:has-text('Inventaire')").count() === 1);
await ok("inventaire vide : invitation à décrire le site", (await tech.textContent("main")).includes("Décrivez le site"));

// 3. Structure : bâtiment, niveau, zone
await r.etape("3. Structure : bâtiment, niveau, zone");
const envoyer = async () => { await Promise.all([tech.waitForNavigation(), tech.click("form.formulaire-inventaire button[type=submit]")]); };
await tech.click("a:has-text('Ajouter un bâtiment')"); await tech.waitForLoadState();
await tech.fill("input[name='StructureSaisie.Nom']", "Église"); await envoyer();
await ok("bâtiment créé avec ses parties communes", (await tech.textContent(".arbre")).includes("Parties communes"));
await tech.click(".arbre a:has-text('+ Niveau')"); await tech.waitForLoadState();
await tech.fill("input[name='StructureSaisie.Nom']", "Rez-de-chaussée"); await tech.fill("input[name='StructureSaisie.Ordre']", "0"); await envoyer();
await tech.click(".arbre a:has-text('+ Zone')"); await tech.waitForLoadState();
await tech.fill("input[name='StructureSaisie.Nom']", "Grande salle");
await tech.selectOption("select[name='StructureSaisie.NiveauId']", { label: "Rez-de-chaussée" }); await envoyer();
await ok("zone créée sous son niveau et sélectionnée", (await tech.textContent(".arbre a[aria-current='page']")).includes("Grande salle"));

// 4. Composants : lot réparti par état, puis composant individuel soumis à vérification
await r.etape("4. Composants : lot réparti par état, puis composant individuel soumis à vérification");
await tech.click("a:has-text('Ajouter un composant')"); await tech.waitForLoadState();
await tech.fill("input[name='ComposantSaisi.Nom']", "Chaises");
await tech.selectOption("select[name='ComposantSaisi.CorpsEtat']", "Mobilier");
await tech.selectOption("select[name='ComposantSaisi.Mode']", "lot");
await tech.fill("input[name='ComposantSaisi.Quantite']", "40");
await tech.fill("input[name='ComposantSaisi.Bon']", "30"); await tech.fill("input[name='ComposantSaisi.Degrade']", "8");
await tech.fill("input[name='ComposantSaisi.Critique']", "2"); await envoyer();
await ok("lot affiché avec sa répartition", await tech.locator("svg.barre-etats[aria-label='30 bon, 8 dégradé, 2 critique']").count() === 1);
await ok("historique : création du lot par le technicien", (await tech.textContent(".historique")).includes("Création : lot de 40 unités") && (await tech.textContent(".historique")).includes("Technicien Vérification"));
await tech.click("a:has-text('Ajouter un composant')"); await tech.waitForLoadState();
await tech.fill("input[name='ComposantSaisi.Nom']", "Tableau divisionnaire");
await tech.selectOption("select[name='ComposantSaisi.CorpsEtat']", "CourantsForts");
await tech.fill("input[name='ComposantSaisi.Marque']", "Legrand"); await tech.fill("input[name='ComposantSaisi.Annee']", "2012"); await envoyer();
await ok("vérification proposée pour les courants forts", (await tech.textContent(".fiche-composant")).includes("Vérification périodiqueOui"));
await tech.screenshot({ path: "us07-inventaire-jour.png", fullPage: true });
await tech.evaluate(() => document.documentElement.setAttribute("data-theme", "nuit"));
await tech.screenshot({ path: "us07-inventaire-nuit.png", fullPage: true });

// 5. Filtres et refus expliqué
await r.etape("5. Filtres et refus expliqué");
await tech.goto(`${page}?verification=true`);
const filtre = await tech.textContent(".liste-composants");
await ok("filtre « soumis à vérification »", filtre.includes("Tableau divisionnaire") && !filtre.includes("Chaises"));
await tech.goto(`${page}?recherche=chai`);
await ok("recherche", (await tech.textContent(".liste-composants")).includes("Chaises"));
await tech.click("a:has-text('Ajouter un composant')"); await tech.waitForLoadState();
await tech.fill("input[name='ComposantSaisi.Nom']", "Bancs");
await tech.selectOption("select[name='ComposantSaisi.CorpsEtat']", "Mobilier");
await tech.selectOption("select[name='ComposantSaisi.ZoneId']", { label: "Grande salle (Rez-de-chaussée)" });
await tech.selectOption("select[name='ComposantSaisi.Mode']", "lot");
await tech.fill("input[name='ComposantSaisi.Quantite']", "6"); await tech.fill("input[name='ComposantSaisi.Bon']", "7");
await tech.click("form.formulaire-inventaire button[type=submit]"); await tech.waitForLoadState();
await ok("répartition incohérente expliquée, saisie conservée",
  (await tech.textContent("[role=alert]")).includes("La somme des quantités par état doit égaler la quantité totale")
  && await tech.inputValue("input[name='ComposantSaisi.Nom']") === "Bancs");
violations.forEach(v => console.log("  violation : " + v.slice(0, 300)));
await ok("aucune violation de la politique de sécurité du contenu (activation comprise)", violations.length === 0);
await r.terminer();

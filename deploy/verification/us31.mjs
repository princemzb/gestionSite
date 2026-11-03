// Vérification de la préproduction pour US-31 (navigateur réel, Playwright 1.55.0 ; recette visible avec RECETTE=1) :
// administration autonome selon les maquettes validées le 03/11. Sites : « Ajouter un site » en deux étapes (nom en double
// refusé sans tenir compte des majuscules, QR et « À faire ensuite ») ; Membres : panneau latéral d'un membre (onglets
// Rôles, Plans, Accès ; motif obligatoire ; droit « modifier les plans » accordé avec motif), nouveau lien d'activation
// d'une invitation en attente, lien de réinitialisation créé par l'administrateur puis page « Nouveau mot de passe » et
// connexion, refus de se révoquer soi-même, onglet Journal ; page « Mot de passe oublié » signalée hors service (e-mail
// non acheté, S10-09). Si VERIF_ORG2 et VERIF_JETON2 sont fournis (seconde organisation fictive ouverte par
// « ouvrir-organisation »), le technicien, déjà membre de la première, est invité par la seconde : carte d'invitation en
// tête de l'application, acceptation, « Vous avez rejoint », sélecteur d'organisation, puis lien de réinitialisation refusé
// (compte lié à une autre organisation). Aucune violation de la politique de sécurité du contenu.
// Usage : deploy/verification/lancer.sh us31 (ou VERIF_URL=… VERIF_ORG=… VERIF_JETON=… [VERIF_ORG2=… VERIF_JETON2=…]
// node us31.mjs) ; organisation fictive ouverte par « ouvrir-organisation --site » (site « Site centre ») ; jetons jamais
// passés en argument ni écrits.
import { randomBytes } from "node:crypto";
import { preparer } from "./commun.mjs";

const { r, ok, base, org, jeton, page, activer, inviter, finir } = await preparer("us31");
const { VERIF_ORG2: org2, VERIF_JETON2: jeton2 } = process.env;
const texte = async (p, selecteur = "body") => ((await p.textContent(selecteur)) ?? "").replace(/\s+/g, " ");
const suffixe = randomBytes(3).toString("hex");
const nouveauSite = `Chapelle vérif ${suffixe}`;
const panneau = "aside#panneau";
const ligneSite = (p, nom) => p.locator(`${panneau} .lignes > div`, { hasText: nom });

// 1. Administrateur : création d'un site en deux étapes (maquette 1B)
await r.etape("1. Administrateur : ajouter un site en deux étapes");
const admin = await page("Administrateur");
await activer(admin, `${base}/activation?jeton=${encodeURIComponent(jeton)}`);
await admin.goto(`${base}/organisations/${org}/sites`);
await admin.click("a:has-text('Ajouter un site')"); await admin.waitForLoadState();
await ok("étape 1 : nom du site demandé", (await texte(admin, "main")).includes("1. Nom du site"));
await admin.fill("input[name='SaisieSite.Nom']", "  site CENTRE ");
await admin.click("button:has-text('Créer le site')"); await admin.waitForLoadState();
await ok("nom en double refusé (majuscules et espaces ignorés)",
  (await texte(admin, "main")).includes("Un site porte déjà ce nom dans l'organisation. Choisissez un autre nom."));
await admin.fill("input[name='SaisieSite.Nom']", nouveauSite);
await admin.click("button:has-text('Créer le site')"); await admin.waitForLoadState();
const etape2 = await texte(admin, ".creation-site");
await ok("étape 2 : site créé avec son QR code et son lien public",
  admin.url().includes("?cree=") && etape2.includes(`${nouveauSite} est créé`) && await admin.locator(".creation-site .qr svg").count() === 1
  && etape2.includes(`${base}/s/`));
await ok("étape 2 : « À faire ensuite » (numéro d'urgence, rôles, bâtiments)",
  ["À faire ensuite", "Saisir le numéro", "Ouvrir Membres", "Décrire les bâtiments"].every(t => etape2.includes(t)));
await admin.screenshot({ path: "us31-site-cree.png", fullPage: true });
await admin.click(".creation-site a:has-text('Terminer')"); await admin.waitForLoadState();
await ok("le nouveau site figure dans la grille des sites", (await texte(admin, ".grille-sites")).includes(nouveauSite));

// 2. Membres : un technicien actif et une invitation en attente
await r.etape("2. Administrateur : invitation d'un technicien et d'une pasteure");
const technicien = await inviter(admin, "Marc Technicien", "Technicien");
const pasteure = await inviter(admin, "Paule Pasteure", "Pasteur");
const tech = await page("Technicien");
await activer(tech, technicien.lien);
await ok("le technicien active son compte", !tech.url().includes("/activation"));

// 3. Panneau du technicien : onglet Rôles (maquette 2B)
await r.etape("3. Panneau d'un membre : onglet Rôles, motif obligatoire");
await admin.goto(`${base}/organisations/${org}/membres`);
await admin.click("a.membre-lien:has-text('Marc Technicien')"); await admin.waitForLoadState();
await ok("panneau ouvert à onglets Rôles / Plans / Accès",
  await admin.locator(panneau).count() === 1 && (await texte(admin, `${panneau} nav.segments`)).replace(/\s/g, "") === "RôlesPlansAccès");
await ligneSite(admin, "Site centre").locator("select").selectOption("Technicien");
await admin.click(`${panneau} button:has-text('Enregistrer les rôles')`);
const motifManquant = await admin.locator(`${panneau} textarea[name='Roles.Motif']`).evaluate(e => e.validity.valueMissing);
await ok("motif manquant : enregistrement refusé", motifManquant && !(await texte(admin, panneau)).includes("enregistrés"));
await admin.fill(`${panneau} textarea[name='Roles.Motif']`, "Renfort sur le site centre (vérification)");
await admin.click(`${panneau} button:has-text('Enregistrer les rôles')`); await admin.waitForLoadState();
await ok("rôles enregistrés, message de réussite",
  (await texte(admin, panneau)).includes("Accès de Marc Technicien enregistrés. Ils s'appliquent dès sa prochaine action."));
await admin.screenshot({ path: "us31-panneau-roles.png", fullPage: true });

// 4. Onglet Plans : droit « modifier les plans » accordé avec un motif (maquette 3A)
await r.etape("4. Onglet Plans : accorder le droit « modifier les plans »");
await admin.click(`${panneau} nav.segments a:has-text('Plans')`); await admin.waitForLoadState();
await admin.click(`${panneau} a.interrupteur[aria-label='Accorder le droit sur Site centre']`); await admin.waitForLoadState();
await ok("encadré « Accorder sur Site centre » avec motif", (await texte(admin, `${panneau} .encadre`)).includes("Accorder sur Site centre"));
await admin.fill(`${panneau} textarea[name='Plans.Motif']`, "Relevé des plans du site centre (vérification)");
await admin.click(`${panneau} button:has-text('Accorder le droit')`); await admin.waitForLoadState();
await ok("droit accordé, message de réussite", (await texte(admin, panneau)).includes("Droit « modifier les plans » accordé à Marc Technicien."));
await ok("interrupteur du site centre actif",
  await admin.locator(`${panneau} a.interrupteur.actif[aria-label='Retirer le droit sur Site centre']`).count() === 1);
await ok("mention « + plans » dans la grille", (await texte(admin, "table")).includes("+ plans"));
await admin.screenshot({ path: "us31-panneau-plans.png", fullPage: true });

// 5. Onglet Accès : nouveau lien d'activation (invitation en attente) et lien de réinitialisation (membre actif)
await r.etape("5. Onglet Accès : nouveau lien d'activation et lien de réinitialisation");
await admin.goto(`${base}/organisations/${org}/membres`);
await admin.click("a.membre-lien:has-text('Paule Pasteure')"); await admin.waitForLoadState();
await admin.click(`${panneau} nav.segments a:has-text('Accès')`); await admin.waitForLoadState();
await admin.click(`${panneau} button:has-text(\"Nouveau lien d'activation\")`); await admin.waitForLoadState();
const lienActivation = (await admin.textContent(`${panneau} code.lien-activation`))?.trim() ?? "";
await ok("nouveau lien d'activation affiché une fois", lienActivation.startsWith(`${base}/activation?jeton=`) && lienActivation !== pasteure.lien);
await admin.goto(`${base}/organisations/${org}/membres`);
await admin.click("a.membre-lien:has-text('Marc Technicien')"); await admin.waitForLoadState();
await admin.click(`${panneau} nav.segments a:has-text('Accès')`); await admin.waitForLoadState();
await admin.click(`${panneau} button:has-text('Créer un lien de réinitialisation')`); await admin.waitForLoadState();
const lienReinitialisation = (await admin.textContent(`${panneau} code.lien-activation`))?.trim() ?? "";
await ok("lien de réinitialisation affiché une fois, valable 1 heure",
  lienReinitialisation.startsWith(`${base}/reinitialisation?jeton=`) && (await texte(admin, panneau)).includes("vaut 1 heure"));
await admin.screenshot({ path: "us31-panneau-acces.png", fullPage: true });

// 6. Le technicien choisit un nouveau mot de passe (maquette 5A) ; l'écran de connexion propose « Mot de passe oublié ? »
await r.etape("6. Technicien : nouveau mot de passe par le lien, puis connexion");
const nouveauMdp = `${randomBytes(12).toString("hex")}-neuf`;
await tech.goto(lienReinitialisation);
await ok("page « Nouveau mot de passe » (autres appareils déconnectés)",
  (await texte(tech, "main")).includes("Nouveau mot de passe") && (await texte(tech, "main")).includes("Vos autres appareils seront déconnectés."));
await tech.fill("input[type=password] >> nth=0", nouveauMdp); await tech.fill("input[type=password] >> nth=1", nouveauMdp);
await tech.click("button:has-text('Enregistrer et me connecter')"); await tech.waitForLoadState();
await tech.goto(`${base}/`); await tech.waitForLoadState();
await ok("technicien connecté avec le nouveau mot de passe", tech.url().includes(`/organisations/${org}/dossiers`));
await tech.goto(lienReinitialisation);
await tech.fill("input[type=password] >> nth=0", nouveauMdp); await tech.fill("input[type=password] >> nth=1", nouveauMdp);
await tech.click("button:has-text('Enregistrer et me connecter')"); await tech.waitForLoadState();
await ok("lien déjà utilisé refusé", (await texte(tech, "main")).includes("Lien expiré ou déjà utilisé."));
const anonyme = await page("Visiteur");
await anonyme.goto(`${base}/connexion`);
await anonyme.click("a:has-text('Mot de passe oublié ?')"); await anonyme.waitForLoadState();
await ok("« Mot de passe oublié » : e-mail signalé pas encore en service",
  anonyme.url().endsWith("/mot-de-passe-oublie") && (await texte(anonyme, "main")).includes("La réinitialisation par e-mail n'est pas encore en service."));
await anonyme.screenshot({ path: "us31-mot-de-passe-oublie.png", fullPage: true });

// 7. Administrateur : se révoquer soi-même est refusé ; journal des habilitations
await r.etape("7. Administrateur : son propre accès et le journal des habilitations");
await admin.goto(`${base}/organisations/${org}/membres`);
await admin.locator("tr", { hasText: "Administrateur · toute l'organisation" }).locator("a.membre-lien").first().click();
await admin.waitForLoadState();
await admin.click(`${panneau} nav.segments a:has-text('Accès')`); await admin.waitForLoadState();
await ok("se révoquer soi-même : refusé et expliqué",
  (await texte(admin, panneau)).includes("Vous ne pouvez pas révoquer votre propre accès : un autre administrateur doit le faire.")
  && await admin.locator(`${panneau} a:has-text(\"Révoquer l'accès\")`).count() === 0);
await admin.goto(`${base}/organisations/${org}/membres?vue=journal`);
const journal = await texte(admin, "main");
await ok("onglet Journal : entrées des actions de la vérification",
  ["Journal des habilitations", "Rôles modifiés", "Droit « plans » accordé", "Nouveau lien d'activation", "Lien de réinitialisation créé",
    "Mot de passe réinitialisé", "Relevé des plans du site centre (vérification)"].every(t => journal.includes(t)));
await admin.screenshot({ path: "us31-journal.png", fullPage: true });

// 8. Seconde organisation (facultative) : invitation d'un compte existant, carte en tête, sélecteur d'organisation
if (org2 && jeton2) {
  await r.etape("8. Seconde organisation : invitation d'un compte existant et changement d'organisation");
  const admin2 = await page("Administrateur 2");
  await activer(admin2, `${base}/activation?jeton=${encodeURIComponent(jeton2)}`);
  await admin2.goto(`${base}/organisations/${org2}/membres?inviter=1`);
  await admin2.fill("input[name='Saisie.Nom']", "Marc Technicien");
  await admin2.fill("input[name='Saisie.Email']", technicien.email);
  await admin2.click("button:has-text('Continuer')"); await admin2.waitForLoadState();
  await admin2.selectOption("main select >> nth=0", "Technicien");
  await admin2.click("button:has-text(\"Créer l'invitation\")"); await admin2.waitForLoadState();
  await ok("compte existant : invitation sans lien", (await texte(admin2, "main")).includes("Son compte existe déjà : l'invitation l'attend dans son espace"));
  await tech.goto(`${base}/`); await tech.waitForLoadState();
  const carte = await texte(tech, ".carte-invitation");
  await ok("carte d'invitation en tête : organisation, rôle par site, qui invite",
    carte.includes("vous invite") && carte.includes("Technicien sur") && carte.includes("Admin"));
  await tech.screenshot({ path: "us31-invitation.png", fullPage: true });
  await tech.click(".carte-invitation button:has-text(\"Accepter l'invitation\")"); await tech.waitForLoadState();
  await ok("invitation acceptée : « Vous avez rejoint »", (await texte(tech, "main")).includes("Vous avez rejoint"));
  await tech.click("a:has-text('Ouvrir ')"); await tech.waitForLoadState();
  await ok("seconde organisation ouverte, sélecteur d'organisation visible",
    tech.url().includes(`/organisations/${org2}/`) && await tech.locator("details.organisation-choix").count() === 1);
  await admin.goto(`${base}/organisations/${org}/membres`);
  await admin.click("a.membre-lien:has-text('Marc Technicien')"); await admin.waitForLoadState();
  await admin.click(`${panneau} nav.segments a:has-text('Accès')`); await admin.waitForLoadState();
  await admin.click(`${panneau} button:has-text('Créer un lien de réinitialisation')`); await admin.waitForLoadState();
  await ok("compte lié à une autre organisation : lien de réinitialisation refusé",
    (await texte(admin, panneau)).includes("Lien impossible pour ce compte"));
} else {
  console.log("—     seconde organisation non fournie (VERIF_ORG2, VERIF_JETON2) : invitation d'un compte existant non vérifiée");
}

await finir();

// Éditeur de plans de bâtiment (US-21, S21-07, ADR-11) : colle DOM mince entre la page Plans (data-*) et le noyau pur
// (plans/noyau/*.mjs, testé par node:test). Aucun style ni script en ligne (CSP), textContent pour tout texte saisi,
// fond servi par l'application web (<image href>, img-src 'self'). Vérifiée dans un navigateur réel (Playwright).

import { ecrireBrouillon, effacerBrouillon, cleBrouillon, lireBrouillon, purgerAutresComptes } from "./noyau/brouillon.mjs";
import {
  bornesFond, defautCote, distanceProposee, ecartControle, formaterEcart, formaterMetres, lireDistance, longueurPixels, messageDefaut,
  metresParPixel, metresParPixelDeclares, pixelVersRepere, pointImage, repereVersPixel,
} from "./noyau/calibration.mjs";
import { annuler, appliquer, creerHistorique, peutAnnuler, peutRetablir, retablir } from "./noyau/historique.mjs";
import { distance } from "./noyau/mesures.mjs";
import {
  abandonnerSaisie, bornesAffichees, centre, commencerCalibration, conflit, controleFond, corpsEnregistrement, descriptionFond,
  documentDepuisVersion, echelleFond, estModifie, fixerDistance, formaterDate, messageRefus, pointsSvg, poserPoint, resumeVersion,
  retirerFond, validerCalibration,
} from "./noyau/plan.mjs";
import { ajuster, deplacer, echelleVue, pasGrille, pincer, redimensionner, versRepere, viewBox, zoomer } from "./noyau/vue.mjs";

const SVG = "http://www.w3.org/2000/svg";
const ECHELLE_PROVISOIRE = 0.01; // fond pas encore calibré : 1 cm par pixel, le temps de poser la cote
const SEUIL_GLISSER = 6; // pixels : en deçà, un appui est un toucher (pose d'un point), pas un glissement

const racine = document.getElementById("editeur-plan");
if (racine) {
  demarrer(racine);
}

function demarrer(racine) {
  const page = racine.closest("main");
  const d = racine.dataset;
  const modifiable = d.modifiable === "true";
  const base = `/organisations/${d.organisation}/sites/${d.site}/plans-batiment`;
  const el = selecteur => page.querySelector(selecteur);
  const tous = selecteur => [...page.querySelectorAll(selecteur)];
  const svg = el("[data-role=svg]");
  const couches = { fond: el("[data-role=fond]"), calque: el("[data-role=calque]"), pieces: el("[data-role=pieces]"), cotes: el("[data-role=cotes]") };
  let zones = {};
  try {
    zones = JSON.parse(d.zones || "{}");
  } catch {
    zones = {};
  }

  const etat = {
    mode: d.mode,
    planId: d.plan || null,
    lue: null,
    versionLue: 0,
    reference: documentDepuisVersion(null),
    historique: creerHistorique(documentDepuisVersion(null)),
    fonds: [],
    panneau: d.mode === "guide" ? "fond" : "proprietes",
    cible: "calibration",
    outil: "selection",
    echelleSaisie: null,
    vue: null,
    affichage: null,
    calque: { affiche: false, contenu: null },
    mesure: { de: null, a: null },
    reprise: null,
    enCours: false,
    rendus: { pieces: null, fond: null, calque: null },
  };
  const present = () => etat.historique.present;

  // ——— Stockage local (brouillon), réseau, messages ———
  function stockage() {
    try {
      return window.localStorage;
    } catch {
      return null;
    }
  }

  function cle() {
    return etat.planId ? cleBrouillon(d.compte, d.organisation, etat.planId) : null;
  }

  async function appeler(methode, chemin, corps) {
    const options = { method: methode, headers: { Accept: "application/json" }, credentials: "same-origin", cache: "no-store" };
    if (methode !== "GET") {
      options.headers.RequestVerificationToken = d.jeton;
    }

    if (corps instanceof FormData) {
      options.body = corps;
    } else if (corps !== undefined) {
      options.headers["Content-Type"] = "application/json";
      options.body = JSON.stringify(corps);
    }

    let reponse;
    try {
      reponse = await fetch(chemin, options);
    } catch {
      return { statut: 0, probleme: null };
    }

    let json = null;
    try {
      const texte = await reponse.text();
      json = texte ? JSON.parse(texte) : null;
    } catch {
      json = null;
    }

    return reponse.ok ? { statut: reponse.status, donnees: json } : { statut: reponse.status, probleme: json };
  }

  function message(texte, genre = "succes") {
    const zone = el("[data-role=message]");
    zone.textContent = texte;
    zone.className = `alerte ${genre}`;
    zone.hidden = !texte;
  }

  // ——— Fond affiché et échelle de travail ———
  const fondInfo = id => etat.fonds.find(f => f.id === id) ?? null;
  const fondAffiche = () => present().saisie?.fondId ?? present().contenu.fond?.fondId ?? null;
  const image = info => ({ largeur: info.largeurAffichage, hauteur: info.hauteurAffichage });

  function echelleTravail() {
    if (present().saisie) {
      return etat.echelleSaisie ?? ECHELLE_PROVISOIRE;
    }

    return echelleFond(present().contenu) ?? ECHELLE_PROVISOIRE;
  }

  function bornesActuelles() {
    const info = fondInfo(fondAffiche());
    return bornesAffichees(present().contenu, info ? bornesFond(image(info), echelleTravail()) : null);
  }

  // ——— Vue ———
  let vuePlanifiee = false;
  function mesurerAffichage() {
    const rect = svg.getBoundingClientRect();
    return { largeur: Math.max(1, rect.width), hauteur: Math.max(1, rect.height) };
  }

  function ajusterVue() {
    etat.affichage = mesurerAffichage();
    etat.vue = ajuster(bornesActuelles(), etat.affichage);
    planifierVue();
  }

  function planifierVue() {
    if (!vuePlanifiee) {
      vuePlanifiee = true;
      requestAnimationFrame(() => {
        vuePlanifiee = false;
        appliquerVue();
      });
    }
  }

  function appliquerVue() {
    if (!etat.vue) {
      return;
    }

    svg.setAttribute("viewBox", viewBox(etat.vue));
    const metres = echelleVue(etat.vue, etat.affichage);
    // Textes et points à taille constante à l'écran : une seule valeur par couche, recalculée à chaque vue.
    couches.pieces.setAttribute("font-size", String(13 * metres));
    couches.cotes.setAttribute("font-size", String(13 * metres));
    for (const point of couches.cotes.querySelectorAll("circle")) {
      point.setAttribute("r", String(7 * metres));
    }

    const pas = pasGrille(etat.vue, etat.affichage);
    if (etat.rendus.pas !== pas) {
      etat.rendus.pas = pas;
      const motif = el("[data-role=motif]");
      motif.setAttribute("width", String(pas));
      motif.setAttribute("height", String(pas));
      el("[data-role=motif-trait]").setAttribute("d", `M ${pas} 0 L 0 0 0 ${pas}`);
      el("[data-role=barre-echelle]").textContent = `Grille : ${formaterMetres(pas).replace(",00", "")}`;
    }
  }

  // ——— Rendu ———
  function creer(nom, attributs = {}, parent = null) {
    const element = document.createElementNS(SVG, nom);
    for (const [cleAttribut, valeur] of Object.entries(attributs)) {
      element.setAttribute(cleAttribut, String(valeur));
    }

    parent?.append(element);
    return element;
  }

  function rendreGeometrie(groupe, contenu, avecNoms) {
    groupe.replaceChildren();
    for (const piece of contenu.pieces) {
      creer("polygon", { points: pointsSvg(piece.contour) }, groupe);
      if (avecNoms) {
        const c = centre(piece.contour);
        const nom = creer("text", { x: c.x, y: c.y }, groupe);
        nom.textContent = zones[piece.zoneId] ?? "Zone";
      }
    }

    if (!avecNoms) {
      return;
    }

    for (const escalier of contenu.escaliers) {
      creer("polygon", { points: pointsSvg(escalier.contour), class: "escalier" }, groupe);
    }

    for (const ouverture of contenu.ouvertures) {
      creer("line", { x1: ouverture.de.x, y1: ouverture.de.y, x2: ouverture.a.x, y2: ouverture.a.y, class: "ouverture" }, groupe);
    }

    for (const marqueur of contenu.composants) {
      creer("circle", { cx: marqueur.position.x, cy: marqueur.position.y, r: 0.25, class: "composant" }, groupe);
    }
  }

  function rendreFond() {
    const id = fondAffiche();
    const info = fondInfo(id);
    const metres = echelleTravail();
    const cleRendu = info ? `${id}:${metres}` : null;
    if (etat.rendus.fond === cleRendu) {
      return;
    }

    etat.rendus.fond = cleRendu;
    couches.fond.replaceChildren();
    el("[data-role=fond-texte]").textContent = info ? `Fond : ${descriptionFond(info)}` : id ? "Fond en cours de chargement" : "Plan sur grille métrique";
    if (info) {
      creer("image", {
        href: `${base}/${etat.planId}/fonds/${id}`, x: 0, y: 0, width: info.largeurAffichage * metres, height: info.hauteurAffichage * metres,
        preserveAspectRatio: "none",
      }, couches.fond);
    }
  }

  function segment(de, a, classe, texte) {
    creer("line", { x1: de.x, y1: de.y, x2: a.x, y2: a.y, class: classe }, couches.cotes);
    if (texte) {
      const etiquette = creer("text", { x: (de.x + a.x) / 2, y: (de.y + a.y) / 2 }, couches.cotes);
      etiquette.textContent = texte;
    }
  }

  function rendreCotes() {
    couches.cotes.replaceChildren();
    const saisie = present().saisie;
    const metres = echelleTravail();
    const points = [];
    if (saisie) {
      for (const cible of ["calibration", "controle"]) {
        const cote = saisie[cible];
        const de = cote.de ? pixelVersRepere(cote.de, metres) : null;
        const a = cote.a ? pixelVersRepere(cote.a, metres) : null;
        if (de && a) {
          segment(de, a, cible, cote.distance != null ? formaterMetres(cote.distance) : "?");
        }

        for (const p of [de, a].filter(Boolean)) {
          points.push([p, cible]);
        }
      }
    }

    if (etat.mesure.de) {
      if (etat.mesure.a) {
        segment(etat.mesure.de, etat.mesure.a, "mesure", formaterMetres(distance(etat.mesure.de, etat.mesure.a)));
      }

      for (const p of [etat.mesure.de, etat.mesure.a].filter(Boolean)) {
        points.push([p, "mesure"]);
      }
    }

    const rayon = etat.vue ? 7 * echelleVue(etat.vue, etat.affichage) : 0.1;
    for (const [p, classe] of points) {
      creer("circle", { cx: p.x, cy: p.y, r: rayon, class: classe }, couches.cotes);
    }
  }

  function rendrePanneauCalibration() {
    const saisie = present().saisie;
    if (!saisie) {
      return;
    }

    for (const bouton of tous("[data-cible]")) {
      bouton.setAttribute("aria-pressed", String(bouton.dataset.cible === etat.cible));
    }

    const cote = saisie[etat.cible];
    const nom = etat.cible === "calibration" ? "de calibration" : "de contrôle";
    el("[data-role=consigne]").textContent = !cote.de
      ? `Touchez le premier point de la cote ${nom} sur le fond.`
      : !cote.a
        ? `Touchez le second point de la cote ${nom}.`
        : `Cote ${nom} posée (${Math.round(longueurPixels(cote))} px). Touchez de nouveau le fond pour la recommencer.`;
    for (const cible of ["calibration", "controle"]) {
      const champ = el(`[data-champ=${cible}]`);
      if (document.activeElement !== champ) {
        const valeur = saisie[cible].distance;
        champ.value = valeur == null ? "" : String(valeur).replace(".", ",");
      }
    }

    const resultat = el("[data-role=resultat-controle]");
    const info = fondInfo(saisie.fondId);
    const calibree = info && !defautCote(saisie.calibration, image(info));
    const controle = saisie.controle;
    if (calibree && controle.de && controle.a && controle.distance != null && !defautCote(controle, image(info))) {
      const { mesuree, declaree, ecart, avertissement } = ecartControle(controle, metresParPixel(saisie.calibration));
      resultat.textContent = `Cote de contrôle : mesurée sur place ${formaterMetres(declaree)}, plan ${formaterMetres(mesuree)} · écart ${formaterEcart(ecart)}${avertissement ? " : au-delà de 2 %, vérifiez la calibration." : " ✓"}`;
      resultat.className = `alerte ${avertissement ? "avertissement" : "succes"}`;
      resultat.hidden = false;
    } else {
      resultat.hidden = true;
    }
  }

  function rendreProprietes() {
    const contenu = present().contenu;
    const info = fondInfo(contenu.fond?.fondId);
    el("[data-role=prop-fond]").textContent = contenu.fond ? (info ? descriptionFond(info) : "Fond importé") : "Aucun (grille métrique)";
    const metres = echelleFond(contenu);
    el("[data-role=prop-echelle]").textContent = metres
      ? `1 px du fond = ${(metres * 100).toLocaleString("fr-FR", { maximumFractionDigits: 2 })} cm`
      : "Mètres de la grille";
    const controle = controleFond(contenu);
    el("[data-role=prop-controle]").textContent = controle
      ? `${formaterMetres(controle.declaree)} mesurés, ${formaterMetres(controle.mesuree)} sur le plan · écart ${formaterEcart(controle.ecart)}${controle.avertissement ? " (au-delà de 2 %)" : ""}`
      : contenu.fond ? "Aucune" : "—";
    const recalibrer = el("[data-action=recalibrer]");
    if (recalibrer) {
      recalibrer.hidden = !contenu.fond;
    }

    if (etat.mode === "atelier") {
      el("[data-role=resume]").textContent = estModifie(present(), etat.reference)
        ? `${resumeVersion(etat.lue, etat.reference.contenu)} · modifications non enregistrées`
        : resumeVersion(etat.lue, etat.reference.contenu);
    }
  }

  function rendreCommandes() {
    for (const bouton of tous("[data-action=annuler]")) {
      bouton.disabled = !modifiable || !peutAnnuler(etat.historique);
    }

    for (const bouton of tous("[data-action=retablir]")) {
      bouton.disabled = !modifiable || !peutRetablir(etat.historique);
    }

    const enregistrer = el("[data-action=enregistrer]");
    if (enregistrer) {
      enregistrer.textContent = `Enregistrer la version ${etat.versionLue + 1}`;
      enregistrer.disabled = etat.enCours || Boolean(present().saisie) || !estModifie(present(), etat.reference) || Boolean(etat.reprise);
    }

    for (const bouton of tous("[data-outil]")) {
      bouton.setAttribute("aria-pressed", String(bouton.dataset.outil === etat.outil));
    }

    svg.classList.toggle("pointage", etat.panneau === "calibration" || etat.outil === "cote");
    for (const panneau of tous("[data-panneau]")) {
      panneau.hidden = panneau.dataset.panneau !== etat.panneau;
    }

    const etapes = tous("[data-etape]");
    const courante = etat.panneau === "calibration" ? 1 : 0;
    etapes.forEach((etape, rang) => {
      etape.classList.toggle("faite", rang < courante);
      if (rang === courante) {
        etape.setAttribute("aria-current", "step");
      } else {
        etape.removeAttribute("aria-current");
      }
    });
  }

  function rendre() {
    if (etat.rendus.pieces !== present().contenu) {
      etat.rendus.pieces = present().contenu;
      rendreGeometrie(couches.pieces, present().contenu, true);
    }

    rendreFond();
    rendreCotes();
    rendrePanneauCalibration();
    rendreProprietes();
    rendreCommandes();
    planifierVue();
  }

  // ——— Historique et brouillon ———
  let ecriturePlanifiee = null;
  function memoriserBrouillon() {
    clearTimeout(ecriturePlanifiee);
    ecriturePlanifiee = setTimeout(() => {
      const cleCourante = cle();
      const local = stockage();
      if (!modifiable || !cleCourante || !local || etat.reprise) {
        return;
      }

      if (estModifie(present(), etat.reference) || present().saisie) {
        ecrireBrouillon(local, {
          compteId: d.compte, organisationId: d.organisation, planId: etat.planId, versionLue: etat.versionLue, document: present(),
          modifieLe: new Date().toISOString(),
        });
      } else {
        effacerBrouillon(local, cleCourante);
      }
    }, 300);
  }

  function changer(document) {
    etat.historique = appliquer(etat.historique, document);
    memoriserBrouillon();
    rendre();
  }

  function charger(lue) {
    etat.lue = lue;
    etat.versionLue = lue?.version ?? 0;
    etat.reference = documentDepuisVersion(lue);
    etat.historique = creerHistorique(etat.reference);
    etat.rendus.pieces = null;
  }

  function afficherBrouillon(texte, { reprendre }) {
    const zone = el("[data-role=brouillon]");
    el("[data-role=brouillon-texte]").textContent = texte;
    el("[data-action=brouillon-reprendre]").hidden = !reprendre;
    el("[data-action=brouillon-confirmer]").hidden = true;
    zone.hidden = false;
  }

  function masquerBrouillon() {
    el("[data-role=brouillon]").hidden = true;
    etat.reprise = null;
  }

  // Brouillon local à l'ouverture : même version, il est repris ; version dépassée, la reprise est proposée (décision du
  // porteur du 02/11 : action explicite avec confirmation, jamais de réapplication automatique).
  function examinerBrouillon() {
    const local = stockage();
    const cleCourante = cle();
    if (!modifiable || !local || !cleCourante) {
      return;
    }

    const brouillon = lireBrouillon(local, cleCourante);
    if (!brouillon || brouillon.versionLue > etat.versionLue || !estModifie(brouillon.document, etat.reference) && !brouillon.document.saisie) {
      effacerBrouillon(local, cleCourante);
      return;
    }

    const quand = formaterDate(brouillon.modifieLe);
    if (brouillon.versionLue === etat.versionLue) {
      etat.historique = appliquer(etat.historique, brouillon.document);
      if (brouillon.document.saisie) {
        etat.echelleSaisie = echelleInitiale(brouillon.document.saisie.fondId, brouillon.document);
        etat.panneau = "calibration";
      }

      afficherBrouillon(`Brouillon non enregistré repris (modifié le ${quand}).`, { reprendre: false });
      return;
    }

    etat.reprise = { document: brouillon.document, versionCourante: etat.versionLue };
    afficherBrouillon(
      `Votre brouillon du ${quand} (sur la version ${brouillon.versionLue}) n'a pas été enregistré : le plan est passé à la version ${etat.versionLue}${etat.lue?.auteur ? ` (${etat.lue.auteur})` : ""}.`,
      { reprendre: true });
  }

  function echelleInitiale(fondId, document) {
    const fond = document.contenu.fond;
    if (fond?.fondId === fondId) {
      return metresParPixel(fond.calibration);
    }

    return metresParPixelDeclares(fondInfo(fondId)) ?? ECHELLE_PROVISOIRE;
  }

  // ——— Échanges avec le serveur ———
  async function lirePlan() {
    const reponse = await appeler("GET", `${base}/${etat.planId}`);
    if (reponse.statut !== 200) {
      message(messageRefus(reponse.statut, reponse.probleme), "danger");
      return null;
    }

    return reponse.donnees;
  }

  async function lireFonds() {
    if (!etat.planId) {
      return;
    }

    const reponse = await appeler("GET", `${base}/${etat.planId}/fonds`);
    etat.fonds = reponse.statut === 200 && Array.isArray(reponse.donnees) ? reponse.donnees : [];
    etat.rendus.fond = undefined;
    const liste = el("[data-role=fonds-liste]");
    if (!liste) {
      return;
    }

    liste.replaceChildren();
    for (const fond of [...etat.fonds].reverse()) {
      const bouton = document.createElement("button");
      bouton.type = "button";
      bouton.textContent = descriptionFond(fond);
      bouton.addEventListener("click", () => choisirFond(fond.id));
      liste.append(bouton);
    }

    el("[data-role=fonds]").hidden = etat.fonds.length === 0;
  }

  async function exigerPlan() {
    if (etat.planId) {
      return true;
    }

    const reponse = await appeler("POST", base, { batimentId: d.batiment, niveauId: d.niveau || null });
    const id = reponse.statut === 200 ? reponse.donnees?.id : reponse.probleme?.code === "plan_existant" ? reponse.probleme.planId : null;
    if (!id) {
      message(messageRefus(reponse.statut, reponse.probleme), "danger");
      return false;
    }

    etat.planId = id;
    return true;
  }

  async function enregistrer(motif) {
    etat.enCours = true;
    rendreCommandes();
    const corps = corpsEnregistrement(present(), etat.versionLue, crypto.randomUUID(), motif);
    const reponse = await appeler("PUT", `${base}/${etat.planId}`, corps);
    etat.enCours = false;
    if (reponse.statut === 200) {
      const local = stockage();
      if (local) {
        effacerBrouillon(local, cle());
      }

      const lue = await lirePlan();
      if (lue) {
        charger(lue);
      }

      masquerBrouillon();
      message(`Version ${reponse.donnees.version} enregistrée.`);
      rendre();
      return true;
    }

    memoriserBrouillon();
    const enConflit = conflit(reponse.statut, reponse.probleme);
    message(messageRefus(reponse.statut, reponse.probleme), "danger");
    if (enConflit) {
      etat.reprise = { document: present(), versionCourante: enConflit.versionCourante };
      afficherBrouillon(`Vous pouvez reprendre votre brouillon sur la version ${enConflit.versionCourante}, ou l'abandonner pour recharger cette version.`, { reprendre: true });
    }

    rendre();
    return false;
  }

  // ——— Actions ———
  function choisirFond(fondId) {
    etat.echelleSaisie = echelleInitiale(fondId, present());
    etat.cible = "calibration";
    etat.panneau = "calibration";
    changer(commencerCalibration(present(), fondId));
    ajusterVue();
  }

  function toucher(position) {
    const point = versRepere(etat.vue, etat.affichage, position);
    const saisie = present().saisie;
    if (etat.panneau === "calibration" && saisie) {
      const info = fondInfo(saisie.fondId);
      const pixel = info ? pointImage(repereVersPixel(point, etat.echelleSaisie), image(info)) : null;
      if (!pixel) {
        message("Touchez à l'intérieur du fond.", "avertissement");
        return;
      }

      message("");
      let suivant = poserPoint(present(), etat.cible, pixel);
      const cote = suivant.saisie[etat.cible];
      const proposee = cote.a && cote.distance == null ? distanceProposee(cote, info) : null;
      if (proposee) {
        suivant = fixerDistance(suivant, etat.cible, proposee); // échelle déclarée du PDF : à confirmer par la cote mesurée
      }

      changer(suivant);
      return;
    }

    if (etat.outil === "cote") {
      etat.mesure = !etat.mesure.de || etat.mesure.a ? { de: point, a: null } : { ...etat.mesure, a: point };
      const sortie = el("[data-role=mesure]");
      sortie.hidden = !etat.mesure.a;
      sortie.textContent = etat.mesure.a ? `Cote mesurée : ${formaterMetres(distance(etat.mesure.de, etat.mesure.a))}` : "";
      rendreCotes();
      planifierVue();
    }
  }

  async function validerEchelle() {
    const saisie = present().saisie;
    const info = fondInfo(saisie?.fondId);
    if (!info) {
      return;
    }

    const resultat = validerCalibration(present(), image(info));
    if (resultat.defaut) {
      const nom = resultat.defaut.cote === "calibration" ? "Cote de calibration" : "Cote de contrôle";
      etat.cible = resultat.defaut.cote;
      message(`${nom} : ${messageDefaut(resultat.defaut.defaut)}`, "avertissement");
      rendre();
      return;
    }

    etat.echelleSaisie = null;
    etat.panneau = "proprietes";
    changer(resultat.document);
    ajusterVue();
    if (etat.mode === "guide") {
      // Parcours guidé : chaque étape s'enregistre comme une version (maquette C).
      if (await enregistrer("Calibration du fond")) {
        passerEnAtelier();
      }
    } else {
      message("Échelle validée : enregistrez la version pour la conserver.", "succes");
    }
  }

  async function commencerGrille() {
    if (etat.mode === "guide") {
      if (!(await exigerPlan())) {
        return;
      }

      changer(retirerFond(present()));
      if (await enregistrer("Plan sur grille métrique")) {
        passerEnAtelier();
      }

      return;
    }

    etat.panneau = "proprietes";
    changer(retirerFond(present()));
    ajusterVue();
  }

  // Étapes 3 et 4 du parcours (pièces, composants) : S21-08 et S21-09 ; en attendant, l'atelier.
  function passerEnAtelier() {
    window.location.reload();
  }

  async function importer(formulaire) {
    const fichier = formulaire.elements.fichier.files[0];
    if (!fichier) {
      message("Choisissez un fichier.", "avertissement");
      return;
    }

    if (!(await exigerPlan())) {
      return;
    }

    const envoi = new FormData();
    envoi.append("commandeId", crypto.randomUUID());
    envoi.append("fichier", fichier);
    for (const champ of ["page", "echelle"]) {
      const valeur = formulaire.elements[champ].value.trim();
      if (valeur) {
        envoi.append(champ, valeur);
      }
    }

    message("Import en cours : un PDF peut demander jusqu'à une minute.", "avertissement");
    const reponse = await appeler("POST", `${base}/${etat.planId}/fonds`, envoi);
    if (reponse.statut !== 200) {
      message(messageRefus(reponse.statut, reponse.probleme), "danger");
      return;
    }

    message("Fond importé : calibrez maintenant l'échelle.");
    formulaire.reset();
    await lireFonds();
    choisirFond(reponse.donnees.id);
  }

  async function basculerCalque(bouton) {
    etat.calque.affiche = !etat.calque.affiche;
    if (etat.calque.affiche && !etat.calque.contenu) {
      const reponse = await appeler("GET", `${base}/${d.planInferieur}`);
      if (reponse.statut !== 200) {
        etat.calque.affiche = false;
        message(messageRefus(reponse.statut, reponse.probleme), "danger");
        return;
      }

      etat.calque.contenu = documentDepuisVersion(reponse.donnees).contenu;
      rendreGeometrie(couches.calque, etat.calque.contenu, false);
    }

    couches.calque.toggleAttribute("hidden", !etat.calque.affiche); // élément SVG : pas de propriété hidden
    bouton.setAttribute("aria-pressed", String(etat.calque.affiche));
    bouton.textContent = `Calque ${d.niveauInferieur} : ${etat.calque.affiche ? "affiché" : "masqué"}`;
  }

  async function reprendreBrouillon() {
    const reprise = etat.reprise;
    const lue = await lirePlan();
    if (!reprise || !lue) {
      return;
    }

    charger(lue);
    etat.historique = appliquer(etat.historique, { ...reprise.document, saisie: null });
    masquerBrouillon();
    memoriserBrouillon();
    message(`Brouillon repris sur la version ${etat.versionLue} : enregistrez pour en faire la version ${etat.versionLue + 1} ; la version ${etat.versionLue} reste dans l'historique.`, "avertissement");
    rendre();
  }

  async function abandonnerBrouillon() {
    const local = stockage();
    if (local && cle()) {
      effacerBrouillon(local, cle());
    }

    const lue = etat.planId ? await lirePlan() : null;
    charger(lue);
    etat.echelleSaisie = null;
    etat.panneau = etat.mode === "guide" ? "fond" : "proprietes";
    masquerBrouillon();
    message("Brouillon abandonné.");
    rendre();
    ajusterVue();
  }

  // ——— Gestes : un doigt glisse la vue (ou pose un point s'il ne bouge pas), deux doigts zooment ———
  const pointeurs = new Map();
  let geste = null;
  const position = evenement => {
    const rect = svg.getBoundingClientRect();
    return { x: evenement.clientX - rect.left, y: evenement.clientY - rect.top };
  };

  svg.addEventListener("pointerdown", evenement => {
    svg.setPointerCapture?.(evenement.pointerId);
    pointeurs.set(evenement.pointerId, position(evenement));
    if (pointeurs.size === 1) {
      geste = { type: "glisser", depart: position(evenement), dernier: position(evenement), deplace: false };
    } else if (pointeurs.size === 2) {
      geste = { type: "pincer", avant: [...pointeurs.values()] };
    }
  });

  svg.addEventListener("pointermove", evenement => {
    if (!pointeurs.has(evenement.pointerId) || !geste || !etat.vue) {
      return;
    }

    const p = position(evenement);
    pointeurs.set(evenement.pointerId, p);
    if (geste.type === "pincer" && pointeurs.size === 2) {
      const apres = [...pointeurs.values()];
      etat.vue = pincer(etat.vue, etat.affichage, geste.avant, apres);
      geste.avant = apres;
      planifierVue();
    } else if (geste.type === "glisser") {
      if (!geste.deplace && Math.hypot(p.x - geste.depart.x, p.y - geste.depart.y) > SEUIL_GLISSER) {
        geste.deplace = true;
      }

      if (geste.deplace) {
        etat.vue = deplacer(etat.vue, etat.affichage, p.x - geste.dernier.x, p.y - geste.dernier.y);
        planifierVue();
      }

      geste.dernier = p;
    }
  });

  function finPointeur(evenement, annule) {
    if (!pointeurs.has(evenement.pointerId)) {
      return;
    }

    pointeurs.delete(evenement.pointerId);
    if (geste?.type === "glisser" && !geste.deplace && !annule && pointeurs.size === 0 && etat.vue) {
      toucher(position(evenement));
    }

    // Après un pincement, le doigt restant glisse la vue sans poser de point.
    geste = pointeurs.size === 1 ? { type: "glisser", depart: [...pointeurs.values()][0], dernier: [...pointeurs.values()][0], deplace: true } : null;
  }

  svg.addEventListener("pointerup", evenement => finPointeur(evenement, false));
  svg.addEventListener("pointercancel", evenement => finPointeur(evenement, true));
  svg.addEventListener("wheel", evenement => {
    if (!etat.vue) {
      return;
    }

    evenement.preventDefault();
    const pixels = evenement.deltaMode === 1 ? evenement.deltaY * 16 : evenement.deltaY;
    etat.vue = zoomer(etat.vue, etat.affichage, Math.exp(-pixels * 0.0015), position(evenement));
    planifierVue();
  }, { passive: false });

  // Alternatives sans glisser (WCAG 2.5.7) : boutons et clavier pour zoomer, déplacer et tout afficher.
  function commandeVue(action) {
    if (!etat.vue) {
      return false;
    }

    const pas = 0.2;
    const vues = {
      "zoom-avant": () => zoomer(etat.vue, etat.affichage, 1.5),
      "zoom-arriere": () => zoomer(etat.vue, etat.affichage, 1 / 1.5),
      gauche: () => deplacer(etat.vue, etat.affichage, etat.affichage.largeur * pas, 0),
      droite: () => deplacer(etat.vue, etat.affichage, -etat.affichage.largeur * pas, 0),
      haut: () => deplacer(etat.vue, etat.affichage, 0, etat.affichage.hauteur * pas),
      bas: () => deplacer(etat.vue, etat.affichage, 0, -etat.affichage.hauteur * pas),
    };
    if (action === "ajuster") {
      ajusterVue();
      return true;
    }

    if (!vues[action]) {
      return false;
    }

    etat.vue = vues[action]();
    planifierVue();
    return true;
  }

  svg.addEventListener("keydown", evenement => {
    const touches = { ArrowLeft: "gauche", ArrowRight: "droite", ArrowUp: "haut", ArrowDown: "bas", "+": "zoom-avant", "=": "zoom-avant", "-": "zoom-arriere", 0: "ajuster" };
    if (touches[evenement.key] && commandeVue(touches[evenement.key])) {
      evenement.preventDefault();
    }
  });

  document.addEventListener("keydown", evenement => {
    const champ = evenement.target.closest?.("input, textarea, select");
    if (champ || !modifiable || !(evenement.ctrlKey || evenement.metaKey) || evenement.key.toLowerCase() !== "z" && evenement.key.toLowerCase() !== "y") {
      return;
    }

    evenement.preventDefault();
    const retour = evenement.key.toLowerCase() === "y" || evenement.shiftKey;
    etat.historique = retour ? retablir(etat.historique) : annuler(etat.historique);
    memoriserBrouillon();
    rendre();
  });

  page.addEventListener("click", evenement => {
    const cible = evenement.target.closest("button");
    if (!cible || !page.contains(cible)) {
      return;
    }

    if (cible.dataset.outil) {
      etat.outil = cible.dataset.outil;
      etat.mesure = { de: null, a: null };
      el("[data-role=mesure]").hidden = true;
      rendre();
      return;
    }

    if (cible.dataset.cible) {
      etat.cible = cible.dataset.cible;
      rendre();
      return;
    }

    const action = cible.dataset.action;
    if (commandeVue(action)) {
      return;
    }

    const actions = {
      annuler: () => { etat.historique = annuler(etat.historique); memoriserBrouillon(); rendre(); },
      retablir: () => { etat.historique = retablir(etat.historique); memoriserBrouillon(); rendre(); },
      enregistrer: () => enregistrer(null),
      calque: () => basculerCalque(cible),
      "changer-fond": () => { etat.panneau = "fond"; rendre(); },
      recalibrer: () => choisirFond(present().contenu.fond.fondId),
      "fermer-panneau": () => { etat.panneau = "proprietes"; rendre(); },
      "retour-fond": () => { etat.echelleSaisie = null; etat.panneau = "fond"; changer(abandonnerSaisie(present())); ajusterVue(); },
      "valider-calibration": validerEchelle,
      grille: commencerGrille,
      "brouillon-abandonner": abandonnerBrouillon,
      "brouillon-reprendre": () => {
        el("[data-role=brouillon-texte]").textContent =
          `Confirmez : votre brouillon remplacera le contenu de la version ${etat.reprise?.versionCourante} dans la prochaine version enregistrée ; la version ${etat.reprise?.versionCourante} restera dans l'historique, restaurable.`;
        cible.hidden = true;
        el("[data-action=brouillon-confirmer]").hidden = false;
      },
      "brouillon-confirmer": reprendreBrouillon,
    };
    actions[action]?.();
  });

  for (const cible of ["calibration", "controle"]) {
    el(`[data-champ=${cible}]`)?.addEventListener("change", evenement => {
      const texte = evenement.target.value;
      const valeur = lireDistance(texte);
      if (texte.trim() && valeur == null) {
        message("Distance : saisissez un nombre de mètres, par exemple 14,33.", "avertissement");
        return;
      }

      etat.cible = cible;
      changer(fixerDistance(present(), cible, valeur));
    });
  }

  el("[data-role=import]")?.addEventListener("submit", evenement => {
    evenement.preventDefault();
    importer(evenement.target);
  });

  new ResizeObserver(() => {
    const apres = mesurerAffichage();
    if (etat.vue && etat.affichage && (apres.largeur !== etat.affichage.largeur || apres.hauteur !== etat.affichage.hauteur)) {
      etat.vue = redimensionner(etat.vue, etat.affichage, apres);
      etat.affichage = apres;
      planifierVue();
    }
  }).observe(svg);

  // ——— Ouverture ———
  (async () => {
    const local = stockage();
    if (local) {
      purgerAutresComptes(local, d.compte); // tablette partagée : aucun brouillon d'un autre compte ne reste
    }

    if (etat.planId) {
      const lue = await lirePlan();
      charger(lue);
      await lireFonds();
    }

    examinerBrouillon();
    if (etat.mode === "guide" && etat.panneau === "fond") {
      el("[data-role=resume]").textContent = "Quatre étapes ; chaque étape s'enregistre comme une version, vous pouvez vous arrêter et reprendre.";
    }

    rendre();
    ajusterVue();
  })();
}

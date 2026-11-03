// Éditeur de plans de bâtiment (US-21, S21-07, S21-08, ADR-11) : colle DOM mince entre la page Plans (data-*) et le noyau
// pur (plans/noyau/*.mjs, testé par node:test). Aucun style ni script en ligne (CSP), textContent pour tout texte saisi,
// fond servi par l'application web (<image href>, img-src 'self'). Vérifiée dans un navigateur réel (Playwright).

import { ecrireBrouillon, effacerBrouillon, cleBrouillon, lireBrouillon, purgerAutresComptes } from "./noyau/brouillon.mjs";
import {
  bornesFond, defautCote, distanceProposee, ecartControle, formaterEcart, formaterMetres, lireDistance, longueurPixels, messageDefaut,
  metresParPixel, metresParPixelDeclares, pointImage,
} from "./noyau/calibration.mjs";
import {
  accrocher, ajouterSommet, autresNiveaux, avancement, changerZone, commencerTrace, contoursAccrochage, defautEscalier, defautOuverture,
  defautPiece, deplacerElement, deplacerSommet, elementSous, fermeLeTrace, fermerTrace, formaterSurface, glisserOuverture, insererSommet,
  largeurOuverture, libelleType, messageDessin, modifierEscalier, modifierOuverture, nomNiveau, nomZone, poserOuverture, premierDefaut,
  regler, supprimerElement, supprimerSommet, TYPES_OUVERTURE, zonesALaMarge, zonesDessinables, zonesRestantes,
} from "./noyau/dessin.mjs";
import { perimetre } from "./noyau/geometrie.mjs";
import { annuler, appliquer, creerHistorique, peutAnnuler, peutRetablir, retablir } from "./noyau/historique.mjs";
import { distance } from "./noyau/mesures.mjs";
import {
  abandonnerSaisie, appliquerDecalage, bornesAffichees, centre, commencerCalibration, conflit, controleFond, corpsEnregistrement,
  decalageFond, descriptionFond, documentDepuisVersion, echelleFond, estModifie, fixerDistance, formaterDate, messageRefus, pointsSvg,
  poserPoint, resumeVersion, retirerFond, validerCalibration,
} from "./noyau/plan.mjs";
import * as recalage from "./noyau/recalage.mjs";
import { ajuster, deplacer, echelleVue, pasGrille, pincer, redimensionner, versRepere, viewBox, zoomer } from "./noyau/vue.mjs";

const SVG = "http://www.w3.org/2000/svg";
const ECHELLE_PROVISOIRE = 0.01; // fond pas encore calibré : 1 cm par pixel, le temps de poser la cote
const SEUIL_GLISSER = 6; // pixels : en deçà, un appui est un toucher (pose d'un point), pas un glissement
const TOLERANCE_PIXELS = 12; // accrochage et désignation : 12 pixels à l'écran, quel que soit le zoom
const PAS_CLAVIER = 0.1; // déplacement d'un élément au clavier ou par les boutons (10 cm ; 1 cm pour un sommet)

const racine = document.getElementById("editeur-plan");
if (racine) {
  demarrer(racine);
}

function lireJson(texte, defaut) {
  try {
    return JSON.parse(texte || "null") ?? defaut;
  } catch {
    return defaut;
  }
}

function demarrer(racine) {
  const page = racine.closest("main");
  const d = racine.dataset;
  const modifiable = d.modifiable === "true";
  const base = `/organisations/${d.organisation}/sites/${d.site}/plans-batiment`;
  const el = selecteur => page.querySelector(selecteur);
  const tous = selecteur => [...page.querySelectorAll(selecteur)];
  const svg = el("[data-role=svg]");
  const couches = {
    fond: el("[data-role=fond]"), calque: el("[data-role=calque]"), pieces: el("[data-role=pieces]"), selection: el("[data-role=selection]"),
    cotes: el("[data-role=cotes]"),
  };
  const cadre = lireJson(d.cadre, { niveauId: null, zones: [], niveaux: [] });
  const zones = lireJson(d.zones, {});

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
    selection: null,
    apercu: null,
    recalage: null,
    typeOuverture: "Porte",
    liaison: { type: "Escalier", versNiveauId: autresNiveaux(cadre)[0]?.id ?? null },
    echelleSaisie: null,
    vue: null,
    affichage: null,
    calque: { affiche: false, contenu: null },
    mesure: { de: null, a: null },
    reprise: null,
    enCours: false,
    rendus: { pieces: null, fond: null },
  };
  const present = () => etat.historique.present;
  const affiche = () => etat.apercu ?? present();

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
  const fondAffiche = () => affiche().saisie?.fondId ?? affiche().contenu.fond?.fondId ?? null;
  const image = info => ({ largeur: info.largeurAffichage, hauteur: info.hauteurAffichage });

  function echelleTravail() {
    if (present().saisie) {
      return etat.echelleSaisie ?? ECHELLE_PROVISOIRE;
    }

    return echelleFond(affiche().contenu) ?? ECHELLE_PROVISOIRE;
  }

  // Coin haut gauche du fond affiché dans le repère : son recalage, gardé pendant qu'on recalibre le même fond.
  function origineFond() {
    const contenu = affiche().contenu;
    return contenu.fond && contenu.fond.fondId === fondAffiche() ? decalageFond(contenu) : { x: 0, y: 0 };
  }

  function bornesActuelles() {
    const info = fondInfo(fondAffiche());
    const o = origineFond();
    const b = info ? bornesFond(image(info), echelleTravail()) : null;
    return bornesAffichees(affiche().contenu, b ? { minX: b.minX + o.x, minY: b.minY + o.y, maxX: b.maxX + o.x, maxY: b.maxY + o.y } : null);
  }

  const tolerance = () => TOLERANCE_PIXELS * (etat.vue ? echelleVue(etat.vue, etat.affichage) : 0.05);

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
    for (const couche of [couches.pieces, couches.cotes, couches.selection]) {
      couche.setAttribute("font-size", String(13 * metres));
    }

    for (const point of [...couches.cotes.querySelectorAll("circle"), ...couches.selection.querySelectorAll("circle")]) {
      point.setAttribute("r", String((point.classList.contains("actif") || point.classList.contains("premier") ? 10 : 7) * metres));
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

  function texteSvg(parent, point, contenu, classe) {
    const t = creer("text", { x: point.x, y: point.y, ...(classe ? { class: classe } : {}) }, parent);
    t.textContent = contenu;
    return t;
  }

  function memeSelection(genre, index) {
    return etat.selection?.genre === genre && etat.selection.index === index;
  }

  function rendreGeometrie(groupe, contenu, avecNoms) {
    groupe.replaceChildren();
    contenu.pieces.forEach((piece, index) => {
      const classes = ["piece", memeSelection("piece", index) ? "selectionnee" : ""].filter(Boolean).join(" ");
      creer("polygon", { points: pointsSvg(piece.contour), class: classes }, groupe);
      if (avecNoms) {
        texteSvg(groupe, centre(piece.contour), zones[piece.zoneId] ?? nomZone(cadre, piece.zoneId));
      }
    });

    contenu.escaliers.forEach((escalier, index) => {
      creer("polygon", { points: pointsSvg(escalier.contour), class: `escalier${memeSelection("escalier", index) ? " selectionnee" : ""}` }, groupe);
      if (avecNoms) {
        texteSvg(groupe, centre(escalier.contour), `${libelleType(escalier.type)} → ${nomNiveau(cadre, escalier.versNiveauId)}`, "etiquette-liaison");
      }
    });

    if (!avecNoms) {
      return;
    }

    contenu.ouvertures.forEach((ouverture, index) => {
      creer("line", {
        x1: ouverture.de.x, y1: ouverture.de.y, x2: ouverture.a.x, y2: ouverture.a.y,
        class: `ouverture ${ouverture.type.toLowerCase()}${memeSelection("ouverture", index) ? " selectionnee" : ""}`,
      }, groupe);
    });

    for (const marqueur of contenu.composants) {
      creer("circle", { cx: marqueur.position.x, cy: marqueur.position.y, r: 0.25, class: "composant" }, groupe);
    }
  }

  // Poignées de la sélection (sommets), tracé en cours et points du recalage.
  function rendreSelection() {
    const groupe = couches.selection;
    groupe.replaceChildren();
    const contenu = affiche().contenu;
    const s = etat.selection;
    if (s && s.genre !== "ouverture") {
      const element = contenu[s.genre === "piece" ? "pieces" : "escaliers"][s.index];
      element?.contour.forEach((p, sommet) => creer("circle", { cx: p.x, cy: p.y, r: 0.1, class: `sommet${s.sommet === sommet ? " actif" : ""}` }, groupe));
    }

    const trace = affiche().trace;
    if (trace && trace.points.length > 0) {
      creer("polyline", { points: pointsSvg(trace.points), class: "trace" }, groupe);
      trace.points.forEach((p, rang) => creer("circle", { cx: p.x, cy: p.y, r: 0.1, class: `sommet${rang === 0 ? " premier" : ""}` }, groupe));
    }

    if (etat.recalage) {
      const o = origineFond();
      etat.recalage.points.forEach((p, rang) => {
        const surCalque = rang % 2 === 0;
        const point = surCalque ? p : { x: p.x + o.x, y: p.y + o.y };
        creer("circle", { cx: point.x, cy: point.y, r: 0.1, class: surCalque ? "recalage-calque" : "recalage-fond" }, groupe);
        texteSvg(groupe, { x: point.x, y: point.y }, String(Math.floor(rang / 2) + 1), "numero");
      });
    }
  }

  function rendreFond() {
    const id = fondAffiche();
    const info = fondInfo(id);
    const metres = echelleTravail();
    const o = origineFond();
    const cleRendu = info ? `${id}:${metres}:${o.x}:${o.y}` : null;
    if (etat.rendus.fond === cleRendu) {
      return;
    }

    etat.rendus.fond = cleRendu;
    couches.fond.replaceChildren();
    el("[data-role=fond-texte]").textContent = info ? `Fond : ${descriptionFond(info)}` : id ? "Fond en cours de chargement" : "Plan sur grille métrique";
    if (info) {
      creer("image", {
        href: `${base}/${etat.planId}/fonds/${id}`, x: o.x, y: o.y, width: info.largeurAffichage * metres, height: info.hauteurAffichage * metres,
        preserveAspectRatio: "none",
      }, couches.fond);
    }
  }

  function segment(de, a, classe, texte) {
    creer("line", { x1: de.x, y1: de.y, x2: a.x, y2: a.y, class: classe }, couches.cotes);
    if (texte) {
      texteSvg(couches.cotes, { x: (de.x + a.x) / 2, y: (de.y + a.y) / 2 }, texte);
    }
  }

  // Point de l'image (pixels) dans le repère, et inversement, à l'échelle de travail et à l'origine du fond.
  const pixelVersPlan = (point, metres) => ({ x: point.x * metres + origineFond().x, y: point.y * metres + origineFond().y });
  const planVersPixel = (point, metres) => ({ x: (point.x - origineFond().x) / metres, y: (point.y - origineFond().y) / metres });

  function rendreCotes() {
    couches.cotes.replaceChildren();
    const saisie = present().saisie;
    const metres = echelleTravail();
    const points = [];
    if (saisie) {
      for (const cible of ["calibration", "controle"]) {
        const cote = saisie[cible];
        const de = cote.de ? pixelVersPlan(cote.de, metres) : null;
        const a = cote.a ? pixelVersPlan(cote.a, metres) : null;
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
    const decalage = decalageFond(contenu);
    el("[data-role=prop-recalage]").textContent = !contenu.fond
      ? "—"
      : contenu.fond.decalage ? `Fond décalé de ${formaterMetres(decalage.x)} ; ${formaterMetres(decalage.y)}` : "Aucun (fond à l'origine)";
    const recalibrer = el("[data-action=recalibrer]");
    if (recalibrer) {
      recalibrer.hidden = !contenu.fond;
    }

    const recaler = el("[data-action=recaler]");
    if (recaler) {
      recaler.hidden = !contenu.fond;
    }

    el("[data-role=pieces-compte]").textContent =
      `${contenu.pieces.length} pièce${contenu.pieces.length > 1 ? "s" : ""}, ${contenu.ouvertures.length} ouverture${contenu.ouvertures.length > 1 ? "s" : ""}, ${contenu.escaliers.length} escalier${contenu.escaliers.length > 1 ? "s" : ""} ou ascenseur${contenu.escaliers.length > 1 ? "s" : ""}`;
    if (etat.mode === "atelier") {
      el("[data-role=resume]").textContent = estModifie(present(), etat.reference)
        ? `${resumeVersion(etat.lue, etat.reference.contenu)} · modifications non enregistrées`
        : resumeVersion(etat.lue, etat.reference.contenu);
    }
  }

  // Liste déroulante remplie par textContent (noms saisis par les utilisateurs).
  function remplirListe(liste, options, valeur) {
    liste.replaceChildren(...options.map(([cleOption, libelle, desactivee]) => {
      const option = document.createElement("option");
      option.value = cleOption;
      option.textContent = libelle;
      option.disabled = Boolean(desactivee);
      return option;
    }));
    liste.value = valeur ?? "";
  }

  function optionsZones(zoneActuelle) {
    const dessinees = new Set(present().contenu.pieces.map(p => p.zoneId));
    return [["", "Choisir une zone…", true], ...zonesDessinables(cadre).map(z => [z.id, dessinees.has(z.id) && z.id !== zoneActuelle ? `${z.nom} (déjà tracée)` : z.nom, dessinees.has(z.id) && z.id !== zoneActuelle])];
  }

  const optionsNiveaux = () => [["", "Choisir le niveau…", true], ...autresNiveaux(cadre).map(n => [n.id, n.nom])];
  const optionsLiaison = () => [["Escalier", "Escalier"], ["Ascenseur", "Ascenseur"]];

  function defautAffiche(zone, defaut) {
    zone.textContent = defaut ? messageDessin(defaut, cadre) : "";
    zone.hidden = !defaut;
  }

  function rendrePanneauTrace() {
    const trace = present().trace;
    const genre = trace?.genre ?? (etat.outil === "escalier" ? "escalier" : "piece");
    el("[data-role=trace-titre]").textContent = genre === "piece" ? "Nouvelle pièce" : "Nouvel escalier ou ascenseur";
    el("[data-role=trace-piece]").hidden = genre !== "piece";
    el("[data-role=trace-escalier]").hidden = genre !== "escalier";
    if (genre === "piece") {
      const zoneTrace = trace?.zoneId ?? zonesRestantes(present().contenu, cadre)[0]?.id ?? "";
      remplirListe(el("[data-champ=trace-zone]"), optionsZones(null), zoneTrace);
    } else {
      remplirListe(el("[data-champ=trace-type]"), optionsLiaison(), trace?.type ?? etat.liaison.type);
      remplirListe(el("[data-champ=trace-niveau]"), optionsNiveaux(), trace?.versNiveauId ?? etat.liaison.versNiveauId);
    }

    const sommets = trace?.points.length ?? 0;
    el("[data-role=trace-consigne]").textContent = sommets === 0
      ? `Touchez les angles ${genre === "piece" ? "de la pièce" : "de la cage"} un à un sur le plan (faces intérieures des murs).`
      : sommets < 3
        ? `${sommets} sommet${sommets > 1 ? "s" : ""} : touchez le suivant.`
        : `${sommets} sommets : touchez le premier sommet ou « Fermer ${genre === "piece" ? "la pièce" : "le contour"} ».`;
    const restantes = zonesRestantes(present().contenu, cadre).length;
    el("[data-role=trace-toutes]").hidden = genre !== "piece" || restantes > 0;
  }

  function rendrePanneauPiece(s) {
    const contenu = affiche().contenu;
    const piece = contenu.pieces[s.index];
    el("[data-role=piece-nom]").textContent = nomZone(cadre, piece.zoneId);
    remplirListe(el("[data-champ=piece-zone]"), optionsZones(piece.zoneId), piece.zoneId);
    el("[data-role=piece-surface]").textContent = formaterSurface(piece.contour);
    el("[data-role=piece-perimetre]").textContent = formaterMetres(perimetre(piece.contour));
    el("[data-role=piece-sommets]").textContent = String(piece.contour.length);
    defautAffiche(el("[data-role=piece-defaut]"), defautPiece(contenu, cadre, s.index));
    rendreSommet("piece", piece.contour, s);
  }

  function rendreSommet(prefixe, contour, s) {
    const zone = el(`[data-role=${prefixe}-sommet]`);
    zone.hidden = s.sommet == null;
    if (s.sommet == null) {
      return;
    }

    const p = contour[s.sommet];
    el(`[data-role=${prefixe}-sommet-titre]`).textContent = `Sommet ${s.sommet + 1} sur ${contour.length}`;
    for (const axe of ["x", "y"]) {
      const champ = el(`[data-champ=${prefixe}-${axe}]`);
      if (document.activeElement !== champ) {
        champ.value = String(p[axe]).replace(".", ",");
      }
    }

    el(`[data-role=${prefixe}-sommet] [data-action=supprimer-sommet]`).disabled = contour.length <= 3;
  }

  function rendrePanneauOuverture(s) {
    const contenu = affiche().contenu;
    const o = contenu.ouvertures[s.index];
    el("[data-role=ouverture-nom]").textContent = `${libelleType(o.type)} · ${o.zones.map(z => nomZone(cadre, z)).join(" ↔ ")}${o.zones.length === 1 ? " ↔ extérieur" : ""}`;
    remplirListe(el("[data-champ=ouverture-type]"), TYPES_OUVERTURE.map(t => [t, libelleType(t)]), o.type);
    const champ = el("[data-champ=ouverture-largeur]");
    if (document.activeElement !== champ) {
      champ.value = String(largeurOuverture(o)).replace(".", ",");
    }

    const autres = contenu.pieces.filter(p => p.zoneId !== o.zones[0]).map(p => [p.zoneId, nomZone(cadre, p.zoneId)]);
    remplirListe(el("[data-champ=ouverture-face]"), [["", "Extérieur"], ...autres], o.zones[1] ?? "");
    defautAffiche(el("[data-role=ouverture-defaut]"), defautOuverture(contenu, s.index));
  }

  function rendrePanneauEscalier(s) {
    const contenu = affiche().contenu;
    const e = contenu.escaliers[s.index];
    el("[data-role=escalier-nom]").textContent = `${libelleType(e.type)} vers ${nomNiveau(cadre, e.versNiveauId)}`;
    remplirListe(el("[data-champ=escalier-type]"), optionsLiaison(), e.type);
    remplirListe(el("[data-champ=escalier-niveau]"), optionsNiveaux(), e.versNiveauId);
    el("[data-role=escalier-surface]").textContent = formaterSurface(e.contour);
    defautAffiche(el("[data-role=escalier-defaut]"), defautEscalier(contenu, cadre, s.index));
    rendreSommet("escalier", e.contour, s);
  }

  function rendrePanneauRecalage() {
    const r = etat.recalage;
    if (!r) {
      return;
    }

    el("[data-role=recalage-consigne]").textContent = recalage.consigne(r);
    const resultat = el("[data-role=recalage-resultat]");
    const calcul = recalage.etape(r) ? null : recalage.calculer(r);
    resultat.hidden = !calcul;
    if (calcul) {
      resultat.textContent = calcul.defaut
        ? recalage.messageDefaut(calcul.defaut)
        : `Fond décalé de ${formaterMetres(calcul.decalage.x)} ; ${formaterMetres(calcul.decalage.y)} · écart des distances ${formaterEcart(calcul.ecart)}${calcul.avertissement ? " : au-delà de 2 %, les points ne se correspondent pas bien (ou l'échelle diffère) ; vérifiez-les." : " ✓"}`;
      resultat.className = `alerte ${calcul.defaut || calcul.avertissement ? "avertissement" : "succes"}`;
    }

    el("[data-action=valider-recalage]").disabled = !calcul || Boolean(calcul.defaut);
  }

  function rendrePanneauGuide() {
    const contenu = present().contenu;
    const { total, faites } = avancement(contenu, cadre);
    const dessinees = new Set(contenu.pieces.map(p => p.zoneId));
    const courante = present().trace?.zoneId;
    const liste = el("[data-role=guide-zones]");
    liste.replaceChildren(...zonesDessinables(cadre).map(z => {
      const item = document.createElement("li");
      item.textContent = `${dessinees.has(z.id) ? "✓ " : z.id === courante ? "→ " : ""}${z.nom}`;
      item.className = dessinees.has(z.id) ? "faite" : z.id === courante ? "courante" : "";
      return item;
    }));
    el("[data-etape=pieces]").textContent = `3 · Pièces (${faites} sur ${total} zone${total > 1 ? "s" : ""})`;
    const sommets = present().trace?.points.length ?? 0;
    el("[data-role=guide-consigne]").textContent = courante
      ? `${nomZone(cadre, courante)} : touchez les angles de la pièce un à un (${sommets} sommet${sommets > 1 ? "s" : ""}), puis « Fermer la pièce ».`
      : "Toutes les zones du niveau ont leur contour : terminez l'étape.";
    el("[data-action=zone-suivante]").disabled = !courante;
  }

  function panneauCourant() {
    if (etat.panneau !== "proprietes") {
      return etat.panneau;
    }

    if (present().trace || etat.outil === "piece" || etat.outil === "escalier") {
      return "trace";
    }

    return etat.selection ? etat.selection.genre : "proprietes";
  }

  function rendrePanneaux() {
    const panneau = panneauCourant();
    for (const zone of tous("[data-panneau]")) {
      zone.hidden = zone.dataset.panneau !== panneau;
    }

    const rendus = {
      trace: rendrePanneauTrace, piece: () => rendrePanneauPiece(etat.selection), ouverture: () => rendrePanneauOuverture(etat.selection),
      escalier: () => rendrePanneauEscalier(etat.selection), recalage: rendrePanneauRecalage, pieces: rendrePanneauGuide,
    };
    if (modifiable || panneau === "proprietes") {
      rendus[panneau]?.();
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
      enregistrer.disabled = etat.enCours || etat.chargementEchoue || Boolean(present().saisie) || Boolean(present().trace)
        || !estModifie(present(), etat.reference) || Boolean(etat.reprise);
    }

    for (const bouton of tous("[data-outil]")) {
      bouton.setAttribute("aria-pressed", String(bouton.dataset.outil === etat.outil));
    }

    const fermer = el("[data-action=fermer-trace]");
    if (fermer) {
      const trace = present().trace;
      fermer.hidden = !trace;
      fermer.disabled = !trace || trace.points.length < 3;
      fermer.textContent = trace?.genre === "escalier" ? "Fermer le contour" : "Fermer la pièce";
    }

    svg.classList.toggle("pointage", etat.panneau === "calibration" || etat.panneau === "recalage" || etat.panneau === "pieces" || etat.outil !== "selection");
    const etapes = tous("[data-etape]");
    const courante = { fond: 0, calibration: 1, pieces: 2 }[etat.panneau] ?? 0;
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
    const contenu = affiche().contenu;
    const cleRendu = `${etat.selection?.genre}:${etat.selection?.index}`;
    if (etat.rendus.pieces !== contenu || etat.rendus.selection !== cleRendu) {
      etat.rendus.pieces = contenu;
      etat.rendus.selection = cleRendu;
      rendreGeometrie(couches.pieces, contenu, true);
    }

    rendreFond();
    rendreCotes();
    rendreSelection();
    rendrePanneauCalibration();
    rendreProprietes();
    rendrePanneaux();
    rendreCommandes();
    planifierVue();
  }

  // ——— Historique et brouillon ———
  let ecriturePlanifiee = null;
  function ecrireBrouillonMaintenant() {
    clearTimeout(ecriturePlanifiee);
    const cleCourante = cle();
    const local = stockage();
    // Pas d'écriture tant qu'une reprise est proposée (le brouillon en attente reste tel quel), ni si le plan n'a pas pu
    // être lu (la version lue serait fausse et le brouillon perdu).
    if (!modifiable || !cleCourante || !local || etat.reprise || etat.chargementEchoue) {
      return;
    }

    if (estModifie(present(), etat.reference) || present().saisie || present().trace) {
      ecrireBrouillon(local, {
        compteId: d.compte, organisationId: d.organisation, planId: etat.planId, versionLue: etat.versionLue, document: present(),
        modifieLe: new Date().toISOString(),
      });
    } else {
      effacerBrouillon(local, cleCourante);
    }
  }

  function memoriserBrouillon() {
    clearTimeout(ecriturePlanifiee);
    ecriturePlanifiee = setTimeout(ecrireBrouillonMaintenant, 300);
  }

  // Aucune modification pendant un enregistrement : elle serait effacée par le rechargement de la version enregistrée.
  function changer(document) {
    if (etat.enCours || etat.chargementEchoue || !modifiable) {
      return;
    }

    etat.historique = appliquer(etat.historique, document);
    memoriserBrouillon();
    rendre();
  }

  // Sélection encore valable après une modification (élément supprimé : plus de sélection).
  function verifierSelection() {
    const s = etat.selection;
    const listeSel = s ? present().contenu[{ piece: "pieces", ouverture: "ouvertures", escalier: "escaliers" }[s.genre]] : null;
    if (s && (!listeSel || s.index >= listeSel.length || (s.sommet != null && s.sommet >= (listeSel[s.index].contour?.length ?? 0)))) {
      etat.selection = null;
    }
  }

  // Annuler, rétablir : le panneau et l'échelle de travail suivent la calibration en cours du document.
  function deplacerHistorique(retour) {
    if (etat.enCours || etat.chargementEchoue) {
      return;
    }

    etat.historique = retour ? retablir(etat.historique) : annuler(etat.historique);
    const saisie = present().saisie;
    if (saisie && etat.panneau !== "calibration") {
      etat.echelleSaisie = echelleInitiale(saisie.fondId, present());
      etat.panneau = "calibration";
    } else if (!saisie && etat.panneau === "calibration") {
      etat.echelleSaisie = null;
      etat.panneau = etat.mode === "guide" ? "fond" : "proprietes";
    }

    verifierSelection();
    memoriserBrouillon();
    rendre();
  }

  function charger(lue) {
    etat.lue = lue;
    etat.versionLue = lue?.version ?? 0;
    etat.reference = documentDepuisVersion(lue);
    etat.historique = creerHistorique(etat.reference);
    etat.selection = null;
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
    if (!modifiable || !local || !cleCourante || etat.chargementEchoue) {
      return;
    }

    const brouillon = lireBrouillon(local, cleCourante);
    const document = brouillon ? { trace: null, ...brouillon.document } : null;
    if (!brouillon || brouillon.versionLue > etat.versionLue || !estModifie(document, etat.reference) && !document.saisie && !document.trace) {
      effacerBrouillon(local, cleCourante);
      return;
    }

    const quand = formaterDate(brouillon.modifieLe);
    if (brouillon.versionLue === etat.versionLue) {
      etat.historique = appliquer(etat.historique, document);
      if (document.saisie) {
        etat.echelleSaisie = echelleInitiale(document.saisie.fondId, document);
        etat.panneau = "calibration";
      } else if (document.trace) {
        etat.outil = document.trace.genre;
      }

      afficherBrouillon(`Brouillon non enregistré repris (modifié le ${quand}).`, { reprendre: false });
      return;
    }

    etat.reprise = { document, versionCourante: etat.versionLue };
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

  // Défaut détecté par l'éditeur avant l'envoi : l'élément fautif est sélectionné et son défaut expliqué.
  function refuserDefaut() {
    const defaut = premierDefaut(present().contenu, cadre);
    if (!defaut) {
      return false;
    }

    etat.outil = "selection";
    etat.panneau = etat.mode === "guide" ? etat.panneau : "proprietes";
    etat.selection = defaut.genre === "plan" ? null : { genre: defaut.genre, index: defaut.index };
    message(`À corriger avant d'enregistrer : ${messageDessin(defaut.defaut, cadre)}`, "danger");
    rendre();
    return true;
  }

  async function enregistrer(motif) {
    if (refuserDefaut()) {
      return false;
    }

    etat.enCours = true;
    rendreCommandes();
    const corps = corpsEnregistrement(present(), etat.versionLue, crypto.randomUUID(), motif);
    const reponse = await appeler("PUT", `${base}/${etat.planId}`, corps);
    if (reponse.statut === 200) {
      const local = stockage();
      if (local) {
        effacerBrouillon(local, cle());
      }

      const enregistre = present();
      const lue = await lirePlan();
      if (lue) {
        charger(lue);
      } else {
        // Relecture impossible : la version enregistrée devient la référence, pour ne pas renvoyer une version dépassée.
        etat.versionLue = reponse.donnees.version;
        etat.lue = { ...etat.lue, version: reponse.donnees.version, le: null, auteur: null };
        etat.reference = { contenu: enregistre.contenu, saisie: null, trace: null };
        etat.historique = creerHistorique(etat.reference);
      }

      etat.enCours = false;
      masquerBrouillon();
      message(`Version ${reponse.donnees.version} enregistrée.`);
      rendre();
      return true;
    }

    etat.enCours = false;
    ecrireBrouillonMaintenant(); // conservé avant que la reprise ne soit proposée
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
    etat.selection = null;
    changer(commencerCalibration(present(), fondId));
    ajusterVue();
  }

  function selectionner(selection) {
    etat.selection = selection;
    rendre();
  }

  // Tracé de pièce ou de liaison : premier toucher, sommets accrochés, fermeture sur le premier sommet.
  function toucherTrace(point) {
    let document = present();
    if (!document.trace) {
      const genre = etat.outil === "escalier" ? "escalier" : "piece";
      const options = genre === "piece"
        ? { zoneId: el("[data-champ=trace-zone]")?.value || zonesRestantes(document.contenu, cadre)[0]?.id || null }
        : { ...etat.liaison };
      document = commencerTrace(document, genre, options);
    }

    const contours = [...contoursAccrochage(document.contenu), document.trace.points, ...(etat.calque.affiche ? contoursAccrochage(etat.calque.contenu) : [])];
    const { point: accroche } = accrocher(point, contours, tolerance());
    if (fermeLeTrace(document.trace, accroche, tolerance())) {
      changer(document);
      fermer();
      return;
    }

    changer(ajouterSommet(document, accroche));
  }

  function fermer() {
    const resultat = fermerTrace(present(), cadre);
    if (resultat.defaut) {
      message(messageDessin(resultat.defaut, cadre), "avertissement");
      rendre();
      return;
    }

    message("");
    const genre = present().trace.genre;
    changer(resultat.document);
    if (etat.panneau === "pieces") {
      // Parcours guidé : la zone suivante se trace aussitôt.
      const suivante = zonesRestantes(present().contenu, cadre)[0];
      if (suivante) {
        changer(commencerTrace(present(), "piece", { zoneId: suivante.id }));
      }

      return;
    }

    etat.outil = "selection";
    selectionner({ genre, index: resultat.index });
  }

  function toucher(position) {
    const point = versRepere(etat.vue, etat.affichage, position);
    const saisie = present().saisie;
    if (etat.panneau === "calibration" && saisie) {
      const info = fondInfo(saisie.fondId);
      const pixel = info ? pointImage(planVersPixel(point, etat.echelleSaisie), image(info)) : null;
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

    if (etat.panneau === "recalage" && etat.recalage) {
      toucherRecalage(point);
      return;
    }

    if (etat.outil === "cote") {
      etat.mesure = !etat.mesure.de || etat.mesure.a ? { de: point, a: null } : { ...etat.mesure, a: point };
      const sortie = el("[data-role=mesure]");
      sortie.hidden = !etat.mesure.a;
      sortie.textContent = etat.mesure.a ? `Cote mesurée : ${formaterMetres(distance(etat.mesure.de, etat.mesure.a))}` : "";
      rendreCotes();
      planifierVue();
      return;
    }

    if (!modifiable) {
      return;
    }

    if (etat.panneau === "pieces" || etat.outil === "piece" || etat.outil === "escalier") {
      if (etat.panneau !== "pieces" || present().trace) {
        toucherTrace(point);
      }

      return;
    }

    if (etat.outil === "ouverture") {
      const resultat = poserOuverture(present(), etat.typeOuverture, point, tolerance());
      if (resultat.defaut) {
        message(messageDessin(resultat.defaut, cadre), "avertissement");
        return;
      }

      message("");
      changer(resultat.document);
      selectionner({ genre: "ouverture", index: resultat.index });
      return;
    }

    selectionner(elementSous(present().contenu, point, tolerance(), etat.selection));
  }

  // Recalage : points du calque accrochés à ses contours, points du fond exprimés depuis son coin haut gauche.
  function toucherRecalage(point) {
    const r = etat.recalage;
    if (!recalage.etape(r)) {
      return;
    }

    if (recalage.surCalque(r)) {
      const { point: accroche } = accrocher(point, contoursAccrochage(etat.calque.contenu), tolerance());
      etat.recalage = recalage.poserPoint(r, accroche);
    } else {
      const o = origineFond();
      etat.recalage = recalage.poserPoint(r, { x: point.x - o.x, y: point.y - o.y });
    }

    rendre();
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
      // Parcours guidé : chaque étape s'enregistre comme une version (maquette C), puis les pièces zone par zone.
      if (await enregistrer("Calibration du fond")) {
        commencerEtapePieces();
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
        commencerEtapePieces();
      }

      return;
    }

    etat.panneau = "proprietes";
    changer(retirerFond(present()));
    ajusterVue();
  }

  // Étape 3 du parcours guidé : les zones du niveau tracées une à une ; l'étape 4 (composants) arrive avec S21-09.
  function commencerEtapePieces() {
    etat.panneau = "pieces";
    etat.outil = "piece";
    const suivante = zonesRestantes(present().contenu, cadre)[0];
    if (suivante) {
      etat.historique = appliquer(etat.historique, commencerTrace(present(), "piece", { zoneId: suivante.id }));
    }

    rendre();
    ajusterVue();
  }

  function zoneSuivante() {
    const restantes = zonesRestantes(present().contenu, cadre);
    const courante = present().trace?.zoneId;
    const rang = restantes.findIndex(z => z.id === courante);
    const suivante = restantes[(rang + 1) % restantes.length];
    if (suivante && suivante.id !== courante) {
      changer(commencerTrace(present(), "piece", { zoneId: suivante.id }));
    }
  }

  async function terminerPieces() {
    changer({ ...present(), trace: null });
    if (!estModifie(present(), etat.reference) || (await enregistrer("Pièces tracées"))) {
      passerEnAtelier();
    }
  }

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

  async function chargerCalque() {
    if (etat.calque.contenu) {
      return true;
    }

    const reponse = await appeler("GET", `${base}/${d.planInferieur}`);
    if (reponse.statut !== 200) {
      message(messageRefus(reponse.statut, reponse.probleme), "danger");
      return false;
    }

    etat.calque.contenu = documentDepuisVersion(reponse.donnees).contenu;
    rendreGeometrie(couches.calque, etat.calque.contenu, false);
    return true;
  }

  function afficherCalque(affiche) {
    etat.calque.affiche = affiche;
    couches.calque.toggleAttribute("hidden", !affiche); // élément SVG : pas de propriété hidden
    const bouton = el("[data-action=calque]");
    bouton?.setAttribute("aria-pressed", String(affiche));
    if (bouton) {
      bouton.textContent = `Calque ${d.niveauInferieur} : ${affiche ? "affiché" : "masqué"}`;
    }
  }

  async function basculerCalque() {
    if (!etat.calque.affiche && !(await chargerCalque())) {
      return;
    }

    afficherCalque(!etat.calque.affiche);
  }

  async function commencerRecalage() {
    if (!d.planInferieur || !(await chargerCalque())) {
      message(`Recalage impossible : le niveau inférieur n'a pas encore de plan enregistré.`, "avertissement");
      return;
    }

    if (etat.calque.contenu.pieces.length + etat.calque.contenu.escaliers.length === 0) {
      message(`Le plan du niveau ${d.niveauInferieur} n'a encore ni pièce ni escalier : rien à quoi recaler ce fond.`, "avertissement");
      return;
    }

    afficherCalque(true);
    etat.recalage = recalage.creerRecalage();
    etat.retourRecalage = etat.panneau;
    etat.panneau = "recalage";
    etat.selection = null;
    message("");
    rendre();
  }

  function finirRecalage() {
    etat.recalage = null;
    etat.panneau = etat.retourRecalage === "pieces" ? "pieces" : "proprietes";
    rendre();
  }

  function validerRecalage() {
    const calcul = recalage.calculer(etat.recalage);
    if (calcul.defaut) {
      return;
    }

    finirRecalage();
    changer(appliquerDecalage(present(), calcul.decalage));
    ajusterVue();
    message(etat.mode === "guide"
      ? "Fond recalé sur le niveau inférieur : il sera enregistré avec les pièces."
      : "Fond recalé sur le niveau inférieur : enregistrez la version pour le conserver.", calcul.avertissement ? "avertissement" : "succes");
  }

  async function creerZone(formulaire) {
    const nom = formulaire.elements.nom.value.trim();
    if (!nom) {
      message("Saisissez le nom de la nouvelle zone.", "avertissement");
      return;
    }

    const reponse = await appeler("POST", `${base}/zones`, { nom, batimentId: d.batiment, niveauId: d.niveau || null });
    if (reponse.statut !== 200) {
      message(messageRefus(reponse.statut, reponse.probleme), "danger");
      return;
    }

    const zone = { id: reponse.donnees.id, nom, niveauId: d.niveau || null, partiesCommunes: false };
    cadre.zones.push(zone);
    zones[zone.id] = nom;
    formulaire.reset();
    message(`Zone « ${nom} » ajoutée à l'inventaire : tracez maintenant son contour.`);
    changer(present().trace ? regler(present(), { zoneId: zone.id }) : commencerTrace(present(), "piece", { zoneId: zone.id }));
  }

  async function reprendreBrouillon() {
    const reprise = etat.reprise;
    const lue = await lirePlan();
    if (!reprise || !lue) {
      return;
    }

    charger(lue);
    etat.historique = appliquer(etat.historique, { ...reprise.document, saisie: null, trace: null });
    etat.echelleSaisie = null;
    etat.panneau = "proprietes";
    masquerBrouillon();
    if (etat.mode === "guide") {
      // Parcours guidé (sans bouton « Enregistrer ») : la reprise confirmée s'enregistre aussitôt, comme une étape.
      if (await enregistrer("Reprise du brouillon")) {
        passerEnAtelier();
      }

      return;
    }

    ecrireBrouillonMaintenant();
    message(`Brouillon repris sur la version ${etat.versionLue} : enregistrez pour en faire la version ${etat.versionLue + 1} ; la version ${etat.versionLue} reste dans l'historique.`, "avertissement");
    rendre();
    ajusterVue();
  }

  async function abandonnerBrouillon() {
    const lue = etat.planId ? await lirePlan() : null;
    if (etat.planId && !lue) {
      return; // plan illisible (réseau, session) : le brouillon reste, le message d'erreur est affiché
    }

    const local = stockage();
    if (local && cle()) {
      effacerBrouillon(local, cle());
    }

    etat.chargementEchoue = false;
    charger(lue);
    etat.echelleSaisie = null;
    etat.panneau = etat.mode === "guide" ? "fond" : "proprietes";
    masquerBrouillon();
    message("Brouillon abandonné.");
    rendre();
    ajusterVue();
  }

  // ——— Modifications de l'élément sélectionné (boutons, champs, clavier) ———
  function modifierSelection(fabrique) {
    if (!etat.selection) {
      return;
    }

    changer(fabrique(present(), etat.selection));
    verifierSelection();
    rendre();
  }

  function deplacerSelection(dx, dy) {
    const s = etat.selection;
    if (!s) {
      return;
    }

    if (s.sommet != null) {
      const contour = present().contenu[s.genre === "piece" ? "pieces" : "escaliers"][s.index].contour;
      const p = contour[s.sommet];
      modifierSelection(doc => deplacerSommet(doc, s, s.sommet, { x: p.x + dx, y: p.y + dy }));
    } else {
      modifierSelection(doc => deplacerElement(doc, s, dx, dy));
    }
  }

  function supprimerSelection() {
    const s = etat.selection;
    if (!s) {
      return;
    }

    if (s.sommet != null) {
      modifierSelection(doc => supprimerSommet(doc, s, s.sommet));
      etat.selection = { genre: s.genre, index: s.index };
    } else {
      changer(supprimerElement(present(), s));
      etat.selection = null;
    }

    rendre();
  }

  // ——— Gestes : un doigt glisse la vue (ou pose un point s'il ne bouge pas), deux doigts zooment ; avec la sélection,
  // glisser un sommet ou l'élément sélectionné le déplace (alternatives sans glisser : boutons et clavier) ———
  const pointeurs = new Map();
  let geste = null;
  const position = evenement => {
    const rect = svg.getBoundingClientRect();
    return { x: evenement.clientX - rect.left, y: evenement.clientY - rect.top };
  };

  function cibleGlisser(ecran) {
    if (!modifiable || etat.outil !== "selection" || etat.panneau !== "proprietes" || !etat.selection || !etat.vue) {
      return null;
    }

    const point = versRepere(etat.vue, etat.affichage, ecran);
    const sous = elementSous(present().contenu, point, tolerance(), etat.selection);
    if (sous && sous.genre === etat.selection.genre && sous.index === etat.selection.index) {
      return { ...sous, depart: point };
    }

    return null;
  }

  svg.addEventListener("pointerdown", evenement => {
    if (evenement.pointerType === "mouse" && evenement.button !== 0) {
      return; // clic droit ou du milieu : ni glissement ni pose de point
    }

    svg.setPointerCapture?.(evenement.pointerId);
    pointeurs.set(evenement.pointerId, position(evenement));
    if (pointeurs.size === 1) {
      geste = { type: "glisser", depart: position(evenement), dernier: position(evenement), deplace: false, element: cibleGlisser(position(evenement)) };
    } else if (pointeurs.size === 2) {
      etat.apercu = null;
      geste = { type: "pincer", avant: [...pointeurs.values()] };
    }
  });

  function apercuGlisser(element, ecran) {
    const point = versRepere(etat.vue, etat.affichage, ecran);
    if (element.sommet != null) {
      const contours = [...contoursAccrochage(present().contenu, element), ...(etat.calque.affiche ? contoursAccrochage(etat.calque.contenu) : [])];
      return deplacerSommet(present(), element, element.sommet, accrocher(point, contours, tolerance()).point);
    }

    if (element.genre === "ouverture") {
      const o = present().contenu.ouvertures[element.index];
      const l = Math.hypot(o.a.x - o.de.x, o.a.y - o.de.y) || 1;
      return glisserOuverture(present(), element.index, ((point.x - element.depart.x) * (o.a.x - o.de.x) + (point.y - element.depart.y) * (o.a.y - o.de.y)) / l);
    }

    return deplacerElement(present(), element, point.x - element.depart.x, point.y - element.depart.y);
  }

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

      if (geste.deplace && geste.element) {
        etat.apercu = apercuGlisser(geste.element, p);
        rendre();
      } else if (geste.deplace) {
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
    if (geste?.type === "glisser" && geste.element && etat.apercu) {
      const apercu = etat.apercu;
      etat.apercu = null;
      if (!annule) {
        changer(apercu); // une seule étape d'historique pour tout le glissement
      }

      rendre();
    } else if (geste?.type === "glisser" && !geste.deplace && !annule && pointeurs.size === 0 && etat.vue) {
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

  const fleches = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
  svg.addEventListener("keydown", evenement => {
    // Élément sélectionné : les flèches le déplacent (un sommet de 1 cm, un élément de 10 cm ; Maj : dix fois plus),
    // Suppr l'efface, Échap le désélectionne ou abandonne le tracé.
    if (modifiable && etat.selection && fleches[evenement.key]) {
      const pas = (etat.selection.sommet != null ? 0.01 : PAS_CLAVIER) * (evenement.shiftKey ? 10 : 1);
      deplacerSelection(fleches[evenement.key][0] * pas, fleches[evenement.key][1] * pas);
      evenement.preventDefault();
      return;
    }

    if (modifiable && etat.selection && (evenement.key === "Delete" || evenement.key === "Backspace")) {
      supprimerSelection();
      evenement.preventDefault();
      return;
    }

    if (evenement.key === "Escape") {
      if (present().trace && etat.panneau !== "pieces") {
        changer({ ...present(), trace: null });
      }

      selectionner(null);
      return;
    }

    if (evenement.key === "Enter" && present().trace) {
      fermer();
      evenement.preventDefault();
      return;
    }

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
    deplacerHistorique(evenement.key.toLowerCase() === "y" || evenement.shiftKey);
  });

  function choisirOutil(outil) {
    if (present().trace && etat.panneau !== "pieces") {
      changer({ ...present(), trace: null });
    }

    etat.outil = outil;
    etat.selection = null;
    etat.mesure = { de: null, a: null };
    el("[data-role=mesure]").hidden = true;
    rendre();
  }

  page.addEventListener("click", evenement => {
    const cible = evenement.target.closest("button");
    if (!cible || !page.contains(cible)) {
      return;
    }

    if (cible.dataset.outil) {
      choisirOutil(cible.dataset.outil);
      return;
    }

    if (cible.dataset.cible) {
      etat.cible = cible.dataset.cible;
      rendre();
      return;
    }

    if (cible.dataset.pousser) {
      const [dx, dy] = cible.dataset.pousser.split(" ").map(Number);
      const s = etat.selection;
      if (s?.genre === "ouverture") {
        modifierSelection(doc => glisserOuverture(doc, s.index, dx * PAS_CLAVIER));
      } else {
        deplacerSelection(dx * PAS_CLAVIER, dy * PAS_CLAVIER);
      }

      return;
    }

    const action = cible.dataset.action;
    if (commandeVue(action)) {
      return;
    }

    const actions = {
      annuler: () => deplacerHistorique(false),
      retablir: () => deplacerHistorique(true),
      enregistrer: () => enregistrer(null),
      calque: basculerCalque,
      "changer-fond": () => { etat.panneau = "fond"; rendre(); },
      recalibrer: () => choisirFond(present().contenu.fond.fondId),
      recaler: commencerRecalage,
      "recalage-recommencer": () => { etat.recalage = recalage.creerRecalage(); rendre(); },
      "recalage-annuler": finirRecalage,
      "valider-recalage": validerRecalage,
      "fermer-panneau": () => { etat.panneau = "proprietes"; rendre(); },
      "retour-fond": () => { etat.echelleSaisie = null; etat.panneau = "fond"; changer(abandonnerSaisie(present())); ajusterVue(); },
      "valider-calibration": validerEchelle,
      grille: commencerGrille,
      "fermer-trace": fermer,
      "abandonner-trace": () => { changer({ ...present(), trace: null }); choisirOutil("selection"); },
      "zone-suivante": zoneSuivante,
      "terminer-pieces": terminerPieces,
      "inserer-sommet": () => {
        const s = etat.selection;
        modifierSelection(doc => insererSommet(doc, s, s.sommet));
        etat.selection = { ...s, sommet: s.sommet + 1 };
        rendre();
      },
      "supprimer-sommet": supprimerSelection,
      "supprimer-element": () => { etat.selection = etat.selection && { genre: etat.selection.genre, index: etat.selection.index }; supprimerSelection(); },
      deselectionner: () => selectionner(null),
      "brouillon-abandonner": abandonnerBrouillon,
      "brouillon-reprendre": () => {
        el("[data-role=brouillon-texte]").textContent =
          `Confirmez : votre brouillon remplacera le contenu de la version ${etat.reprise?.versionCourante} dans la prochaine version enregistrée ; la version ${etat.reprise?.versionCourante} restera dans l'historique, restaurable.`;
        cible.hidden = true;
        el("[data-action=brouillon-confirmer]").hidden = false;
      },
      "brouillon-confirmer": reprendreBrouillon,
    };
    void actions[action]?.();
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

  // Champs des panneaux de dessin (listes et longueurs saisies, au clavier : alternative au glisser).
  const champs = {
    "trace-zone": valeur => present().trace && changer(regler(present(), { zoneId: valeur || null })),
    "trace-type": valeur => {
      etat.liaison.type = valeur;
      if (present().trace) {
        changer(regler(present(), { type: valeur }));
      }
    },
    "trace-niveau": valeur => {
      etat.liaison.versNiveauId = valeur || null;
      if (present().trace) {
        changer(regler(present(), { versNiveauId: valeur || null }));
      }
    },
    "piece-zone": valeur => valeur && modifierSelection((doc, s) => changerZone(doc, s.index, valeur)),
    "ouverture-type": valeur => {
      etat.typeOuverture = valeur;
      modifierSelection((doc, s) => modifierOuverture(doc, s.index, { type: valeur }));
    },
    "ouverture-face": valeur => modifierSelection((doc, s) => modifierOuverture(doc, s.index, { enFace: valeur || null })),
    "escalier-type": valeur => modifierSelection((doc, s) => modifierEscalier(doc, s.index, { type: valeur })),
    "escalier-niveau": valeur => modifierSelection((doc, s) => modifierEscalier(doc, s.index, { versNiveauId: valeur || null })),
  };
  for (const [nom, appliquerChamp] of Object.entries(champs)) {
    el(`[data-champ=${nom}]`)?.addEventListener("change", evenement => appliquerChamp(evenement.target.value));
  }

  function lireMetres(champ, appliquerValeur) {
    el(`[data-champ=${champ}]`)?.addEventListener("change", evenement => {
      const texte = evenement.target.value.trim().replace(/\s/g, "").replace(/m$/i, "").replace(",", ".");
      const valeur = /^-?\d+(\.\d+)?$/.test(texte) ? Number(texte) : null;
      if (valeur == null) {
        message("Saisissez un nombre de mètres, par exemple 4,25.", "avertissement");
        return;
      }

      appliquerValeur(valeur);
    });
  }

  lireMetres("ouverture-largeur", valeur => modifierSelection((doc, s) => modifierOuverture(doc, s.index, { largeur: valeur })));
  for (const prefixe of ["piece", "escalier"]) {
    for (const axe of ["x", "y"]) {
      lireMetres(`${prefixe}-${axe}`, valeur => {
        const s = etat.selection;
        const p = present().contenu[s.genre === "piece" ? "pieces" : "escaliers"][s.index].contour[s.sommet];
        modifierSelection(doc => deplacerSommet(doc, s, s.sommet, { ...p, [axe]: valeur }));
      });
    }
  }

  el("[data-role=import]")?.addEventListener("submit", evenement => {
    evenement.preventDefault();
    void importer(evenement.target); // refus et erreurs affichés par importer
  });

  for (const formulaire of tous("[data-role=creer-zone]")) {
    formulaire.addEventListener("submit", evenement => {
      evenement.preventDefault();
      void creerZone(evenement.target);
    });
  }

  // Zones listées à côté du plan, sans contour : Parties communes et zones sans niveau (RP-21.5).
  for (const liste of tous("[data-role=zones-marge]")) {
    liste.replaceChildren(...zonesALaMarge(cadre).map(z => {
      const item = document.createElement("li");
      item.textContent = z.nom;
      return item;
    }));
    liste.closest("[data-role=marge]")?.toggleAttribute("hidden", zonesALaMarge(cadre).length === 0);
  }

  new ResizeObserver(() => {
    const apres = mesurerAffichage();
    if (etat.vue && etat.affichage && (apres.largeur !== etat.affichage.largeur || apres.hauteur !== etat.affichage.hauteur)) {
      etat.vue = redimensionner(etat.vue, etat.affichage, apres);
      etat.affichage = apres;
      planifierVue();
    }
  }).observe(svg);

  // ——— Ouverture ———
  void (async () => {
    const local = stockage();
    if (local) {
      purgerAutresComptes(local, d.compte); // tablette partagée : aucun brouillon d'un autre compte ne reste
    }

    if (etat.planId) {
      const [lue] = await Promise.all([lirePlan(), lireFonds()]);
      if (lue) {
        charger(lue);
      } else {
        // Plan illisible (réseau, session expirée) : rien n'est modifiable et le brouillon local reste intact.
        etat.chargementEchoue = true;
      }
    }

    examinerBrouillon();
    if (etat.mode === "guide" && etat.panneau === "fond") {
      el("[data-role=resume]").textContent = "Quatre étapes ; chaque étape s'enregistre comme une version, vous pouvez vous arrêter et reprendre.";
    }

    rendre();
    ajusterVue();
  })();
}

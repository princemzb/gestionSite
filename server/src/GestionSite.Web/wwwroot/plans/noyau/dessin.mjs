// Éditeur de plans de bâtiment (US-21, S21-08, RP-21.5, RP-21.6, ADR-11) : pièces, ouvertures, escaliers et ascenseurs.
// Fonctions pures sur le document de l'éditeur ({ contenu, saisie, trace }) : chaque modification rend un nouveau
// document (annuler et rétablir, brouillon). Les contrôles reprennent ceux du serveur (ValidationPlan), seul juge.
// Cadre = { niveauId (null : plan masse), zones: [{ id, nom, niveauId, partiesCommunes }], niveaux: [{ id, nom, ordre }] }.

import {
  aire, areteProche, aretes, arrondirPoint, chevauchent, defautContour, distanceBord, contient, longueur, memePoint, projeter,
  surUneArete, translater,
} from "./geometrie.mjs";
import { arrondirAuCentimetre } from "./mesures.mjs";

export const PIECES_MAX = 300;
export const OUVERTURES_MAX = 1_000;
export const ESCALIERS_MAX = 100;
export const POINTS_MAX = 20_000;
export const OUVERTURE_MIN = 0.2;
export const OUVERTURE_MAX = 30;

export const TYPES_OUVERTURE = Object.freeze(["Porte", "Baie", "Fenetre"]);
export const TYPES_LIAISON = Object.freeze(["Escalier", "Ascenseur"]);

/**
 * Largeur proposée d'une nouvelle ouverture, modifiable ensuite : porte de 0,90 m (une unité de passage), baie de
 * 1,40 m (deux unités de passage), fenêtre de 1,00 m.
 */
export const LARGEURS = Object.freeze({ Porte: 0.9, Baie: 1.4, Fenetre: 1 });

/** Épaisseur de mur au-delà de laquelle la pièce d'en face n'est pas proposée pour une ouverture (maçonnerie ancienne). */
export const MUR_MAX = 1;

const LIBELLES_TYPE = Object.freeze({ Porte: "Porte", Baie: "Baie", Fenetre: "Fenêtre", Escalier: "Escalier", Ascenseur: "Ascenseur" });

/** Libellé affiché d'un type d'ouverture ou de liaison. */
export function libelleType(type) {
  return LIBELLES_TYPE[type] ?? type;
}

// ——— Zones et niveaux du cadre ———

/** Zones dessinables sur ce plan : zones du niveau (plan masse : sans niveau), hors Parties communes (RP-21.5). */
export function zonesDessinables(cadre) {
  return cadre.zones.filter(z => !z.partiesCommunes && (z.niveauId ?? null) === (cadre.niveauId ?? null));
}

/** Zones listées à côté du plan, sans contour : Parties communes et, sur un plan de niveau, zones sans niveau. */
export function zonesALaMarge(cadre) {
  return cadre.zones.filter(z => z.partiesCommunes || (cadre.niveauId && !z.niveauId));
}

/** Zones dessinables qui n'ont pas encore de contour dans ce contenu. */
export function zonesRestantes(contenu, cadre) {
  const dessinees = new Set(contenu.pieces.map(p => p.zoneId));
  return zonesDessinables(cadre).filter(z => !dessinees.has(z.id));
}

/** Avancement de l'étape « pièces zone par zone » : zones tracées sur le total, et la prochaine zone à tracer. */
export function avancement(contenu, cadre) {
  const total = zonesDessinables(cadre).length;
  const restantes = zonesRestantes(contenu, cadre);
  return { total, faites: total - restantes.length, suivante: restantes[0] ?? null };
}

/** Niveaux d'arrivée possibles d'un escalier ou d'un ascenseur : les autres niveaux du bâtiment, du plus bas au plus haut. */
export function autresNiveaux(cadre) {
  return cadre.niveauId ? cadre.niveaux.filter(n => n.id !== cadre.niveauId).sort((a, b) => a.ordre - b.ordre) : [];
}

/** Nom d'une zone du cadre (« Zone » si elle est inconnue). */
export function nomZone(cadre, zoneId) {
  return cadre.zones.find(z => z.id === zoneId)?.nom ?? "Zone";
}

/** Nom d'un niveau du cadre (« Niveau » s'il est inconnu). */
export function nomNiveau(cadre, niveauId) {
  return cadre.niveaux.find(n => n.id === niveauId)?.nom ?? "Niveau";
}

// ——— Tracé d'un contour (pièce, escalier ou ascenseur) ———

/**
 * Commence le tracé d'une pièce (zone facultative, choisie avant ou après) ou d'une liaison (type et niveau d'arrivée).
 * Ferme une calibration en cours.
 */
export function commencerTrace(document, genre, options = {}) {
  if (genre !== "piece" && genre !== "escalier") {
    throw new RangeError("Tracé inconnu.");
  }

  const trace = genre === "piece"
    ? { genre, zoneId: options.zoneId ?? null, points: [] }
    : { genre, type: options.type ?? "Escalier", versNiveauId: options.versNiveauId ?? null, points: [] };
  return { ...document, saisie: null, trace };
}

/** Modifie les options du tracé en cours (zone, type, niveau d'arrivée), sans toucher à ses sommets. */
export function regler(document, options) {
  return document.trace ? { ...document, trace: { ...document.trace, ...options, genre: document.trace.genre, points: document.trace.points } } : document;
}

/** Ajoute un sommet au tracé (au centimètre) ; ignoré s'il répète le dernier ou sort du repère. */
export function ajouterSommet(document, point) {
  const trace = document.trace;
  const p = arrondirPoint(point);
  if (!trace || !p || (trace.points.length > 0 && memePoint(trace.points.at(-1), p))) {
    return document;
  }

  return { ...document, trace: { ...trace, points: [...trace.points, p] } };
}

/** Vrai si le point ferme le tracé : il retombe sur le premier sommet d'un tracé d'au moins 3 sommets. */
export function fermeLeTrace(trace, point, tolerance) {
  return Boolean(trace && trace.points.length >= 3 && longueur(trace.points[0], point) <= tolerance);
}

/** Abandonne le tracé en cours. */
export function abandonnerTrace(document) {
  return document.trace ? { ...document, trace: null } : document;
}

function compterPoints(contenu) {
  return contenu.pieces.reduce((n, p) => n + p.contour.length, 0) + contenu.escaliers.reduce((n, e) => n + e.contour.length, 0);
}

/**
 * Ferme le tracé : rend { document } (pièce ou liaison ajoutée au contenu, tracé clos, avec son rang dans la liste) ou
 * { defaut } sans rien changer.
 */
export function fermerTrace(document, cadre) {
  const trace = document.trace;
  if (!trace) {
    return { defaut: "trace" };
  }

  const contenu = document.contenu;
  const defautForme = defautContour(trace.points);
  if (defautForme) {
    return { defaut: defautForme };
  }

  if (compterPoints(contenu) + trace.points.length > POINTS_MAX) {
    return { defaut: "points" };
  }

  if (trace.genre === "escalier") {
    const defaut = defautLiaison({ type: trace.type, versNiveauId: trace.versNiveauId }, cadre)
      ?? (contenu.escaliers.length >= ESCALIERS_MAX ? "nombre" : null);
    if (defaut) {
      return { defaut };
    }

    const escalier = { type: trace.type, contour: trace.points, versNiveauId: trace.versNiveauId };
    return { document: { ...document, trace: null, contenu: { ...contenu, escaliers: [...contenu.escaliers, escalier] } }, index: contenu.escaliers.length };
  }

  const defaut = defautZone(contenu, cadre, trace.zoneId, -1)
    ?? (contenu.pieces.length >= PIECES_MAX ? "nombre" : null)
    ?? chevauchement(contenu, trace.points, -1);
  if (defaut) {
    return { defaut };
  }

  const piece = { zoneId: trace.zoneId, contour: trace.points };
  return { document: { ...document, trace: null, contenu: { ...contenu, pieces: [...contenu.pieces, piece] } }, index: contenu.pieces.length };
}

// ——— Contrôles (mêmes règles que le serveur) ———

function defautZone(contenu, cadre, zoneId, index) {
  if (!zoneId) {
    return "zone";
  }

  if (!zonesDessinables(cadre).some(z => z.id === zoneId)) {
    return "zone-niveau";
  }

  return contenu.pieces.some((p, i) => i !== index && p.zoneId === zoneId) ? "zone-dessinee" : null;
}

function chevauchement(contenu, contour, index) {
  const autre = contenu.pieces.findIndex((p, i) => i !== index && chevauchent(contour, p.contour));
  return autre >= 0 ? { code: "chevauche", avec: contenu.pieces[autre].zoneId } : null;
}

function defautLiaison(liaison, cadre) {
  if (!TYPES_LIAISON.includes(liaison.type)) {
    return "type";
  }

  return autresNiveaux(cadre).some(n => n.id === liaison.versNiveauId) ? null : "niveau";
}

/** Défaut de la pièce de rang `index` (zone, contour, chevauchement d'une autre pièce), ou null. */
export function defautPiece(contenu, cadre, index) {
  const piece = contenu.pieces[index];
  return defautZone(contenu, cadre, piece.zoneId, index) ?? defautContour(piece.contour) ?? chevauchement(contenu, piece.contour, index);
}

/** Défaut de l'escalier ou de l'ascenseur de rang `index` (type, niveau d'arrivée, contour), ou null. */
export function defautEscalier(contenu, cadre, index) {
  const escalier = contenu.escaliers[index];
  return defautLiaison(escalier, cadre) ?? defautContour(escalier.contour);
}

/**
 * Défaut de l'ouverture de rang `index`, ou null : « type », « largeur » (0,20 à 30 m), « zones » (une ou deux pièces
 * dessinées, distinctes), « arete » (posée à plus de 5 cm d'une arête de ses pièces).
 */
export function defautOuverture(contenu, index) {
  const ouverture = contenu.ouvertures[index];
  if (!TYPES_OUVERTURE.includes(ouverture.type)) {
    return "type";
  }

  const largeur = arrondirAuCentimetre(longueur(ouverture.de, ouverture.a));
  if (largeur < OUVERTURE_MIN || largeur > OUVERTURE_MAX) {
    return "largeur";
  }

  const zones = ouverture.zones ?? [];
  const formes = zones.map(z => contenu.pieces.find(p => p.zoneId === z)?.contour);
  if (zones.length === 0 || zones.length > 2 || new Set(zones).size !== zones.length || formes.some(f => !f)) {
    return "zones";
  }

  return formes.some(f => surUneArete(f, ouverture.de, ouverture.a)) ? null : "arete";
}

/**
 * Premier défaut du contenu, dans l'ordre où le serveur contrôle (pièces, escaliers, ouvertures) : { genre, index,
 * defaut } ou null. Un contenu sans défaut peut être enregistré.
 */
export function premierDefaut(contenu, cadre) {
  if (contenu.pieces.length > PIECES_MAX || contenu.escaliers.length > ESCALIERS_MAX || contenu.ouvertures.length > OUVERTURES_MAX) {
    return { genre: "plan", index: 0, defaut: "nombre" };
  }

  if (compterPoints(contenu) > POINTS_MAX) {
    return { genre: "plan", index: 0, defaut: "points" };
  }

  const listes = [["piece", contenu.pieces, defautPiece], ["escalier", contenu.escaliers, defautEscalier]];
  for (const [genre, liste, controle] of listes) {
    for (let index = 0; index < liste.length; index++) {
      const defaut = controle(contenu, cadre, index);
      if (defaut) {
        return { genre, index, defaut };
      }
    }
  }

  for (let index = 0; index < contenu.ouvertures.length; index++) {
    const defaut = defautOuverture(contenu, index);
    if (defaut) {
      return { genre: "ouverture", index, defaut };
    }
  }

  return null;
}

const messages = {
  trace: "Aucun tracé en cours.",
  sommets: "Un contour a de 3 à 500 sommets : touchez au moins trois points.",
  bornes: "Un sommet sort du repère du bâtiment (5 km autour de l'origine).",
  repete: "Deux sommets consécutifs sont confondus : supprimez l'un d'eux.",
  croise: "Le contour se croise ou se touche lui-même : déplacez ou supprimez un sommet.",
  aire: "Contour trop petit : 0,25 m² au moins.",
  etroit: "Contour trop étroit : plus de 5 cm de large.",
  points: "Le plan dépasse 20 000 sommets.",
  nombre: "Le plan a déjà le nombre maximal d'éléments (300 pièces, 100 escaliers ou ascenseurs, 1 000 ouvertures).",
  zone: "Choisissez la zone de l'inventaire de cette pièce.",
  "zone-niveau": "Cette zone n'est pas sur ce niveau (ou ce sont les Parties communes, jamais dessinées).",
  "zone-dessinee": "Cette zone a déjà son contour sur ce plan : une zone, un contour.",
  type: "Choisissez le type.",
  niveau: "Choisissez le niveau d'arrivée (un autre niveau du bâtiment).",
  largeur: "Largeur d'une ouverture : de 0,20 m à 30 m.",
  zones: "Une ouverture relie une ou deux pièces dessinées sur ce plan.",
  arete: "L'ouverture n'est plus posée sur une arête de sa pièce (5 cm près) : déplacez-la ou supprimez-la.",
  "hors-arete": "Touchez une arête d'une pièce pour y poser l'ouverture.",
  "arete-courte": "Cette arête est plus courte que 20 cm : une ouverture fait au moins 0,20 m.",
};

/** Message à l'utilisateur pour un défaut de dessin (le chevauchement nomme l'autre pièce). */
export function messageDessin(defaut, cadre) {
  if (defaut && typeof defaut === "object") {
    return `Cette pièce chevauche « ${nomZone(cadre, defaut.avec)} » de plus de 5 cm : un mur commun est admis, pas un recouvrement.`;
  }

  return messages[defaut] ?? "Élément invalide.";
}

// ——— Édition des éléments posés ———

function remplacer(liste, index, element) {
  return liste.map((e, i) => (i === index ? element : e));
}

const LISTES = Object.freeze({ piece: "pieces", escalier: "escaliers", ouverture: "ouvertures" });

function liste(genre) {
  const nom = LISTES[genre];
  if (!nom) {
    throw new RangeError("Élément inconnu.");
  }

  return nom;
}

// Ouvertures posées sur une arête d'une pièce : rang de l'arête, position du milieu sur l'arête et largeur, pour les
// replacer quand la pièce change (elles suivent leur mur).
function ancrages(contenu, piece) {
  const zoneId = piece.zoneId;
  const resultat = [];
  contenu.ouvertures.forEach((o, rang) => {
    if (o.zones?.[0] !== zoneId) {
      return;
    }

    const index = aretes(piece.contour).findIndex(([p, q]) => surUneArete([p, q], o.de, o.a));
    if (index >= 0) {
      const [p, q] = aretes(piece.contour)[index];
      const milieu = { x: (o.de.x + o.a.x) / 2, y: (o.de.y + o.a.y) / 2 };
      resultat.push({ rang, index, t: projeter(milieu, p, q).t, largeur: longueur(o.de, o.a), milieu });
    }
  });
  return resultat;
}

// Ouverture de largeur donnée centrée en un point d'une arête, dans le sens de l'arête, au centimètre.
function segmentSur(p, q, centre, largeur) {
  const l = longueur(p, q) || 1;
  const ux = (q.x - p.x) / l;
  const uy = (q.y - p.y) / l;
  const demi = largeur / 2;
  return {
    de: arrondirPoint({ x: centre.x - ux * demi, y: centre.y - uy * demi }) ?? centre,
    a: arrondirPoint({ x: centre.x + ux * demi, y: centre.y + uy * demi }) ?? centre,
  };
}

// Ouverture de largeur donnée sur l'arête [p, q], son milieu en t (0 en p, 1 en q) gardé à une demi-largeur des
// extrémités quand l'arête est assez longue : elle tient sur l'arête.
function placerSur(p, q, t, largeur) {
  const longueurArete = longueur(p, q) || 1;
  const marge = Math.min(largeur / 2, longueurArete / 2) / longueurArete;
  const u = Math.min(1 - marge, Math.max(marge, t));
  return segmentSur(p, q, { x: p.x + u * (q.x - p.x), y: p.y + u * (q.y - p.y) }, largeur);
}

// Replace les ouvertures ancrées sur la pièce modifiée : même arête (même rang) quand le nombre de sommets n'a pas changé,
// sinon l'arête la plus proche de leur ancien milieu.
function replacer(contenu, ancres, contour, memeAretes) {
  if (ancres.length === 0) {
    return contenu.ouvertures;
  }

  const liste = aretes(contour);
  const ouvertures = [...contenu.ouvertures];
  for (const ancre of ancres) {
    const proche = memeAretes ? { index: ancre.index, t: ancre.t } : areteProche(contour, ancre.milieu, Infinity);
    const [p, q] = liste[proche.index];
    ouvertures[ancre.rang] = { ...ouvertures[ancre.rang], ...placerSur(p, q, proche.t, ancre.largeur) };
  }

  return ouvertures;
}

// Nouveau contour d'une pièce ou d'une liaison ; les ouvertures d'une pièce suivent.
function changerContour(document, cible, contour, memeAretes) {
  const contenu = document.contenu;
  const nom = liste(cible.genre);
  const element = contenu[nom][cible.index];
  const ouvertures = cible.genre === "piece" ? replacer(contenu, ancrages(contenu, element), contour, memeAretes) : contenu.ouvertures;
  return { ...document, contenu: { ...contenu, [nom]: remplacer(contenu[nom], cible.index, { ...element, contour }), ouvertures } };
}

/** Déplace un sommet d'une pièce ou d'une liaison (au centimètre ; ignoré hors du repère). */
export function deplacerSommet(document, cible, sommet, point) {
  const p = arrondirPoint(point);
  const element = document.contenu[liste(cible.genre)][cible.index];
  if (!p || cible.genre === "ouverture" || memePoint(element.contour[sommet], p)) {
    return document;
  }

  return changerContour(document, cible, element.contour.map((s, i) => (i === sommet ? p : s)), true);
}

/** Supprime un sommet (un contour garde au moins 3 sommets). */
export function supprimerSommet(document, cible, sommet) {
  const element = document.contenu[liste(cible.genre)][cible.index];
  if (element.contour.length <= 3) {
    return document;
  }

  return changerContour(document, cible, element.contour.filter((_, i) => i !== sommet), false);
}

/** Insère un sommet au milieu de l'arête qui suit le sommet donné (500 sommets au plus). */
export function insererSommet(document, cible, sommet) {
  const contour = document.contenu[liste(cible.genre)][cible.index].contour;
  if (contour.length >= 500) {
    return document;
  }

  const p = contour[sommet];
  const q = contour[(sommet + 1) % contour.length];
  const milieu = arrondirPoint({ x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 });
  return changerContour(document, cible, [...contour.slice(0, sommet + 1), milieu, ...contour.slice(sommet + 1)], false);
}

/**
 * Déplace un élément de (dx, dy) mètres : une pièce avec ses ouvertures, une liaison, ou une ouverture (qui doit rester
 * sur son arête, contrôlé à l'enregistrement).
 */
export function deplacerElement(document, cible, dx, dy) {
  const contenu = document.contenu;
  if (cible.genre === "ouverture") {
    const o = contenu.ouvertures[cible.index];
    const [de, a] = translater([o.de, o.a], dx, dy);
    return { ...document, contenu: { ...contenu, ouvertures: remplacer(contenu.ouvertures, cible.index, { ...o, de, a }) } };
  }

  const element = contenu[liste(cible.genre)][cible.index];
  const contour = translater(element.contour, dx, dy);
  if (contour.some(p => Math.abs(p.x) > 5_000 || Math.abs(p.y) > 5_000)) {
    return document;
  }

  return changerContour(document, cible, contour, true);
}

/**
 * Fait glisser une ouverture le long de son arête de `distance` mètres (sens de « de » vers « a »), sans quitter l'arête
 * de sa pièce quand elle y est posée.
 */
export function glisserOuverture(document, index, distance) {
  const contenu = document.contenu;
  const o = contenu.ouvertures[index];
  const l = longueur(o.de, o.a) || 1;
  const piece = contenu.pieces.find(p => p.zoneId === o.zones?.[0]);
  const arete = piece ? aretes(piece.contour).find(a => surUneArete(a, o.de, o.a)) : null;
  const ux = (o.a.x - o.de.x) / l;
  const uy = (o.a.y - o.de.y) / l;
  const centre = { x: (o.de.x + o.a.x) / 2 + ux * distance, y: (o.de.y + o.a.y) / 2 + uy * distance };
  if (arete) {
    // Le milieu reste sur l'arête, à une demi-largeur au moins de ses extrémités quand elle est assez longue.
    const place = placerSur(arete[0], arete[1], projeter(centre, arete[0], arete[1]).t, l);
    return { ...document, contenu: { ...contenu, ouvertures: remplacer(contenu.ouvertures, index, { ...o, ...place }) } };
  }

  return deplacerElement(document, { genre: "ouverture", index }, ux * distance, uy * distance);
}

/**
 * Supprime un élément. Supprimer une pièce supprime les ouvertures posées sur elle seule, et retire sa zone de celles
 * qui la reliaient à une autre pièce.
 */
export function supprimerElement(document, cible) {
  const contenu = document.contenu;
  const nom = liste(cible.genre);
  const restants = contenu[nom].filter((_, i) => i !== cible.index);
  if (cible.genre !== "piece") {
    return { ...document, contenu: { ...contenu, [nom]: restants } };
  }

  const zoneId = contenu.pieces[cible.index].zoneId;
  const ouvertures = contenu.ouvertures
    .map(o => ({ ...o, zones: (o.zones ?? []).filter(z => z !== zoneId) }))
    .filter(o => o.zones.length > 0);
  return { ...document, contenu: { ...contenu, pieces: restants, ouvertures } };
}

/** Change la zone d'une pièce ; ses ouvertures suivent (la zone est remplacée dans leurs zones). */
export function changerZone(document, index, zoneId) {
  const contenu = document.contenu;
  const avant = contenu.pieces[index].zoneId;
  if (avant === zoneId) {
    return document;
  }

  const ouvertures = contenu.ouvertures.map(o => (o.zones?.includes(avant) ? { ...o, zones: o.zones.map(z => (z === avant ? zoneId : z)) } : o));
  return { ...document, contenu: { ...contenu, pieces: remplacer(contenu.pieces, index, { ...contenu.pieces[index], zoneId }), ouvertures } };
}

/** Change le type ou le niveau d'arrivée d'un escalier ou d'un ascenseur. */
export function modifierEscalier(document, index, changements) {
  const contenu = document.contenu;
  const escalier = { ...contenu.escaliers[index], ...changements, contour: contenu.escaliers[index].contour };
  return { ...document, contenu: { ...contenu, escaliers: remplacer(contenu.escaliers, index, escalier) } };
}

// ——— Ouvertures ———

/**
 * Pièce d'en face pour une ouverture : la pièce (autre que la sienne) dont le bord est le plus proche de son milieu, à
 * l'épaisseur d'un mur au plus (1 m) ; null pour une ouverture vers l'extérieur.
 */
export function pieceEnFace(contenu, zoneId, milieu) {
  let meilleure = null;
  for (const piece of contenu.pieces) {
    const d = piece.zoneId === zoneId ? Infinity : distanceBord(piece.contour, milieu);
    if (d <= MUR_MAX && (!meilleure || d < meilleure.distance)) {
      meilleure = { zoneId: piece.zoneId, distance: d };
    }
  }

  return meilleure?.zoneId ?? null;
}

/**
 * Pose une ouverture sur l'arête de pièce la plus proche du point touché (à `tolerance` mètres au plus) : centrée sur
 * le point, de la largeur proposée pour son type (réduite à la longueur de l'arête), reliée à sa pièce et, à l'épaisseur
 * d'un mur, à la pièce d'en face. Rend { document, index } ou { defaut }.
 */
export function poserOuverture(document, type, point, tolerance) {
  const contenu = document.contenu;
  if (!TYPES_OUVERTURE.includes(type)) {
    return { defaut: "type" };
  }

  if (contenu.ouvertures.length >= OUVERTURES_MAX) {
    return { defaut: "nombre" };
  }

  let choix = null;
  for (const piece of contenu.pieces) {
    const arete = areteProche(piece.contour, point, tolerance);
    if (arete && (!choix || arete.distance < choix.arete.distance)) {
      choix = { piece, arete };
    }
  }

  if (!choix) {
    return { defaut: "hors-arete" };
  }

  const [p, q] = aretes(choix.piece.contour)[choix.arete.index];
  const longueurArete = longueur(p, q);
  if (longueurArete < OUVERTURE_MIN) {
    return { defaut: "arete-courte" };
  }

  const place = placerSur(p, q, choix.arete.t, Math.min(LARGEURS[type], longueurArete));
  const enFace = pieceEnFace(contenu, choix.piece.zoneId, { x: (place.de.x + place.a.x) / 2, y: (place.de.y + place.a.y) / 2 });
  const ouverture = { type, ...place, zones: enFace ? [choix.piece.zoneId, enFace] : [choix.piece.zoneId] };
  return { document: { ...document, contenu: { ...contenu, ouvertures: [...contenu.ouvertures, ouverture] } }, index: contenu.ouvertures.length };
}

const surfaces = new Intl.NumberFormat("fr-FR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** Surface d'un contour au dixième de m² (« 29,9 m² »). */
export function formaterSurface(contour) {
  return `${surfaces.format(aire(contour))} m²`;
}

/** Largeur d'une ouverture, en mètres au centimètre. */
export function largeurOuverture(ouverture) {
  return arrondirAuCentimetre(longueur(ouverture.de, ouverture.a));
}

/**
 * Modifie une ouverture : type, largeur (gardant son milieu et son sens) ou pièce d'en face (`enFace`, null pour
 * l'extérieur).
 */
export function modifierOuverture(document, index, changements) {
  const contenu = document.contenu;
  let o = { ...contenu.ouvertures[index] };
  if (changements.type !== undefined) {
    o.type = changements.type;
  }

  if (changements.largeur !== undefined) {
    const milieu = { x: (o.de.x + o.a.x) / 2, y: (o.de.y + o.a.y) / 2 };
    o = { ...o, ...segmentSur(o.de, o.a, milieu, changements.largeur) };
  }

  if (changements.enFace !== undefined) {
    o.zones = changements.enFace ? [o.zones[0], changements.enFace] : [o.zones[0]];
  }

  return { ...document, contenu: { ...contenu, ouvertures: remplacer(contenu.ouvertures, index, o) } };
}

// ——— Désignation et accrochage ———

/**
 * Élément sous un point (sélection) : sommet de l'élément déjà sélectionné, ouverture, escalier, puis la plus petite
 * pièce qui contient le point. Rend { genre, index, sommet? } ou null.
 */
export function elementSous(contenu, point, tolerance, selection = null) {
  if (selection && selection.genre !== "ouverture") {
    const contour = contenu[liste(selection.genre)][selection.index]?.contour ?? [];
    const sommet = contour.findIndex(p => longueur(p, point) <= tolerance);
    if (sommet >= 0) {
      return { genre: selection.genre, index: selection.index, sommet };
    }
  }

  const ouverture = contenu.ouvertures.findIndex(o => projeter(point, o.de, o.a).distance <= tolerance);
  if (ouverture >= 0) {
    return { genre: "ouverture", index: ouverture };
  }

  const escalier = contenu.escaliers.findIndex(e => contient(e.contour, point));
  if (escalier >= 0) {
    return { genre: "escalier", index: escalier };
  }

  let piece = null;
  contenu.pieces.forEach((p, index) => {
    if (contient(p.contour, point) && (!piece || aire(p.contour) < piece.aire)) {
      piece = { index, aire: aire(p.contour) };
    }
  });
  return piece ? { genre: "piece", index: piece.index } : null;
}

/**
 * Accrochage raisonnable d'un point touché : au sommet le plus proche des contours donnés (pièces, escaliers, tracé en
 * cours, calque du niveau inférieur), sinon à l'arête la plus proche, à `tolerance` mètres au plus ; sinon le point
 * lui-même. Rend { point (au centimètre), accroche: "sommet" | "arete" | null }.
 */
export function accrocher(point, contours, tolerance) {
  let sommet = null;
  let arete = null;
  for (const contour of contours) {
    for (const p of contour) {
      const d = longueur(p, point);
      if (d <= tolerance && (!sommet || d < sommet.distance)) {
        sommet = { point: p, distance: d };
      }
    }

    if (contour.length >= 2) {
      const proche = areteProche(contour, point, tolerance);
      if (proche && (!arete || proche.distance < arete.distance)) {
        arete = proche;
      }
    }
  }

  if (sommet) {
    return { point: arrondirPoint(sommet.point) ?? sommet.point, accroche: "sommet" };
  }

  if (arete) {
    return { point: arrondirPoint(arete.point) ?? arete.point, accroche: "arete" };
  }

  return { point: arrondirPoint(point) ?? point, accroche: null };
}

/** Contours d'accrochage d'un contenu (pièces et escaliers), sauf l'élément désigné. */
export function contoursAccrochage(contenu, sauf = null) {
  return [
    ...contenu.pieces.filter((_, i) => !(sauf?.genre === "piece" && sauf.index === i)).map(p => p.contour),
    ...contenu.escaliers.filter((_, i) => !(sauf?.genre === "escalier" && sauf.index === i)).map(e => e.contour),
  ];
}

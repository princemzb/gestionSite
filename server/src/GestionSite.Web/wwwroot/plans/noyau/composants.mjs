// Éditeur de plans de bâtiment (US-21, S21-09, RP-21.7, ADR-11) : marqueurs des composants de l'inventaire sur le plan.
// Fonctions pures sur le document de l'éditeur ({ contenu, saisie, trace }) : chaque modification rend un nouveau document
// (annuler et rétablir, brouillon). Le serveur (ValidationPlan) reste juge : composant actif du niveau, jamais un objet de
// valeur (équipement de culte, case « objet de valeur » cochée), une fois par version, hors zone accepté avec avertissement.
// Composants placables = [{ id, nom, zoneId }] : ceux du niveau, objets de valeur déjà écartés par la page.

import { arrondirPoint, contient, distanceBord, longueur, translater } from "./geometrie.mjs";

/** Marqueurs au plus par version (ValidationPlan.MarqueursMax). */
export const MARQUEURS_MAX = 2_000;

// Tolérance du bord d'une pièce : le serveur tient un point sur le bord pour couvert (Covers).
const BORD = 1e-6;

/** Rang du marqueur d'un composant dans le contenu, ou -1 s'il n'est pas placé. */
export function rangMarqueur(contenu, composantId) {
  return contenu.composants.findIndex(m => m.composantId === composantId);
}

/**
 * Place un composant au point touché (au centimètre), ou le déplace s'il est déjà placé : rend { document, index } ou
 * { defaut } (« bornes » hors du repère, « nombre » au-delà de 2 000 marqueurs).
 */
export function placerMarqueur(document, composant, point) {
  const position = arrondirPoint(point);
  if (!position) {
    return { defaut: "bornes" };
  }

  const contenu = document.contenu;
  const rang = rangMarqueur(contenu, composant.id);
  if (rang >= 0) {
    return { document: positionnerMarqueur(document, rang, position), index: rang };
  }

  if (contenu.composants.length >= MARQUEURS_MAX) {
    return { defaut: "nombre" };
  }

  // Même forme que le serveur (composantId, zoneId, position) : un marqueur relu se compare tel quel.
  const marqueur = { composantId: composant.id, zoneId: composant.zoneId, position };
  return { document: { ...document, contenu: { ...contenu, composants: [...contenu.composants, marqueur] } }, index: contenu.composants.length };
}

/** Pose le marqueur de rang `index` en un point (au centimètre ; ignoré hors du repère ou au même endroit). */
export function positionnerMarqueur(document, index, point) {
  const position = arrondirPoint(point);
  const contenu = document.contenu;
  const marqueur = contenu.composants[index];
  if (!marqueur || !position || (marqueur.position.x === position.x && marqueur.position.y === position.y)) {
    return document;
  }

  return { ...document, contenu: { ...contenu, composants: contenu.composants.map((m, i) => (i === index ? { ...m, position } : m)) } };
}

/** Déplace le marqueur de rang `index` de (dx, dy) mètres. */
export function deplacerMarqueur(document, index, dx, dy) {
  const marqueur = document.contenu.composants[index];
  return marqueur ? positionnerMarqueur(document, index, translater([marqueur.position], dx, dy)[0]) : document;
}

/** Retire du plan le marqueur de rang `index` (le composant reste dans l'inventaire). */
export function retirerMarqueur(document, index) {
  const contenu = document.contenu;
  return { ...document, contenu: { ...contenu, composants: contenu.composants.filter((_, i) => i !== index) } };
}

/**
 * Vrai si le marqueur est hors du contour de sa zone (ou si sa zone n'est pas dessinée sur ce plan) : avertissement du
 * serveur « marqueur_hors_zone », jamais un refus.
 */
export function horsZone(contenu, marqueur) {
  return dehors(contenu.pieces.find(p => p.zoneId === marqueur.zoneId), marqueur.position);
}

// Hors de la pièce (ou pièce absente) ; le bord compte comme dedans.
function dehors(piece, position) {
  return !piece || !(contient(piece.contour, position) || distanceBord(piece.contour, position) <= BORD);
}

/** Rang du marqueur le plus proche du point, à `tolerance` mètres au plus, ou -1. */
export function marqueurSous(contenu, point, tolerance) {
  let choix = -1;
  let meilleure = Infinity;
  contenu.composants.forEach((m, rang) => {
    const d = longueur(m.position, point);
    if (d <= tolerance && d < meilleure) {
      choix = rang;
      meilleure = d;
    }
  });
  return choix;
}

/**
 * Rangs des marqueurs hors du contour de leur zone, en un passage (une pièce par zone, indexée une fois) : le rendu le
 * recalcule à chaque image d'un glissement.
 */
export function marqueursHorsZone(contenu) {
  const pieces = new Map(contenu.pieces.map(p => [p.zoneId, p]));
  const rangs = new Set();
  contenu.composants.forEach((m, rang) => {
    if (dehors(pieces.get(m.zoneId), m.position)) {
      rangs.add(rang);
    }
  });
  return rangs;
}

/**
 * Composants placables avec leur état sur ce contenu, dans l'ordre donné : { composant, index (rang du marqueur ou -1),
 * horsZone }. Avec `zoneId`, seulement ceux de cette zone (panneau d'une pièce, maquette A).
 */
export function etatsComposants(contenu, placables, zoneId = null) {
  const rangs = new Map(contenu.composants.map((m, rang) => [m.composantId, rang]));
  const horsDeLeurZone = marqueursHorsZone(contenu);
  return placables.filter(c => zoneId == null || c.zoneId === zoneId).map(composant => {
    const index = rangs.get(composant.id) ?? -1;
    return { composant, index, horsZone: horsDeLeurZone.has(index) };
  });
}

/** Noms des composants placables, par identifiant (index construit une fois). */
export function nomsComposants(placables) {
  return new Map(placables.map(c => [c.id, c.nom]));
}

/** Nom affiché d'un marqueur : celui du composant, ou « Composant retiré ou déplacé » s'il n'est plus placable ici. */
export function nomMarqueur(noms, composantId) {
  return noms.get(composantId) ?? "Composant retiré ou déplacé";
}

/** Composants placés et à placer sur ce contenu (« 3 placés sur 5 »). */
export function avancementComposants(contenu, placables) {
  const places = new Set(contenu.composants.map(m => m.composantId));
  return { total: placables.length, places: placables.filter(c => places.has(c.id)).length };
}

/**
 * Signalement des marqueurs obsolètes de la version lue (RP-21.7) : composants retirés de l'inventaire, déplacés dans une
 * autre zone ou devenus objets de valeur depuis, retirés de la prochaine version ; chaîne vide s'il n'y en a pas.
 */
export function messageObsoletes(nombre) {
  if (!nombre) {
    return "";
  }

  const pluriel = nombre > 1;
  return `${nombre} marqueur${pluriel ? "s" : ""} de composant ${pluriel ? "ne sont" : "n'est"} plus affiché${pluriel ? "s" : ""} : composant retiré de l'inventaire, déplacé dans une autre zone ou devenu objet de valeur. ${pluriel ? "Ils seront retirés" : "Il sera retiré"} de la prochaine version enregistrée.`;
}

const messages = {
  bornes: "Ce point sort du repère du bâtiment (5 km autour de l'origine).",
  nombre: "Le plan a déjà 2 000 marqueurs de composants.",
};

/** Message à l'utilisateur pour un refus de placement. */
export function messageMarqueur(defaut) {
  return messages[defaut] ?? "Placement impossible.";
}

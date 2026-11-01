// Éditeur de plans de bâtiment (US-21, S21-07, ADR-11) : vue SVG en mètres (viewBox), zoom, déplacement et pincement.
// Fonctions pures : la colle DOM (wwwroot/plans/editeur.mjs) fournit la taille de la zone d'affichage et les positions
// des pointeurs, en pixels CSS relatifs au coin haut gauche du SVG.
// La viewBox garde toujours les proportions de la zone d'affichage : un pixel CSS vaut le même nombre de mètres en x et
// en y, et la conversion écran → repère est linéaire.

import { exigerNombre } from "./mesures.mjs";

/** Largeur visible minimale (50 cm) et maximale (12 km : tout le repère de ±5 km avec une marge), en mètres. */
export const LARGEUR_MIN = 0.5;
export const LARGEUR_MAX = 12_000;

/** Vue par défaut d'un plan vierge : 20 m de large, origine du repère en haut à gauche avec une marge. */
export const LARGEUR_DEFAUT = 20;

/** Zone d'affichage valide : largeur et hauteur strictement positives (pixels CSS). */
export function exigerAffichage(affichage) {
  if (!affichage || !(exigerNombre(affichage.largeur) > 0) || !(exigerNombre(affichage.hauteur) > 0)) {
    throw new RangeError("Zone d'affichage invalide : largeur et hauteur positives attendues.");
  }

  return affichage;
}

/** Mètres par pixel CSS de la vue. */
export function echelleVue(vue, affichage) {
  return vue.largeur / exigerAffichage(affichage).largeur;
}

function borner(largeur) {
  return Math.min(LARGEUR_MAX, Math.max(LARGEUR_MIN, largeur));
}

// Vue de largeur donnée, centrée sur un point du repère, aux proportions de la zone d'affichage.
function centree(centre, largeur, affichage) {
  const l = borner(largeur);
  const h = l * affichage.hauteur / affichage.largeur;
  return { x: centre.x - l / 2, y: centre.y - h / 2, largeur: l, hauteur: h };
}

/**
 * Vue qui montre les bornes { minX, minY, maxX, maxY } (mètres) avec une marge relative ; sans bornes (plan vierge), les
 * 20 premiers mètres du repère.
 */
export function ajuster(bornes, affichage, marge = 0.05) {
  exigerAffichage(affichage);
  if (!bornes) {
    const l = LARGEUR_DEFAUT;
    const h = l * affichage.hauteur / affichage.largeur;
    return { x: -l * marge, y: -h * marge, largeur: l, hauteur: h };
  }

  const largeur = Math.max(bornes.maxX - bornes.minX, LARGEUR_MIN);
  const hauteur = Math.max(bornes.maxY - bornes.minY, LARGEUR_MIN);
  // Le côté le plus contraint par les proportions de l'écran fixe la largeur visible.
  const visible = Math.max(largeur, hauteur * affichage.largeur / affichage.hauteur) * (1 + 2 * marge);
  return centree({ x: (bornes.minX + bornes.maxX) / 2, y: (bornes.minY + bornes.maxY) / 2 }, visible, affichage);
}

/** Point du repère (mètres) sous une position de l'écran (pixels CSS relatifs au SVG). */
export function versRepere(vue, affichage, ecran) {
  const echelle = echelleVue(vue, affichage);
  return { x: vue.x + exigerNombre(ecran.x) * echelle, y: vue.y + exigerNombre(ecran.y) * echelle };
}

/** Position à l'écran (pixels CSS relatifs au SVG) d'un point du repère. */
export function versEcran(vue, affichage, point) {
  const echelle = echelleVue(vue, affichage);
  return { x: (exigerNombre(point.x) - vue.x) / echelle, y: (exigerNombre(point.y) - vue.y) / echelle };
}

/**
 * Zoom d'un facteur (> 1 : rapproche) autour d'une position de l'écran, qui garde le même point du repère sous elle ;
 * sans position, autour du centre. La largeur visible reste entre 50 cm et 12 km.
 */
export function zoomer(vue, affichage, facteur, ecran) {
  if (!(exigerNombre(facteur) > 0)) {
    throw new RangeError("Facteur de zoom invalide.");
  }

  const pivotEcran = ecran ?? { x: affichage.largeur / 2, y: affichage.hauteur / 2 };
  const pivot = versRepere(vue, affichage, pivotEcran);
  const largeur = borner(vue.largeur / facteur);
  const echelle = largeur / affichage.largeur;
  return { x: pivot.x - pivotEcran.x * echelle, y: pivot.y - pivotEcran.y * echelle, largeur, hauteur: largeur * affichage.hauteur / affichage.largeur };
}

/** Déplacement de la vue qui suit un glissement de dx, dy pixels à l'écran (le plan suit le doigt). */
export function deplacer(vue, affichage, dx, dy) {
  const echelle = echelleVue(vue, affichage);
  return { ...vue, x: vue.x - exigerNombre(dx) * echelle, y: vue.y - exigerNombre(dy) * echelle };
}

/**
 * Pincement à deux doigts : positions des deux pointeurs avant et après (pixels CSS). L'écart entre les doigts règle le
 * zoom, et le point du repère sous leur milieu suit ce milieu (zoom et déplacement d'un même geste).
 */
export function pincer(vue, affichage, avant, apres) {
  const ecartAvant = Math.hypot(avant[1].x - avant[0].x, avant[1].y - avant[0].y);
  const ecartApres = Math.hypot(apres[1].x - apres[0].x, apres[1].y - apres[0].y);
  const milieuAvant = { x: (avant[0].x + avant[1].x) / 2, y: (avant[0].y + avant[1].y) / 2 };
  const milieuApres = { x: (apres[0].x + apres[1].x) / 2, y: (apres[0].y + apres[1].y) / 2 };
  // Doigts confondus : aucun zoom, seulement le déplacement.
  const facteur = ecartAvant > 0 && ecartApres > 0 ? ecartApres / ecartAvant : 1;
  const zoomee = zoomer(vue, affichage, facteur, milieuAvant);
  return deplacer(zoomee, affichage, milieuApres.x - milieuAvant.x, milieuApres.y - milieuAvant.y);
}

/** Zone d'affichage redimensionnée (rotation de la tablette) : même centre, même nombre de mètres par pixel. */
export function redimensionner(vue, avant, apres) {
  const echelle = echelleVue(vue, avant);
  exigerAffichage(apres);
  const centre = { x: vue.x + vue.largeur / 2, y: vue.y + vue.hauteur / 2 };
  return centree(centre, apres.largeur * echelle, apres);
}

/** Attribut viewBox du SVG. */
export function viewBox(vue) {
  return `${vue.x} ${vue.y} ${vue.largeur} ${vue.hauteur}`;
}

/**
 * Pas de la grille métrique et de la barre d'échelle : 1, 2 ou 5 fois une puissance de dix, le plus petit qui fasse au
 * moins `pixelsMin` pixels à l'écran.
 */
export function pasGrille(vue, affichage, pixelsMin = 40) {
  const metres = echelleVue(vue, affichage) * pixelsMin;
  const puissance = 10 ** Math.floor(Math.log10(metres));
  return ([1, 2, 5].find(multiple => multiple * puissance >= metres) ?? 10) * puissance;
}

/** Bornes communes de plusieurs bornes (null ignorés), ou null si aucune. */
export function unirBornes(...liste) {
  const presentes = liste.filter(Boolean);
  if (presentes.length === 0) {
    return null;
  }

  return {
    minX: Math.min(...presentes.map(b => b.minX)),
    minY: Math.min(...presentes.map(b => b.minY)),
    maxX: Math.max(...presentes.map(b => b.maxX)),
    maxY: Math.max(...presentes.map(b => b.maxY)),
  };
}

/** Bornes d'une liste de points { x, y }, ou null si elle est vide. */
export function bornesPoints(points) {
  if (!points || points.length === 0) {
    return null;
  }

  // Une seule boucle, sans tableau intermédiaire : jusqu'à 24 000 points sur un plan chargé.
  const bornes = { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity };
  for (const p of points) {
    bornes.minX = Math.min(bornes.minX, p.x);
    bornes.minY = Math.min(bornes.minY, p.y);
    bornes.maxX = Math.max(bornes.maxX, p.x);
    bornes.maxY = Math.max(bornes.maxY, p.y);
  }

  return bornes;
}

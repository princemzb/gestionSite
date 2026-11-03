// Éditeur de plans de bâtiment (US-21, S21-08, ADR-11) : recalage du fond entre niveaux par deux points homologues
// (décision du porteur du 2 novembre 2026). La personne touche un même point du bâtiment (angle, cage d'escalier) sur le
// calque du niveau inférieur, puis sur le fond du niveau courant, deux fois. L'éditeur en déduit la translation du fond
// courant : ni rotation ni changement d'échelle (l'échelle vient de la calibration) ; si les deux paires donnent des
// distances qui diffèrent de plus de 2 %, il avertit.

import { arrondirAuCentimetre, exigerNombre } from "./mesures.mjs";

/** Écart de distance entre les deux paires au-delà duquel un avertissement s'affiche. */
export const SEUIL_AVERTISSEMENT = 0.02;

/** Distance minimale entre les deux points homologues : en deçà, l'écart des distances n'est pas mesurable. */
export const DISTANCE_MIN = 1;

/** Étapes du recalage, dans l'ordre où les points sont touchés. */
export const ETAPES = Object.freeze(["inferieur1", "courant1", "inferieur2", "courant2"]);

/** Recalage vide : aucun point touché. */
export function creerRecalage() {
  return { points: [] };
}

/** Étape en cours (« inferieur1 », « courant1 », « inferieur2 », « courant2 »), ou null si les quatre points sont posés. */
export function etape(recalage) {
  return ETAPES[recalage.points.length] ?? null;
}

/** Vrai si l'étape en cours se pointe sur le calque du niveau inférieur (sinon sur le fond courant). */
export function surCalque(recalage) {
  return etape(recalage)?.startsWith("inferieur") ?? false;
}

/**
 * Pose le point de l'étape en cours : un point du repère sur le calque du niveau inférieur, ou un point du fond courant
 * exprimé par rapport au coin haut gauche du fond (mètres, sans son décalage). Sans effet une fois les quatre points posés.
 */
export function poserPoint(recalage, point) {
  if (!etape(recalage)) {
    return recalage;
  }

  return { points: [...recalage.points, { x: exigerNombre(point.x), y: exigerNombre(point.y) }] };
}

/** Retire le dernier point posé. */
export function retirerPoint(recalage) {
  return { points: recalage.points.slice(0, -1) };
}

const distance = (a, b) => Math.hypot(b.x - a.x, b.y - a.y);

/**
 * Résultat du recalage une fois les quatre points posés : { decalage (coin haut gauche du fond dans le repère, au
 * centimètre), ecart (écart relatif des distances entre les deux paires), avertissement (au-delà de 2 %) }, ou
 * { defaut } : « incomplet » ou « proches » (points homologues à moins de 1 m l'un de l'autre).
 */
export function calculer(recalage) {
  if (recalage.points.length < ETAPES.length) {
    return { defaut: "incomplet" };
  }

  const [p1, q1, p2, q2] = recalage.points;
  const surCalque = distance(p1, p2);
  const surFond = distance(q1, q2);
  if (surCalque < DISTANCE_MIN || surFond < DISTANCE_MIN) {
    return { defaut: "proches" };
  }

  // Translation moyenne des deux paires : chaque paire donne « point du calque − point du fond ».
  const decalage = {
    x: arrondirAuCentimetre((p1.x - q1.x + p2.x - q2.x) / 2),
    y: arrondirAuCentimetre((p1.y - q1.y + p2.y - q2.y) / 2),
  };
  const ecart = Math.abs(surFond - surCalque) / surCalque;
  return { decalage, ecart, avertissement: ecart > SEUIL_AVERTISSEMENT };
}

const consignes = {
  inferieur1: "Touchez un point repérable sur le calque du niveau inférieur (angle de mur, cage d'escalier).",
  courant1: "Touchez le même point sur le fond de ce niveau.",
  inferieur2: "Touchez un second point sur le calque, loin du premier.",
  courant2: "Touchez ce second point sur le fond de ce niveau.",
};

/** Consigne de l'étape en cours. */
export function consigne(recalage) {
  return consignes[etape(recalage)] ?? "Quatre points posés : validez le recalage ou recommencez.";
}

/** Message d'un défaut de recalage. */
export function messageDefaut(defaut) {
  return defaut === "proches"
    ? "Les deux points homologues sont trop proches : choisissez-les à plus de 1 m l'un de l'autre."
    : "Touchez les quatre points avant de valider.";
}

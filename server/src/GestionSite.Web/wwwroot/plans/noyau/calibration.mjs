// Éditeur de plans de bâtiment (US-21, S21-07, RP-21.4, ADR-11) : calibration d'un fond importé et cote de contrôle.
// Une cote = deux points de l'image (pixels du dérivé d'affichage, origine en haut à gauche) et la distance réelle qui
// les sépare (mètres). Le fond est posé coin haut gauche à l'origine du repère, axes confondus : un pixel vaut
// distance / longueur en pixels mètres. Mêmes règles que le serveur (ValidationPlan), qui reste seul juge.

import { arrondirAuCentimetre, exigerNombre } from "./mesures.mjs";

/** Cote d'au moins 10 px (en deçà, l'imprécision du pointage dépasse 10 %), distance réelle de 0,20 m à 5 km. */
export const COTE_PIXELS_MIN = 10;
export const COTE_MIN = 0.2;
export const BORNE = 5_000;

/** Longueur conseillée pour la cote de calibration (RP-21.4). */
export const COTE_CONSEILLEE = 10;

/** Écart de la cote de contrôle au-delà duquel un avertissement s'affiche (RP-21.4). */
export const SEUIL_AVERTISSEMENT = 0.02;

const nombre = new Intl.NumberFormat("fr-FR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const pourcent = new Intl.NumberFormat("fr-FR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** Longueur d'une cote en pixels de l'image. */
export function longueurPixels(cote) {
  return Math.hypot(exigerNombre(cote.a.x) - exigerNombre(cote.de.x), exigerNombre(cote.a.y) - exigerNombre(cote.de.y));
}

/** Point de l'image au centième de pixel (même arrondi que le serveur), ou null s'il est hors de l'image. */
export function pointImage(point, image) {
  const x = exigerNombre(point.x);
  const y = exigerNombre(point.y);
  if (x < 0 || y < 0 || x > image.largeur || y > image.hauteur) {
    return null;
  }

  return { x: arrondirAuCentimetre(x), y: arrondirAuCentimetre(y) };
}

/** Mètres par pixel de l'image donnés par une cote complète. */
export function metresParPixel(cote) {
  return exigerNombre(cote.distance) / longueurPixels(cote);
}

/**
 * Défaut d'une cote, ou null si elle est complète et valable : « de » ou « a » (point absent ou hors de l'image),
 * « courte » (moins de 10 px), « distance » (absente, ou hors de 0,20 m à 5 km). Contrôlée après arrondi (centième de
 * pixel, centimètre), comme le serveur : une cote acceptée ici l'est aussi à l'enregistrement.
 */
export function defautCote(cote, image) {
  const de = cote?.de ? pointImage(cote.de, image) : null;
  if (!de) {
    return "de";
  }

  const a = cote.a ? pointImage(cote.a, image) : null;
  if (!a) {
    return "a";
  }

  if (longueurPixels({ de, a }) < COTE_PIXELS_MIN) {
    return "courte";
  }

  const distance = cote.distance;
  if (typeof distance !== "number" || !Number.isFinite(distance) || arrondirAuCentimetre(distance) < COTE_MIN || arrondirAuCentimetre(distance) > BORNE) {
    return "distance";
  }

  return null;
}

const messages = {
  de: "Touchez le premier point de la cote sur le fond.",
  a: "Touchez le second point de la cote sur le fond.",
  courte: "Les deux points sont trop proches : choisissez une longueur plus grande (10 m ou plus de préférence).",
  distance: "Saisissez la distance réelle, de 0,20 m à 5 000 m.",
  etendue: "Cette échelle donne un fond de plus de 5 km : vérifiez la distance saisie.",
};

/** Message à l'utilisateur pour un défaut de cote ou de fond. */
export function messageDefaut(defaut) {
  return messages[defaut] ?? "Cote invalide.";
}

/**
 * Défaut du fond calibré (null s'il est valable) : cote de calibration, cote de contrôle facultative, et image calibrée
 * dans les bornes du repère (5 km). Rend { cote: "calibration" | "controle", defaut }.
 */
export function defautFond(fond, image) {
  const calibration = defautCote(fond.calibration, image);
  if (calibration) {
    return { cote: "calibration", defaut: calibration };
  }

  if (Math.max(image.largeur, image.hauteur) * metresParPixel(normaliserCote(fond.calibration, image)) > BORNE) {
    return { cote: "calibration", defaut: "etendue" };
  }

  const controle = fond.controle ? defautCote(fond.controle, image) : null;
  return controle ? { cote: "controle", defaut: controle } : null;
}

/** Cote prête à enregistrer : points au centième de pixel, distance au centimètre. */
export function normaliserCote(cote, image) {
  return { de: pointImage(cote.de, image), a: pointImage(cote.a, image), distance: arrondirAuCentimetre(cote.distance) };
}

/** Longueur d'une cote mesurée sur le plan calibré, en mètres au centimètre. */
export function mesurer(cote, metres) {
  return arrondirAuCentimetre(longueurPixels(cote) * exigerNombre(metres));
}

/**
 * Cote de contrôle : longueur mesurée sur le plan calibré, longueur déclarée (mesurée sur place), écart relatif et
 * avertissement au-delà de 2 % (RP-21.4).
 */
export function ecartControle(controle, metres) {
  const mesuree = mesurer(controle, metres);
  const declaree = exigerNombre(controle.distance);
  const ecart = Math.abs(mesuree - declaree) / declaree;
  return { mesuree, declaree, ecart, avertissement: ecart > SEUIL_AVERTISSEMENT };
}

/**
 * Échelle déclarée d'un PDF (1:N) : mètres par pixel du dérivé proposés (CONTRATS : largeur de page en mm × N / 1000 /
 * largeur du dérivé), simple proposition à confirmer par une cote mesurée ; null sans échelle déclarée.
 */
export function metresParPixelDeclares(fond) {
  const pdf = fond?.pdf;
  if (!pdf?.echelle || !pdf.largeurPageMm || !fond.largeurAffichage) {
    return null;
  }

  return pdf.largeurPageMm * pdf.echelle / 1000 / fond.largeurAffichage;
}

/** Distance proposée pour une cote d'après l'échelle déclarée du PDF, au centimètre, ou null. */
export function distanceProposee(cote, fond) {
  const metres = metresParPixelDeclares(fond);
  return metres && cote?.de && cote.a ? arrondirAuCentimetre(longueurPixels(cote) * metres) : null;
}

/** Distance saisie (« 14,33 », « 14.33 m », « 1 234,5 ») en mètres, ou null si le texte n'est pas une distance. */
export function lireDistance(texte) {
  if (typeof texte !== "string") {
    return null;
  }

  const net = texte.replace(/\s/g, "").replace(/m$/i, "").replace(",", ".");
  if (!/^\d+(\.\d+)?$/.test(net)) {
    return null;
  }

  return Number(net);
}

/** « 14,33 m ». */
export function formaterMetres(metres) {
  return `${nombre.format(exigerNombre(metres))} m`;
}

/** « 0,6 % » (écart relatif donné en fraction). */
export function formaterEcart(ecart) {
  return `${pourcent.format(exigerNombre(ecart) * 100)} %`;
}

/** Point de l'image (pixels) dans le repère (mètres). */
export function pixelVersRepere(point, metres) {
  return { x: exigerNombre(point.x) * metres, y: exigerNombre(point.y) * metres };
}

/** Point du repère (mètres) en pixels de l'image. */
export function repereVersPixel(point, metres) {
  return { x: exigerNombre(point.x) / metres, y: exigerNombre(point.y) / metres };
}

/** Bornes du fond calibré dans le repère (coin haut gauche à l'origine). */
export function bornesFond(image, metres) {
  return { minX: 0, minY: 0, maxX: image.largeur * metres, maxY: image.hauteur * metres };
}

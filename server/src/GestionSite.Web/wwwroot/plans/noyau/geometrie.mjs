// Éditeur de plans de bâtiment (US-21, S21-08, ADR-11) : géométrie des contours en mètres, au centimètre près.
// Mêmes contrôles que le serveur (ValidationPlan, NetTopologySuite), qui reste seul juge : polygone simple de 3 à 500
// sommets, sans sommet répété, d'au moins 0,25 m², plus large que 5 cm ; deux pièces ne se chevauchent pas de plus de
// 5 cm ; ouverture posée sur une arête à 5 cm près. Les tests d'intersection se font en centimètres entiers (calcul exact).

import { arrondirAuCentimetre, exigerNombre } from "./mesures.mjs";

export const BORNE = 5_000;
export const SOMMETS_MAX = 500;
export const AIRE_MIN = 0.25;
/** Chevauchement admis entre deux pièces (mur commun, imprécision du tracé) et largeur minimale d'un contour. */
export const CHEVAUCHEMENT_MAX = 0.05;
export const TOLERANCE_ARETE = 0.05;

// Précision de la recherche de l'endroit le plus « profond » d'un contour (un demi-millimètre).
const PRECISION = 0.0005;

/** Point du repère au centimètre ; null s'il n'est pas fini ou sort du repère (±5 km). */
export function arrondirPoint(point) {
  if (!point || typeof point.x !== "number" || typeof point.y !== "number" || !Number.isFinite(point.x) || !Number.isFinite(point.y)) {
    return null;
  }

  const p = { x: arrondirAuCentimetre(point.x), y: arrondirAuCentimetre(point.y) };
  return Math.abs(p.x) <= BORNE && Math.abs(p.y) <= BORNE ? p : null;
}

/** Points égaux au centimètre. */
export function memePoint(a, b) {
  return Math.round(a.x * 100) === Math.round(b.x * 100) && Math.round(a.y * 100) === Math.round(b.y * 100);
}

/** Projection d'un point sur un segment : point projeté, position t (0 en « de », 1 en « a ») et distance. */
export function projeter(point, de, a) {
  const dx = a.x - de.x;
  const dy = a.y - de.y;
  const carre = dx * dx + dy * dy;
  const t = carre === 0 ? 0 : Math.min(1, Math.max(0, ((point.x - de.x) * dx + (point.y - de.y) * dy) / carre));
  const projete = { x: de.x + t * dx, y: de.y + t * dy };
  return { point: projete, t, distance: Math.hypot(point.x - projete.x, point.y - projete.y) };
}

/** Arêtes d'un contour fermé : [de, a] pour chaque sommet et le suivant. */
export function aretes(contour) {
  return contour.map((p, i) => [p, contour[(i + 1) % contour.length]]);
}

/** Longueur d'un segment en mètres (non arrondie). */
export function longueur(de, a) {
  return Math.hypot(a.x - de.x, a.y - de.y);
}

/** Périmètre d'un contour fermé, au centimètre. */
export function perimetre(contour) {
  return arrondirAuCentimetre(aretes(contour).reduce((total, [de, a]) => total + longueur(de, a), 0));
}

/** Aire d'un contour (m², formule du lacet). */
export function aire(contour) {
  let double = 0;
  for (let i = 0; i < contour.length; i++) {
    const p = contour[i];
    const q = contour[(i + 1) % contour.length];
    double += p.x * q.y - q.x * p.y;
  }

  return Math.abs(double) / 2;
}

/** Vrai si le point est à l'intérieur du contour (règle du rayon ; un point du bord peut tomber d'un côté ou de l'autre). */
export function contient(contour, point) {
  let dedans = false;
  for (let i = 0, j = contour.length - 1; i < contour.length; j = i++) {
    const a = contour[i];
    const b = contour[j];
    if ((a.y > point.y) !== (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) {
      dedans = !dedans;
    }
  }

  return dedans;
}

/** Distance d'un point au bord d'un contour fermé. */
export function distanceBord(contour, point) {
  let minimum = Infinity;
  for (const [de, a] of aretes(contour)) {
    minimum = Math.min(minimum, projeter(point, de, a).distance);
  }

  return minimum;
}

// Distance signée au bord : positive à l'intérieur, négative à l'extérieur (fonction 1-lipschitzienne).
function profondeur(contour, point) {
  const d = distanceBord(contour, point);
  return contient(contour, point) ? d : -d;
}

function bornes(contour) {
  const b = { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity };
  for (const p of contour) {
    b.minX = Math.min(b.minX, p.x);
    b.minY = Math.min(b.minY, p.y);
    b.maxX = Math.max(b.maxX, p.x);
    b.maxY = Math.max(b.maxY, p.y);
  }

  return b;
}

/**
 * Vrai s'il existe un point du rectangle où `f` (1-lipschitzienne) dépasse `seuil` : séparation et évaluation sur des
 * carrés (comme la recherche du « pôle d'inaccessibilité ») ; un carré dont le maximum possible ne dépasse pas le seuil
 * de plus d'un demi-millimètre est écarté.
 */
export function depasse(f, rectangle, seuil) {
  const largeur = rectangle.maxX - rectangle.minX;
  const hauteur = rectangle.maxY - rectangle.minY;
  // Un disque de rayon supérieur au seuil ne tient pas dans un rectangle plus étroit que son diamètre.
  if (!(Math.min(largeur, hauteur) > 2 * seuil)) {
    return false;
  }

  const cote = Math.min(largeur, hauteur);
  const pile = [];
  for (let x = rectangle.minX; x < rectangle.maxX + cote / 2; x += cote) {
    for (let y = rectangle.minY; y < rectangle.maxY + cote / 2; y += cote) {
      pile.push({ x: x + cote / 2, y: y + cote / 2, demi: cote / 2 });
    }
  }

  while (pile.length > 0) {
    const carre = pile.pop();
    const valeur = f(carre);
    if (valeur > seuil) {
      return true;
    }

    if (valeur + carre.demi * Math.SQRT2 > seuil + PRECISION) {
      const d = carre.demi / 2;
      pile.push({ x: carre.x - d, y: carre.y - d, demi: d }, { x: carre.x + d, y: carre.y - d, demi: d },
        { x: carre.x - d, y: carre.y + d, demi: d }, { x: carre.x + d, y: carre.y + d, demi: d });
    }
  }

  return false;
}

/** Vrai si le contour est plus large que 5 cm quelque part (réduit de 2,5 cm, il ne disparaît pas). */
export function assezLarge(contour) {
  return depasse(p => profondeur(contour, p), bornes(contour), CHEVAUCHEMENT_MAX / 2);
}

/**
 * Vrai si deux contours se chevauchent de plus de 5 cm : réduits chacun de 2,5 cm, ils se rencontrent encore (la
 * réduction de l'intersection est l'intersection des réductions : on cherche un point à plus de 2,5 cm des deux bords).
 */
export function chevauchent(a, b) {
  const ba = bornes(a);
  const bb = bornes(b);
  const commun = { minX: Math.max(ba.minX, bb.minX), minY: Math.max(ba.minY, bb.minY), maxX: Math.min(ba.maxX, bb.maxX), maxY: Math.min(ba.maxY, bb.maxY) };
  return depasse(p => Math.min(profondeur(a, p), profondeur(b, p)), commun, CHEVAUCHEMENT_MAX / 2);
}

// Orientation de trois points en centimètres entiers : 1, -1 ou 0 (alignés).
function orientation(p, q, r) {
  const v = (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
  return Math.sign(v);
}

function surSegment(p, q, r) {
  return Math.min(p.x, r.x) <= q.x && q.x <= Math.max(p.x, r.x) && Math.min(p.y, r.y) <= q.y && q.y <= Math.max(p.y, r.y);
}

/** Vrai si les segments [a, b] et [c, d] (centimètres entiers) se rencontrent, contact compris. */
export function seCoupent(a, b, c, d) {
  const o1 = orientation(a, b, c);
  const o2 = orientation(a, b, d);
  const o3 = orientation(c, d, a);
  const o4 = orientation(c, d, b);
  if (o1 !== o2 && o3 !== o4) {
    return true;
  }

  return (o1 === 0 && surSegment(a, c, b)) || (o2 === 0 && surSegment(a, d, b)) || (o3 === 0 && surSegment(c, a, d)) || (o4 === 0 && surSegment(c, b, d));
}

const centimetres = p => ({ x: Math.round(p.x * 100), y: Math.round(p.y * 100) });

/**
 * Vrai si le contour fermé est simple : deux arêtes non voisines ne se touchent pas, deux arêtes voisines ne repartent
 * pas l'une sur l'autre (pointe aplatie).
 */
export function estSimple(contour) {
  const p = contour.map(centimetres);
  const n = p.length;
  for (let i = 0; i < n; i++) {
    const [a, b, c] = [p[i], p[(i + 1) % n], p[(i + 2) % n]];
    // Arêtes voisines [a, b] et [b, c] alignées et de sens opposés : elles se recouvrent.
    if (orientation(a, b, c) === 0 && (a.x - b.x) * (c.x - b.x) + (a.y - b.y) * (c.y - b.y) > 0) {
      return false;
    }

    for (let j = i + 2; j < n; j++) {
      if (i === 0 && j === n - 1) {
        continue; // première et dernière arêtes : voisines par le premier sommet
      }

      if (seCoupent(a, b, p[j], p[(j + 1) % n])) {
        return false;
      }
    }
  }

  return true;
}

/**
 * Défaut d'un contour (au centimètre), ou null : « sommets » (moins de 3 ou plus de 500), « bornes » (point hors du
 * repère), « repete » (deux sommets consécutifs confondus), « croise » (contour qui se croise ou se touche), « aire »
 * (moins de 0,25 m²), « etroit » (pas plus large que 5 cm).
 */
export function defautContour(contour) {
  if (!Array.isArray(contour) || contour.length < 3 || contour.length > SOMMETS_MAX) {
    return "sommets";
  }

  const points = contour.map(arrondirPoint);
  if (points.some(p => p === null)) {
    return "bornes";
  }

  if (points.some((p, i) => memePoint(p, points[(i + 1) % points.length]))) {
    return "repete";
  }

  if (!estSimple(points)) {
    return "croise";
  }

  if (aire(points) < AIRE_MIN) {
    return "aire";
  }

  return assezLarge(points) ? null : "etroit";
}

/** Vrai si le segment [de, a] est posé sur une arête du contour, à 5 cm près (ses deux extrémités). */
export function surUneArete(contour, de, a) {
  return aretes(contour).some(([p, q]) => projeter(de, p, q).distance <= TOLERANCE_ARETE + 1e-9 && projeter(a, p, q).distance <= TOLERANCE_ARETE + 1e-9);
}

/** Arête du contour la plus proche d'un point, à `tolerance` mètres au plus : { index, t, point, distance } ou null. */
export function areteProche(contour, point, tolerance) {
  let meilleure = null;
  aretes(contour).forEach(([de, a], index) => {
    const projection = projeter(point, de, a);
    if (projection.distance <= tolerance && (!meilleure || projection.distance < meilleure.distance)) {
      meilleure = { index, ...projection };
    }
  });
  return meilleure;
}

/** Contour translaté de (dx, dy), au centimètre. */
export function translater(points, dx, dy) {
  exigerNombre(dx);
  exigerNombre(dy);
  return points.map(p => ({ x: arrondirAuCentimetre(p.x + dx), y: arrondirAuCentimetre(p.y + dy) }));
}

// Éditeur de plans de bâtiment (US-21, ADR-11) : fonctions pures du noyau, sans accès au DOM ni au réseau,
// testées par node:test (server/tests/js) et couvertes dans la porte Sonar.
// Géométrie en mètres dans le repère du bâtiment, au centimètre près (RP-21.2).

/** Nombre fini exigé : ni NaN, ni infini, ni valeur d'un autre type (null, texte) convertie en silence. */
export function exigerNombre(valeur) {
  if (typeof valeur !== "number" || !Number.isFinite(valeur)) {
    throw new RangeError("Valeur invalide : nombre fini attendu.");
  }

  return valeur;
}

/**
 * Arrondit une longueur en mètres au centimètre le plus proche, le demi-centimètre s'éloignant de zéro (même règle
 * pour une coordonnée négative, jamais -0). Le calcul porte sur la valeur binaire, comme côté serveur
 * (Math.Round(x * 100, MidpointRounding.AwayFromZero) / 100) : client et serveur arrondissent à l'identique.
 */
export function arrondirAuCentimetre(metres) {
  const centimetres = Math.round(Math.abs(exigerNombre(metres)) * 100);
  return centimetres === 0 ? 0 : Math.sign(metres) * centimetres / 100;
}

/** Distance en mètres entre deux points { x, y } du repère, arrondie au centimètre. */
export function distance(a, b) {
  return arrondirAuCentimetre(Math.hypot(exigerNombre(b.x) - exigerNombre(a.x), exigerNombre(b.y) - exigerNombre(a.y)));
}

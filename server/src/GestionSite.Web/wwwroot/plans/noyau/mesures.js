// Éditeur de plans de bâtiment (US-21, ADR-11) : fonctions pures du noyau, sans accès au DOM ni au réseau,
// testées par node:test (server/tests/js) et couvertes dans la porte Sonar.
// Géométrie en mètres dans le repère du bâtiment, au centimètre près (RP-21.2).

/** Arrondit une longueur en mètres au centimètre ; refuse NaN et l'infini. */
export function arrondirAuCentimetre(metres) {
  if (!Number.isFinite(metres)) {
    throw new RangeError("Longueur invalide : nombre fini attendu.");
  }

  return Math.round(metres * 100) / 100;
}

/** Distance en mètres entre deux points { x, y } du repère, arrondie au centimètre. */
export function distance(a, b) {
  return arrondirAuCentimetre(Math.hypot(b.x - a.x, b.y - a.y));
}

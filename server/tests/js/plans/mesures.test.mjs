// Tests du noyau de l'éditeur de plans de bâtiment (ADR-11) : node:test, sans dépendance.
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { arrondirAuCentimetre, distance } from "../../../src/GestionSite.Web/wwwroot/plans/noyau/mesures.js";

describe("arrondirAuCentimetre", () => {
  it("arrondit au centimètre le plus proche", () => {
    assert.equal(arrondirAuCentimetre(12.004), 12);
    assert.equal(arrondirAuCentimetre(12.006), 12.01);
    assert.equal(arrondirAuCentimetre(-3.456), -3.46);
  });

  it("refuse NaN et l'infini", () => {
    assert.throws(() => arrondirAuCentimetre(Number.NaN), RangeError);
    assert.throws(() => arrondirAuCentimetre(Number.POSITIVE_INFINITY), RangeError);
  });
});

describe("distance", () => {
  it("mesure la distance entre deux points du repère, en mètres", () => {
    assert.equal(distance({ x: 0, y: 0 }, { x: 3, y: 4 }), 5);
    assert.equal(distance({ x: 1.5, y: 2 }, { x: 1.5, y: 14 }), 12);
  });

  it("refuse une coordonnée non numérique", () => {
    assert.throws(() => distance({ x: 0, y: 0 }, { x: Number.NaN, y: 1 }), RangeError);
  });
});

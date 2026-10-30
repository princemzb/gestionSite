// Tests du noyau de l'éditeur de plans de bâtiment (ADR-11) : node:test, sans dépendance.
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { arrondirAuCentimetre, distance, exigerNombre } from "../../../src/GestionSite.Web/wwwroot/plans/noyau/mesures.mjs";

describe("exigerNombre", () => {
  it("rend un nombre fini tel quel", () => {
    assert.equal(exigerNombre(-2.5), -2.5);
  });

  it("refuse NaN, l'infini et toute valeur d'un autre type", () => {
    for (const valeur of [Number.NaN, Number.POSITIVE_INFINITY, null, undefined, "3", true]) {
      assert.throws(() => exigerNombre(valeur), RangeError);
    }
  });
});

describe("arrondirAuCentimetre", () => {
  it("arrondit au centimètre le plus proche", () => {
    assert.equal(arrondirAuCentimetre(12.004), 12);
    assert.equal(arrondirAuCentimetre(12.006), 12.01);
    assert.equal(arrondirAuCentimetre(-3.456), -3.46);
  });

  it("traite une coordonnée négative comme sa symétrique, sans -0", () => {
    assert.equal(arrondirAuCentimetre(1.125), 1.13);
    assert.equal(arrondirAuCentimetre(-1.125), -1.13);
    assert.ok(Object.is(arrondirAuCentimetre(-0.001), 0));
  });

  it("arrondit la valeur binaire, comme le serveur (1,005 vaut 1,00499… en binaire)", () => {
    assert.equal(arrondirAuCentimetre(1.005), 1);
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

  it("refuse une coordonnée non numérique, sans conversion silencieuse", () => {
    assert.throws(() => distance({ x: 0, y: 0 }, { x: Number.NaN, y: 1 }), RangeError);
    assert.throws(() => distance({ x: 0, y: 0 }, { x: null, y: 4 }), RangeError);
    assert.throws(() => distance({ x: 0, y: 0 }, { x: "3", y: 4 }), RangeError);
  });
});

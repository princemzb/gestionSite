// Tests d'annuler et rétablir de l'éditeur de plans de bâtiment (S21-07, ADR-11).
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  LIMITE_HISTORIQUE, annuler, appliquer, creerHistorique, peutAnnuler, peutRetablir, reinitialiser, retablir,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/historique.mjs";

describe("historique", () => {
  it("commence sans rien à annuler ni à rétablir", () => {
    const h = creerHistorique({ n: 0 });
    assert.equal(h.limite, LIMITE_HISTORIQUE);
    assert.ok(!peutAnnuler(h) && !peutRetablir(h));
    assert.equal(annuler(h), h);
    assert.equal(retablir(h), h);
  });

  it("refuse une limite invalide", () => {
    assert.throws(() => creerHistorique({}, 0), RangeError);
    assert.throws(() => creerHistorique({}, 1.5), RangeError);
  });

  it("annule et rétablit dans l'ordre, sans modifier les états", () => {
    const a = { n: 1 };
    const b = { n: 2 };
    const c = { n: 3 };
    let h = appliquer(appliquer(creerHistorique(a), b), c);
    h = annuler(h);
    assert.equal(h.present, b);
    h = annuler(h);
    assert.equal(h.present, a);
    assert.ok(peutRetablir(h));
    h = retablir(h);
    assert.equal(h.present, b);
    h = retablir(h);
    assert.equal(h.present, c);
    assert.deepEqual([a, b, c].map(e => e.n), [1, 2, 3]);
  });

  it("une modification après une annulation vide l'avenir", () => {
    let h = appliquer(creerHistorique({ n: 1 }), { n: 2 });
    h = appliquer(annuler(h), { n: 3 });
    assert.ok(!peutRetablir(h));
    assert.equal(h.passe.length, 1);
  });

  it("ignore un état identique au présent", () => {
    const h = creerHistorique({ n: 1 });
    assert.equal(appliquer(h, h.present), h);
  });

  it("oublie les étapes au-delà de la limite", () => {
    let h = creerHistorique(0, 3);
    for (let i = 1; i <= 5; i++) {
      h = appliquer(h, i);
    }

    assert.deepEqual(h.passe, [2, 3, 4]);
  });

  it("repart d'un état enregistré avec la même limite", () => {
    const h = reinitialiser(appliquer(creerHistorique(0, 7), 1), 9);
    assert.equal(h.present, 9);
    assert.equal(h.limite, 7);
    assert.ok(!peutAnnuler(h));
  });
});

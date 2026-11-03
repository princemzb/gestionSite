// Tests du recalage du fond entre niveaux par deux points homologues (S21-08, décision du porteur du 02/11).
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  calculer, consigne, creerRecalage, etape, messageDefaut, poserPoint, retirerPoint, surCalque,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/recalage.mjs";

function poser(points) {
  return points.reduce((r, p) => poserPoint(r, p), creerRecalage());
}

describe("recalage par deux points homologues", () => {
  it("enchaîne calque, fond, calque, fond", () => {
    let r = creerRecalage();
    assert.equal(etape(r), "inferieur1");
    assert.ok(surCalque(r));
    assert.match(consigne(r), /calque du niveau inférieur/);
    r = poserPoint(r, { x: 1, y: 1 });
    assert.equal(etape(r), "courant1");
    assert.ok(!surCalque(r));
    r = poser([{ x: 1, y: 1 }, { x: 0, y: 0 }, { x: 11, y: 1 }, { x: 10, y: 0 }]);
    assert.equal(etape(r), null);
    assert.ok(!surCalque(r));
    assert.equal(poserPoint(r, { x: 5, y: 5 }), r);
    assert.match(consigne(r), /validez/);
    assert.equal(etape(retirerPoint(r)), "courant2");
    assert.throws(() => poserPoint(creerRecalage(), { x: Number.NaN, y: 0 }), RangeError);
  });

  it("déduit la translation moyenne, au centimètre, sans avertissement sous 2 %", () => {
    // Fond du sous-sol décalé de (2,5 m ; -1,2 m) par rapport au calque, à 1 cm près sur la seconde paire.
    const r = poser([{ x: 3, y: 0 }, { x: 0.5, y: 1.2 }, { x: 13, y: 8 }, { x: 10.51, y: 9.2 }]);
    const resultat = calculer(r);
    assert.deepEqual(resultat.decalage, { x: 2.5, y: -1.2 });
    assert.ok(resultat.ecart < 0.001);
    assert.equal(resultat.avertissement, false);
  });

  it("avertit au-delà de 2 % d'écart entre les distances des deux paires", () => {
    const resultat = calculer(poser([{ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10.3, y: 0 }]));
    assert.equal(resultat.avertissement, true);
    assert.ok(Math.abs(resultat.ecart - 0.03) < 1e-9);
  });

  it("refuse un recalage incomplet ou des points trop proches", () => {
    assert.deepEqual(calculer(poser([{ x: 0, y: 0 }])), { defaut: "incomplet" });
    assert.deepEqual(calculer(poser([{ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 0.5, y: 0 }, { x: 0.5, y: 0 }])), { defaut: "proches" });
    assert.match(messageDefaut("proches"), /plus de 1 m/);
    assert.match(messageDefaut("incomplet"), /quatre points/);
  });
});

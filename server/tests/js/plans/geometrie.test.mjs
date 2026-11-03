// Tests de la géométrie des contours (S21-08, ADR-11) : mêmes seuils que le serveur (ValidationPlan).
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  aire, areteProche, aretes, arrondirPoint, assezLarge, chevauchent, contient, defautContour, depasse, distanceBord, estSimple, longueur,
  memePoint, perimetre, projeter, seCoupent, surUneArete, translater,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/geometrie.mjs";

const carre = (x, y, cote) => [{ x, y }, { x: x + cote, y }, { x: x + cote, y: y + cote }, { x, y: y + cote }];
const rectangle = (x, y, l, h) => [{ x, y }, { x: x + l, y }, { x: x + l, y: y + h }, { x, y: y + h }];

describe("points et segments", () => {
  it("arrondit au centimètre dans le repère, refuse le reste", () => {
    assert.deepEqual(arrondirPoint({ x: 1.234, y: -2.006 }), { x: 1.23, y: -2.01 });
    assert.equal(arrondirPoint({ x: 5000.01, y: 0 }), null);
    assert.equal(arrondirPoint({ x: Number.NaN, y: 0 }), null);
    assert.equal(arrondirPoint({ x: "1", y: 0 }), null);
    assert.equal(arrondirPoint(null), null);
    assert.ok(memePoint({ x: 1.001, y: 2 }, { x: 1.004, y: 2 }));
    assert.ok(!memePoint({ x: 1, y: 2 }, { x: 1.01, y: 2 }));
  });

  it("projette sur un segment, bornée à ses extrémités", () => {
    const de = { x: 0, y: 0 };
    const a = { x: 10, y: 0 };
    assert.deepEqual(projeter({ x: 4, y: 3 }, de, a), { point: { x: 4, y: 0 }, t: 0.4, distance: 3 });
    assert.equal(projeter({ x: -5, y: 0 }, de, a).t, 0);
    assert.equal(projeter({ x: 15, y: 0 }, de, a).t, 1);
    assert.equal(projeter({ x: 3, y: 4 }, de, de).distance, 5); // segment nul
    assert.equal(longueur(de, { x: 3, y: 4 }), 5);
  });

  it("détecte les segments qui se coupent ou se touchent", () => {
    const p = (x, y) => ({ x, y });
    assert.ok(seCoupent(p(0, 0), p(10, 10), p(0, 10), p(10, 0)));
    assert.ok(!seCoupent(p(0, 0), p(10, 0), p(0, 1), p(10, 1)));
    assert.ok(seCoupent(p(0, 0), p(10, 0), p(5, 0), p(5, 5))); // contact en T
    assert.ok(seCoupent(p(0, 0), p(10, 0), p(5, 0), p(20, 0))); // recouvrement
    assert.ok(seCoupent(p(5, 0), p(20, 0), p(0, 0), p(10, 0)));
    assert.ok(seCoupent(p(5, 5), p(5, 0), p(0, 0), p(10, 0)));
    assert.ok(seCoupent(p(0, 0), p(10, 0), p(10, 0), p(10, 5)));
    assert.ok(!seCoupent(p(0, 0), p(10, 0), p(11, 0), p(20, 0)));
  });
});

describe("mesures d'un contour", () => {
  it("aire, périmètre, arêtes et appartenance", () => {
    const c = rectangle(0, 0, 4, 3);
    assert.equal(aire(c), 12);
    assert.equal(perimetre(c), 14);
    assert.equal(aretes(c).length, 4);
    assert.deepEqual(aretes(c)[3], [{ x: 0, y: 3 }, { x: 0, y: 0 }]);
    assert.ok(contient(c, { x: 2, y: 1 }));
    assert.ok(!contient(c, { x: 5, y: 1 }));
    assert.equal(distanceBord(c, { x: 2, y: 1 }), 1);
  });

  it("trouve l'arête la plus proche dans la tolérance", () => {
    const c = rectangle(0, 0, 4, 3);
    assert.deepEqual(areteProche(c, { x: 2, y: 0.1 }, 0.2), { index: 0, point: { x: 2, y: 0 }, t: 0.5, distance: 0.1 });
    assert.equal(areteProche(c, { x: 2, y: 1.5 }, 0.2), null);
    assert.ok(surUneArete(c, { x: 1, y: 0.05 }, { x: 2, y: -0.05 }));
    assert.ok(!surUneArete(c, { x: 1, y: 0.06 }, { x: 2, y: 0 }));
  });

  it("translate au centimètre et refuse une translation non numérique", () => {
    assert.deepEqual(translater([{ x: 1, y: 1 }], 0.104, -2), [{ x: 1.1, y: -1 }]);
    assert.throws(() => translater([], Number.NaN, 0), RangeError);
  });
});

describe("contours valables (comme le serveur)", () => {
  it("accepte un rectangle et un triangle", () => {
    assert.equal(defautContour(rectangle(0, 0, 4, 3)), null);
    assert.equal(defautContour([{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 0, y: 2 }]), null);
  });

  it("refuse trop peu ou trop de sommets, un point hors du repère, un sommet répété", () => {
    assert.equal(defautContour([{ x: 0, y: 0 }, { x: 1, y: 0 }]), "sommets");
    assert.equal(defautContour(null), "sommets");
    assert.equal(defautContour(Array.from({ length: 501 }, (_, k) => ({ x: Math.cos(k), y: Math.sin(k) }))), "sommets");
    assert.equal(defautContour([{ x: 0, y: 0 }, { x: 6000, y: 0 }, { x: 0, y: 2 }]), "bornes");
    assert.equal(defautContour([{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 2.001, y: 0 }, { x: 0, y: 2 }]), "repete");
  });

  it("refuse un contour qui se croise, se touche ou repart sur lui-même", () => {
    assert.equal(defautContour([{ x: 0, y: 0 }, { x: 4, y: 4 }, { x: 4, y: 0 }, { x: 0, y: 4 }]), "croise");
    // Sommet posé sur une autre arête (contour qui se touche).
    assert.equal(defautContour([{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 4 }, { x: 2, y: 0 }, { x: 0, y: 4 }]), "croise");
    // Pointe aplatie : l'arête suivante repart sur la précédente.
    assert.equal(defautContour([{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 2, y: 0 }, { x: 2, y: 3 }]), "croise");
    assert.ok(estSimple([{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 3 }])); // sommet aligné, sans retour
  });

  it("refuse moins de 0,25 m² et un contour pas plus large que 5 cm", () => {
    assert.equal(defautContour(carre(0, 0, 0.49)), "aire");
    assert.equal(defautContour(rectangle(0, 0, 10, 0.05)), "etroit");
    assert.equal(defautContour(rectangle(0, 0, 10, 0.06)), null);
    assert.ok(!assezLarge(rectangle(0, 0, 10, 0.04)));
  });
});

describe("chevauchement de deux pièces au-delà de 5 cm", () => {
  it("admet un mur commun et une imprécision jusqu'à 5 cm", () => {
    assert.ok(!chevauchent(rectangle(0, 0, 4, 4), rectangle(4, 0, 4, 4)));
    assert.ok(!chevauchent(rectangle(0, 0, 4, 4), rectangle(3.96, 0, 4, 4)));
    assert.ok(!chevauchent(rectangle(0, 0, 4, 4), rectangle(3.95, 0, 4, 4)));
    assert.ok(!chevauchent(rectangle(0, 0, 4, 4), rectangle(10, 10, 4, 4)));
  });

  it("refuse un recouvrement de plus de 5 cm, ou une pièce dans une autre", () => {
    assert.ok(chevauchent(rectangle(0, 0, 4, 4), rectangle(3.94, 0, 4, 4)));
    assert.ok(chevauchent(rectangle(0, 0, 10, 10), rectangle(2, 2, 1, 1)));
    // Pièces en L imbriquées : recouvrement dans le coin seulement.
    const l = [{ x: 0, y: 0 }, { x: 6, y: 0 }, { x: 6, y: 2 }, { x: 2, y: 2 }, { x: 2, y: 6 }, { x: 0, y: 6 }];
    assert.ok(!chevauchent(l, rectangle(2, 2, 4, 4)));
    assert.ok(chevauchent(l, rectangle(1.9, 1.9, 4, 4)));
  });

  it("cherche un point au-delà d'un seuil sur un rectangle", () => {
    assert.ok(!depasse(() => 1, { minX: 0, minY: 0, maxX: 1, maxY: 0.01 }, 0.025)); // trop étroit pour le disque
    assert.ok(depasse(p => -Math.hypot(p.x - 0.3, p.y - 0.7) + 0.5, { minX: 0, minY: 0, maxX: 2, maxY: 1 }, 0.45));
    assert.ok(!depasse(p => -Math.hypot(p.x - 0.3, p.y - 0.7) + 0.5, { minX: 0, minY: 0, maxX: 2, maxY: 1 }, 0.6));
  });
});

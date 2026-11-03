// Tests de la vue SVG de l'éditeur de plans de bâtiment (S21-07, ADR-11) : node:test, sans dépendance.
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  LARGEUR_DEFAUT, LARGEUR_MAX, LARGEUR_MIN, ajuster, bornesPoints, deplacer, echelleVue, exigerAffichage, pasGrille, pincer,
  redimensionner, unirBornes, versEcran, versRepere, viewBox, zoomer,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/vue.mjs";

const ecran = { largeur: 800, hauteur: 600 };
const proche = (reel, attendu, nom = "") => assert.ok(Math.abs(reel - attendu) < 1e-9, `${nom} ${reel} ≠ ${attendu}`);

describe("zone d'affichage", () => {
  it("refuse une taille nulle, négative ou non numérique", () => {
    for (const affichage of [null, { largeur: 0, hauteur: 600 }, { largeur: 800, hauteur: -1 }, { largeur: "800", hauteur: 600 }]) {
      assert.throws(() => exigerAffichage(affichage), RangeError);
    }
  });

  it("donne les mètres par pixel de la vue", () => {
    assert.equal(echelleVue({ x: 0, y: 0, largeur: 40, hauteur: 30 }, ecran), 0.05);
  });
});

describe("ajuster", () => {
  it("montre les 20 premiers mètres d'un plan vierge, aux proportions de l'écran", () => {
    const vue = ajuster(null, ecran);
    assert.equal(vue.largeur, LARGEUR_DEFAUT);
    assert.equal(vue.hauteur, 15);
    assert.ok(vue.x < 0 && vue.y < 0);
  });

  it("cadre un fond large sur sa largeur et un fond haut sur sa hauteur, avec une marge", () => {
    const large = ajuster({ minX: 0, minY: 0, maxX: 40, maxY: 10 }, ecran, 0);
    assert.deepEqual(large, { x: 0, y: -10, largeur: 40, hauteur: 30 });
    const haut = ajuster({ minX: 0, minY: 0, maxX: 10, maxY: 30 }, ecran, 0.1);
    proche(haut.hauteur, 36);
    proche(haut.x + haut.largeur / 2, 5);
  });

  it("garde une largeur minimale pour un contenu ponctuel", () => {
    assert.ok(ajuster({ minX: 3, minY: 3, maxX: 3, maxY: 3 }, ecran).largeur >= LARGEUR_MIN);
  });
});

describe("conversions", () => {
  const vue = { x: 10, y: 20, largeur: 40, hauteur: 30 };

  it("convertit l'écran en repère et inversement", () => {
    assert.deepEqual(versRepere(vue, ecran, { x: 400, y: 300 }), { x: 30, y: 35 });
    assert.deepEqual(versEcran(vue, ecran, { x: 30, y: 35 }), { x: 400, y: 300 });
  });

  it("refuse une position non numérique", () => {
    assert.throws(() => versRepere(vue, ecran, { x: Number.NaN, y: 0 }), RangeError);
  });

  it("écrit l'attribut viewBox", () => {
    assert.equal(viewBox(vue), "10 20 40 30");
  });
});

describe("zoomer", () => {
  const vue = { x: 0, y: 0, largeur: 40, hauteur: 30 };

  it("garde le point du repère sous le pointeur", () => {
    const sous = versRepere(vue, ecran, { x: 200, y: 150 });
    const zoomee = zoomer(vue, ecran, 2, { x: 200, y: 150 });
    assert.equal(zoomee.largeur, 20);
    assert.deepEqual(versRepere(zoomee, ecran, { x: 200, y: 150 }), sous);
  });

  it("zoome autour du centre sans pointeur", () => {
    const zoomee = zoomer(vue, ecran, 0.5);
    assert.deepEqual(versRepere(zoomee, ecran, { x: 400, y: 300 }), { x: 20, y: 15 });
  });

  it("borne la largeur visible entre 50 cm et 12 km", () => {
    assert.equal(zoomer(vue, ecran, 1000).largeur, LARGEUR_MIN);
    assert.equal(zoomer(vue, ecran, 0.0001).largeur, LARGEUR_MAX);
  });

  it("refuse un facteur nul ou négatif", () => {
    assert.throws(() => zoomer(vue, ecran, 0), RangeError);
    assert.throws(() => zoomer(vue, ecran, -2), RangeError);
  });
});

describe("déplacer et pincer", () => {
  const vue = { x: 0, y: 0, largeur: 40, hauteur: 30 };

  it("fait suivre le doigt au plan", () => {
    assert.deepEqual(deplacer(vue, ecran, 100, -50), { x: -5, y: 2.5, largeur: 40, hauteur: 30 });
  });

  it("écarter les doigts rapproche, et le point sous leur milieu suit le milieu", () => {
    const avant = [{ x: 300, y: 300 }, { x: 500, y: 300 }];
    const apres = [{ x: 250, y: 320 }, { x: 650, y: 320 }];
    const sous = versRepere(vue, ecran, { x: 400, y: 300 });
    const pincee = pincer(vue, ecran, avant, apres);
    proche(pincee.largeur, 20, "largeur");
    const milieu = versRepere(pincee, ecran, { x: 450, y: 320 });
    proche(milieu.x, sous.x, "x");
    proche(milieu.y, sous.y, "y");
  });

  it("doigts confondus : déplacement seul", () => {
    const pincee = pincer(vue, ecran, [{ x: 100, y: 100 }, { x: 100, y: 100 }], [{ x: 120, y: 100 }, { x: 120, y: 100 }]);
    assert.equal(pincee.largeur, 40);
    proche(pincee.x, -1);
  });
});

describe("redimensionner", () => {
  it("garde le centre et l'échelle quand la tablette tourne", () => {
    const vue = { x: 0, y: 0, largeur: 40, hauteur: 30 };
    const tournee = redimensionner(vue, ecran, { largeur: 600, hauteur: 800 });
    proche(tournee.largeur, 30);
    proche(tournee.hauteur, 40);
    proche(tournee.x + tournee.largeur / 2, 20);
    proche(tournee.y + tournee.hauteur / 2, 15);
  });
});

describe("pasGrille", () => {
  it("choisit 1, 2 ou 5 fois une puissance de dix d'au moins 40 px", () => {
    assert.equal(pasGrille({ x: 0, y: 0, largeur: 20, hauteur: 15 }, ecran), 1); // 1 m = 40 px
    assert.equal(pasGrille({ x: 0, y: 0, largeur: 30, hauteur: 15 }, ecran), 2);
    assert.equal(pasGrille({ x: 0, y: 0, largeur: 80, hauteur: 60 }, ecran), 5);
    assert.equal(pasGrille({ x: 0, y: 0, largeur: 140, hauteur: 105 }, ecran), 10);
  });
});

describe("bornes", () => {
  it("unit des bornes en ignorant les absentes", () => {
    assert.equal(unirBornes(null, undefined), null);
    assert.deepEqual(unirBornes({ minX: 0, minY: 1, maxX: 5, maxY: 6 }, null, { minX: -1, minY: 2, maxX: 3, maxY: 9 }), { minX: -1, minY: 1, maxX: 5, maxY: 9 });
  });

  it("borne une liste de points", () => {
    assert.equal(bornesPoints([]), null);
    assert.equal(bornesPoints(undefined), null);
    assert.deepEqual(bornesPoints([{ x: 1, y: 2 }, { x: -3, y: 4 }]), { minX: -3, minY: 2, maxX: 1, maxY: 4 });
  });
});

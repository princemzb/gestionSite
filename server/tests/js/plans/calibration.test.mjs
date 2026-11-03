// Tests de la calibration de l'éditeur de plans de bâtiment (S21-07, RP-21.4, T-11, ADR-11).
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  BORNE, COTE_PIXELS_MIN, SEUIL_AVERTISSEMENT, bornesFond, defautCote, defautFond, distanceProposee, ecartControle, formaterEcart,
  formaterMetres, lireDistance, longueurPixels, messageDefaut, mesurer, metresParPixel, metresParPixelDeclares, normaliserCote,
  pixelVersRepere, pointImage, repereVersPixel,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/calibration.mjs";

// Fond synthétique de 4 096 × 3 072 px dessiné à 5 mm par pixel (un bâtiment de 20 × 15 m).
const image = { largeur: 4096, hauteur: 3072 };
const vraiePixel = 0.005;

describe("T-11 : calibration avec une cote de 12,00 m, cote de contrôle à moins de 1 %", () => {
  it("retrouve l'échelle et mesure la cote de contrôle à moins de 1 %, malgré un pointage à 2 px près", () => {
    // 12,00 m = 2 400 px à 5 mm par pixel ; points touchés à 1 ou 2 px de la vraie extrémité.
    const calibration = { de: { x: 201, y: 299 }, a: { x: 2599, y: 301 }, distance: 12 };
    const controle = { de: { x: 199, y: 302 }, a: { x: 202, y: 2078 }, distance: 8.9 }; // 1 780 px = 8,90 m
    const metres = metresParPixel(calibration);
    assert.ok(Math.abs(metres - vraiePixel) / vraiePixel < 0.01);
    const resultat = ecartControle(controle, metres);
    assert.ok(resultat.ecart < 0.01, `écart ${resultat.ecart}`);
    assert.equal(resultat.avertissement, false);
    assert.equal(defautFond({ calibration, controle }, image), null);
  });

  it("recette du porteur : calibration 14,33 m, contrôle 8,90 m", () => {
    const calibration = { de: { x: 100, y: 100 }, a: { x: 2966, y: 100 }, distance: 14.33 }; // 2 866 px
    const controle = { de: { x: 100, y: 200 }, a: { x: 100, y: 1980 }, distance: 8.9 };
    const { mesuree, ecart } = ecartControle(controle, metresParPixel(calibration));
    assert.equal(mesuree, 8.9);
    assert.ok(ecart < 0.01);
  });

  it("avertit au-delà de 2 % d'écart, pas en deçà", () => {
    const calibration = { de: { x: 0, y: 0 }, a: { x: 1000, y: 0 }, distance: 10 }; // 1 cm par pixel
    const juste = ecartControle({ de: { x: 0, y: 0 }, a: { x: 0, y: 500 }, distance: 4.91 }, metresParPixel(calibration));
    const faux = ecartControle({ de: { x: 0, y: 0 }, a: { x: 0, y: 500 }, distance: 4.85 }, metresParPixel(calibration));
    assert.equal(juste.avertissement, false);
    assert.equal(faux.avertissement, true);
    assert.ok(faux.ecart > SEUIL_AVERTISSEMENT);
  });
});

describe("cotes", () => {
  it("mesure la longueur en pixels et en mètres", () => {
    const cote = { de: { x: 0, y: 0 }, a: { x: 300, y: 400 }, distance: 10 };
    assert.equal(longueurPixels(cote), 500);
    assert.equal(metresParPixel(cote), 0.02);
    assert.equal(mesurer(cote, 0.0123), 6.15);
  });

  it("arrondit un point au centième de pixel et refuse un point hors de l'image", () => {
    assert.deepEqual(pointImage({ x: 10.004, y: 20.006 }, image), { x: 10, y: 20.01 });
    assert.equal(pointImage({ x: -0.5, y: 3 }, image), null);
    assert.equal(pointImage({ x: 3, y: 3073 }, image), null);
    assert.equal(pointImage({ x: 4097, y: 3 }, image), null);
    assert.equal(pointImage({ x: 3, y: -1 }, image), null);
  });

  it("signale le premier défaut d'une cote", () => {
    const complete = { de: { x: 0, y: 0 }, a: { x: 100, y: 0 }, distance: 5 };
    assert.equal(defautCote(complete, image), null);
    assert.equal(defautCote(null, image), "de");
    assert.equal(defautCote({ ...complete, de: null }, image), "de");
    assert.equal(defautCote({ ...complete, de: { x: -1, y: 0 } }, image), "de");
    assert.equal(defautCote({ ...complete, a: null }, image), "a");
    assert.equal(defautCote({ ...complete, a: { x: 9000, y: 0 } }, image), "a");
    assert.equal(defautCote({ ...complete, a: { x: COTE_PIXELS_MIN - 1, y: 0 } }, image), "courte");
    // Contrôlée après arrondi au centième de pixel, comme le serveur : 10,0002 px avant arrondi, 9,998 px après.
    assert.equal(defautCote({ ...complete, a: { x: 7.0712, y: 7.0712 } }, image), "courte");
    for (const distance of [null, Number.NaN, "5", 0.1, 5001]) {
      assert.equal(defautCote({ ...complete, distance }, image), "distance");
    }
  });

  it("a un message pour chaque défaut", () => {
    for (const defaut of ["de", "a", "courte", "distance", "etendue"]) {
      assert.ok(messageDefaut(defaut).length > 10);
    }

    assert.equal(messageDefaut("inconnu"), "Cote invalide.");
  });

  it("normalise une cote à enregistrer", () => {
    assert.deepEqual(normaliserCote({ de: { x: 1.234, y: 2 }, a: { x: 500.004, y: 2 }, distance: 12.004 }, image),
      { de: { x: 1.23, y: 2 }, a: { x: 500, y: 2 }, distance: 12 });
  });
});

describe("fond calibré", () => {
  const calibration = { de: { x: 0, y: 0 }, a: { x: 1000, y: 0 }, distance: 10 };

  it("refuse une calibration ou un contrôle incomplets", () => {
    assert.deepEqual(defautFond({ calibration: { ...calibration, distance: null } }, image), { cote: "calibration", defaut: "distance" });
    assert.deepEqual(defautFond({ calibration, controle: { de: { x: 0, y: 0 }, a: null, distance: 3 } }, image), { cote: "controle", defaut: "a" });
    assert.equal(defautFond({ calibration, controle: null }, image), null);
  });

  it("refuse une échelle qui sortirait le fond du repère de 5 km", () => {
    assert.deepEqual(defautFond({ calibration: { ...calibration, distance: BORNE } }, image), { cote: "calibration", defaut: "etendue" });
  });

  it("place le fond coin haut gauche à l'origine du repère", () => {
    assert.deepEqual(bornesFond(image, 0.005), { minX: 0, minY: 0, maxX: 20.48, maxY: 15.36 });
    assert.deepEqual(pixelVersRepere({ x: 200, y: 400 }, 0.01), { x: 2, y: 4 });
    assert.deepEqual(repereVersPixel({ x: 2, y: 4 }, 0.01), { x: 200, y: 400 });
  });
});

describe("échelle déclarée d'un PDF", () => {
  // Page A3 paysage (420 mm) à 1:100, rendue en 4 096 px de large : 1 px ≈ 1,025 cm.
  const fond = { largeurAffichage: 4096, pdf: { largeurPageMm: 420, echelle: 100 } };

  it("propose les mètres par pixel et la distance d'une cote", () => {
    assert.ok(Math.abs(metresParPixelDeclares(fond) - 0.42 * 100 / 4096) < 1e-12);
    assert.equal(distanceProposee({ de: { x: 0, y: 0 }, a: { x: 1000, y: 0 } }, fond), 10.25);
  });

  it("ne propose rien sans échelle déclarée, pour une image ou une cote incomplète", () => {
    assert.equal(metresParPixelDeclares({ largeurAffichage: 4096, pdf: { largeurPageMm: 420, echelle: null } }), null);
    assert.equal(metresParPixelDeclares({ largeurAffichage: 4096, pdf: null }), null);
    assert.equal(metresParPixelDeclares(null), null);
    assert.equal(distanceProposee({ de: { x: 0, y: 0 }, a: null }, fond), null);
    assert.equal(distanceProposee(null, fond), null);
  });
});

describe("saisie et affichage", () => {
  it("lit une distance à la française", () => {
    assert.equal(lireDistance("14,33"), 14.33);
    assert.equal(lireDistance(" 8.90 m"), 8.9);
    assert.equal(lireDistance("1 234,5"), 1234.5);
    assert.equal(lireDistance("12"), 12);
  });

  it("refuse un texte qui n'est pas une distance", () => {
    for (const texte of ["", "abc", "-3", "1,2,3", "1e3", null, 12]) {
      assert.equal(lireDistance(texte), null);
    }
  });

  it("formate les mètres et l'écart", () => {
    assert.equal(formaterMetres(14.3), "14,30 m");
    assert.equal(formaterEcart(0.006), "0,6 %");
  });
});

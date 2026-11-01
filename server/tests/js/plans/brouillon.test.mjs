// Tests du brouillon local de l'éditeur de plans de bâtiment (S21-07, RP-21.8, ADR-11) : cloisonné par compte,
// organisation et plan, jamais bloquant.
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  PREFIXE, TAILLE_MAX, cleBrouillon, compteDeCle, ecrireBrouillon, effacerBrouillon, lireBrouillon, purgerAutresComptes, purgerTout,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/brouillon.mjs";

const compteA = "0190f5a2-1111-7000-8000-000000000001";
const compteB = "0190f5a2-2222-7000-8000-000000000002";
const organisation = "0190f5a2-3333-7000-8000-000000000003";
const plan = "0190f5a2-4444-7000-8000-000000000004";
const autrePlan = "0190f5a2-5555-7000-8000-000000000005";

// Stockage en mémoire, à l'image de localStorage (getItem, setItem, removeItem, key, length).
function memoire() {
  const valeurs = new Map();
  return {
    get length() { return valeurs.size; },
    key: i => [...valeurs.keys()][i] ?? null,
    getItem: cle => (valeurs.has(cle) ? valeurs.get(cle) : null),
    setItem: (cle, valeur) => { valeurs.set(cle, String(valeur)); },
    removeItem: cle => { valeurs.delete(cle); },
    valeurs,
  };
}

// Stockage indisponible (navigation privée stricte, stockage désactivé) : chaque accès lève une erreur.
const enPanne = {
  get length() { throw new Error("refusé"); },
  key() { throw new Error("refusé"); },
  getItem() { throw new Error("refusé"); },
  setItem() { throw new Error("refusé"); },
  removeItem() { throw new Error("refusé"); },
};

const brouillon = (compteId, planId = plan) => ({
  compteId, organisationId: organisation, planId, versionLue: 3, document: { contenu: { schema: 1 }, saisie: null }, modifieLe: "2026-11-01T10:00:00Z",
});

describe("clé du brouillon", () => {
  it("cloisonne par compte, organisation et plan", () => {
    const cle = cleBrouillon(compteA, organisation, plan);
    assert.ok(cle.startsWith(PREFIXE));
    assert.notEqual(cle, cleBrouillon(compteB, organisation, plan));
    assert.notEqual(cle, cleBrouillon(compteA, organisation, autrePlan));
    assert.equal(cle, cleBrouillon(compteA.toUpperCase(), organisation, plan));
  });

  it("refuse un identifiant qui n'est pas un GUID", () => {
    assert.throws(() => cleBrouillon("x:y", organisation, plan), RangeError);
    assert.throws(() => cleBrouillon(compteA, null, plan), RangeError);
  });

  it("retrouve le compte d'une clé, ou null pour une clé étrangère", () => {
    assert.equal(compteDeCle(cleBrouillon(compteA, organisation, plan)), compteA);
    assert.equal(compteDeCle("theme"), null);
    assert.equal(compteDeCle(`${PREFIXE}pas-un-guid:x:y`), null);
    assert.equal(compteDeCle(null), null);
  });
});

describe("lecture et écriture", () => {
  it("relit le brouillon écrit pour le même compte et le même plan", () => {
    const stockage = memoire();
    assert.equal(ecrireBrouillon(stockage, brouillon(compteA)), true);
    const lu = lireBrouillon(stockage, cleBrouillon(compteA, organisation, plan));
    assert.equal(lu.versionLue, 3);
    assert.deepEqual(lu.document, { contenu: { schema: 1 }, saisie: null });
  });

  it("ne rend jamais le brouillon d'un autre compte, même recopié sous sa clé", () => {
    const stockage = memoire();
    ecrireBrouillon(stockage, brouillon(compteA));
    const cleB = cleBrouillon(compteB, organisation, plan);
    assert.equal(lireBrouillon(stockage, cleB), null);
    stockage.setItem(cleB, stockage.getItem(cleBrouillon(compteA, organisation, plan)));
    assert.equal(lireBrouillon(stockage, cleB), null);
  });

  it("ignore un brouillon illisible ou incomplet", () => {
    const stockage = memoire();
    const cle = cleBrouillon(compteA, organisation, plan);
    for (const texte of ["{", "null", "42", JSON.stringify({ ...brouillon(compteA), versionLue: -1 }),
      JSON.stringify({ ...brouillon(compteA), versionLue: "3" }), JSON.stringify({ ...brouillon(compteA), document: null }),
      JSON.stringify({ ...brouillon(compteA), document: "x" }), JSON.stringify({ ...brouillon(compteA), modifieLe: 1 }),
      JSON.stringify({ ...brouillon(compteA), compteId: 1 }), JSON.stringify({ ...brouillon(compteA), planId: "pas-un-guid" })]) {
      stockage.setItem(cle, texte);
      assert.equal(lireBrouillon(stockage, cle), null, texte);
    }

    assert.equal(lireBrouillon(memoire(), cle), null);
  });

  it("refuse un brouillon de plus de 4 Mo", () => {
    const stockage = memoire();
    const enorme = { ...brouillon(compteA), document: { contenu: { texte: "x".repeat(TAILLE_MAX) } } };
    assert.equal(ecrireBrouillon(stockage, enorme), false);
    assert.equal(stockage.length, 0);
  });

  it("ne bloque jamais quand le stockage est indisponible", () => {
    const cle = cleBrouillon(compteA, organisation, plan);
    assert.equal(ecrireBrouillon(enPanne, brouillon(compteA)), false);
    assert.equal(lireBrouillon(enPanne, cle), null);
    assert.equal(effacerBrouillon(enPanne, cle), false);
    assert.equal(purgerAutresComptes(enPanne, compteA), 0);
    assert.equal(purgerTout(enPanne), 0);
  });

  it("efface un brouillon", () => {
    const stockage = memoire();
    ecrireBrouillon(stockage, brouillon(compteA));
    const cle = cleBrouillon(compteA, organisation, plan);
    assert.equal(effacerBrouillon(stockage, cle), true);
    assert.equal(lireBrouillon(stockage, cle), null);
  });
});

describe("purge", () => {
  it("efface les brouillons des autres comptes et garde les siens et les clés étrangères", () => {
    const stockage = memoire();
    ecrireBrouillon(stockage, brouillon(compteA));
    ecrireBrouillon(stockage, brouillon(compteA, autrePlan));
    ecrireBrouillon(stockage, brouillon(compteB));
    stockage.setItem(`${PREFIXE}illisible`, "x");
    stockage.setItem("preference", "nuit");
    assert.equal(purgerAutresComptes(stockage, compteA), 2);
    assert.deepEqual([...stockage.valeurs.keys()].sort(), [cleBrouillon(compteA, organisation, plan), cleBrouillon(compteA, organisation, autrePlan), "preference"].sort());
  });

  it("efface tous les brouillons de l'éditeur", () => {
    const stockage = memoire();
    ecrireBrouillon(stockage, brouillon(compteA));
    ecrireBrouillon(stockage, brouillon(compteB));
    stockage.setItem("preference", "nuit");
    assert.equal(purgerTout(stockage), 2);
    assert.deepEqual([...stockage.valeurs.keys()], ["preference"]);
  });

  it("exige un compte valable pour purger", () => {
    assert.throws(() => purgerAutresComptes(memoire(), "x"), RangeError);
  });
});

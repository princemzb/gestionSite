// Tests de l'historique des versions et de la restauration (S21-09, RP-21.8, ADR-11).
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  VERSIONS_PAR_PAGE, ajouterPage, avantSuivant, bilanRestauration, corpsRestauration, libelleVersion, messageRefusRestauration, precisionsVersion,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/versions.mjs";

describe("historique des versions", () => {
  it("présente une version : numéro, date à Paris, auteur, motif et restauration", () => {
    const v = { version: 3, le: "2026-10-28T13:05:00Z", auteur: "Rose R.", motif: "Erreur de relevé", restaureDe: 1 };
    assert.equal(libelleVersion(v), "Version 3 · 28/10/2026 14:05 · Rose R.");
    assert.equal(precisionsVersion(v), "restauration de la version 1 · « Erreur de relevé »");
    assert.equal(libelleVersion({ version: 2, le: null, auteur: null }), "Version 2");
    assert.equal(precisionsVersion({ version: 2, motif: null, restaureDe: null }), "");
  });

  it("pagine : une page pleine annonce la suivante, une page courte est la dernière", () => {
    const pleine = Array.from({ length: VERSIONS_PAR_PAGE }, (_, i) => ({ version: 40 - i }));
    assert.equal(avantSuivant(pleine), 21);
    assert.equal(avantSuivant([{ version: 3 }, { version: 2 }], 2), 2);
    assert.equal(avantSuivant([{ version: 1 }]), null);
    assert.equal(avantSuivant([], 0), null);
    assert.equal(avantSuivant(null), null);
  });

  it("ajoute une page sans répéter une version déjà affichée", () => {
    assert.deepEqual(ajouterPage([{ version: 3 }, { version: 2 }], [{ version: 2 }, { version: 1 }]).map(v => v.version), [3, 2, 1]);
    assert.deepEqual(ajouterPage([{ version: 3 }], null).map(v => v.version), [3]);
  });
});

describe("restauration", () => {
  it("exige un motif et part de la version lue", () => {
    assert.equal(corpsRestauration(4, "envoi", "   "), null);
    assert.equal(corpsRestauration(4, "envoi", undefined), null);
    assert.deepEqual(corpsRestauration(4, "envoi", "  Erreur de relevé "), { commandeId: "envoi", version: 4, motif: "Erreur de relevé" });
  });

  it("dresse le bilan : nouvelle version et marqueurs retirés de la copie", () => {
    assert.deepEqual(bilanRestauration(1, { version: 5, avertissements: [] }), { texte: "Version 1 restaurée : elle devient la version 5.", genre: "succes" });
    const un = bilanRestauration(1, { version: 5, avertissements: [{ code: "marqueur_retire" }, { code: "marqueur_hors_zone" }] });
    assert.equal(un.genre, "avertissement");
    assert.match(un.texte, /1 marqueur de composant a été retiré de la copie/);
    assert.match(bilanRestauration(2, { version: 6, avertissements: [{ code: "marqueur_retire" }, { code: "marqueur_retire" }] }).texte, /2 marqueurs de composant ont été retirés/);
    assert.equal(bilanRestauration(2, null).genre, "succes");
  });

  it("explique un refus : conflit, motif, session, sinon comme un enregistrement", () => {
    const conflit = messageRefusRestauration(409, { code: "version_depassee", versionCourante: 7, auteur: "Marc D.", modifieLe: "2026-11-03T09:00:00Z" });
    assert.equal(conflit, "Ce plan a été modifié entre-temps par Marc D. le 03/11/2026 10:00 (version 7) : rien n'a été restauré. Rechargez la page, puis recommencez.");
    assert.equal(messageRefusRestauration(409, { code: "version_depassee", versionCourante: 7 }),
      "Ce plan a été modifié entre-temps (version 7) : rien n'a été restauré. Rechargez la page, puis recommencez.");
    assert.match(messageRefusRestauration(422, { code: "donnee_invalide", champ: "motif" }), /^Motif obligatoire/);
    assert.equal(messageRefusRestauration(401, null), "Votre session a expiré : reconnectez-vous.");
    assert.equal(messageRefusRestauration(403, null), "Vous ne modifiez pas les plans de ce site.");
    assert.equal(messageRefusRestauration(404, null), "Ce plan n'existe pas ou n'est plus accessible.");
  });
});

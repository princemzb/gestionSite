// Tests des marqueurs de composants sur le plan (S21-09, RP-21.7, ADR-11).
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  MARQUEURS_MAX, avancementComposants, deplacerMarqueur, etatsComposants, horsZone, marqueursHorsZone, marqueurSous, messageMarqueur,
  messageObsoletes, nomMarqueur, nomsComposants, placerMarqueur, positionnerMarqueur, rangMarqueur, retirerMarqueur,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/composants.mjs";

const rectangle = (x, y, l, h) => [{ x, y }, { x: x + l, y }, { x: x + l, y: y + h }, { x, y: y + h }];
const contenu = (composants = []) => ({
  schema: 1, pieces: [{ zoneId: "nef", contour: rectangle(0, 0, 10, 8) }], ouvertures: [], escaliers: [], composants, fond: null,
});
const document = (composants = []) => ({ contenu: contenu(composants), saisie: null, trace: null });
const extincteur = { id: "extincteur", nom: "Extincteur CO2 2 kg", zoneId: "nef" };
const baes = { id: "baes", nom: "Bloc autonome BAES", zoneId: "nef" };
const tableau = { id: "tableau", nom: "Tableau", zoneId: "sacristie" };

describe("placement des marqueurs", () => {
  it("place un composant au centimètre, sous la forme du serveur", () => {
    const { document: d, index } = placerMarqueur(document(), extincteur, { x: 2.004, y: 3.996 });
    assert.equal(index, 0);
    assert.deepEqual(d.contenu.composants, [{ composantId: "extincteur", zoneId: "nef", position: { x: 2, y: 4 } }]);
    assert.deepEqual(Object.keys(d.contenu.composants[0]), ["composantId", "zoneId", "position"]);
    assert.equal(rangMarqueur(d.contenu, "extincteur"), 0);
    assert.equal(rangMarqueur(d.contenu, "baes"), -1);
  });

  it("déplace un composant déjà placé au lieu de le placer deux fois", () => {
    const premier = placerMarqueur(document(), extincteur, { x: 2, y: 2 }).document;
    const second = placerMarqueur(premier, baes, { x: 5, y: 5 }).document;
    const { document: d, index } = placerMarqueur(second, extincteur, { x: 3, y: 3 });
    assert.equal(index, 0);
    assert.equal(d.contenu.composants.length, 2);
    assert.deepEqual(d.contenu.composants[0].position, { x: 3, y: 3 });
  });

  it("refuse un point hors du repère et un marqueur au-delà de 2 000", () => {
    assert.deepEqual(placerMarqueur(document(), extincteur, { x: 6000, y: 0 }), { defaut: "bornes" });
    assert.deepEqual(placerMarqueur(document(), extincteur, { x: Number.NaN, y: 0 }), { defaut: "bornes" });
    const plein = document(Array.from({ length: MARQUEURS_MAX }, (_, i) => ({ composantId: `c${i}`, zoneId: "nef", position: { x: 1, y: 1 } })));
    assert.deepEqual(placerMarqueur(plein, extincteur, { x: 1, y: 1 }), { defaut: "nombre" });
    assert.equal(placerMarqueur(plein, { id: "c3", zoneId: "nef" }, { x: 2, y: 2 }).index, 3); // déjà placé : déplacé
    assert.match(messageMarqueur("bornes"), /repère/);
    assert.match(messageMarqueur("nombre"), /2 000/);
    assert.equal(messageMarqueur("autre"), "Placement impossible.");
  });

  it("positionne, déplace et retire un marqueur sans toucher aux autres", () => {
    const d = placerMarqueur(placerMarqueur(document(), extincteur, { x: 2, y: 2 }).document, baes, { x: 4, y: 4 }).document;
    const pose = positionnerMarqueur(d, 1, { x: 4.256, y: 1 });
    assert.deepEqual(pose.contenu.composants[1].position, { x: 4.26, y: 1 });
    assert.equal(pose.contenu.composants[0], d.contenu.composants[0]);
    assert.equal(positionnerMarqueur(d, 1, { x: 4, y: 4 }), d); // même point : rien ne change (aucune étape d'annulation)
    assert.equal(positionnerMarqueur(d, 1, { x: 9000, y: 4 }), d);
    assert.equal(positionnerMarqueur(d, 5, { x: 1, y: 1 }), d);
    assert.deepEqual(deplacerMarqueur(d, 0, 0.1, -0.1).contenu.composants[0].position, { x: 2.1, y: 1.9 });
    assert.equal(deplacerMarqueur(d, 7, 1, 1), d);
    assert.deepEqual(retirerMarqueur(d, 0).contenu.composants.map(m => m.composantId), ["baes"]);
  });
});

describe("état des marqueurs", () => {
  it("signale un marqueur hors du contour de sa zone ou d'une zone non dessinée, pas sur le bord", () => {
    const c = contenu();
    assert.equal(horsZone(c, { zoneId: "nef", position: { x: 5, y: 4 } }), false);
    assert.equal(horsZone(c, { zoneId: "nef", position: { x: 10, y: 4 } }), false); // bord couvert, comme le serveur
    assert.equal(horsZone(c, { zoneId: "nef", position: { x: 12, y: 4 } }), true);
    assert.equal(horsZone(c, { zoneId: "sacristie", position: { x: 5, y: 4 } }), true);
    const avecMarqueurs = contenu([
      { composantId: "a", zoneId: "nef", position: { x: 5, y: 4 } },
      { composantId: "b", zoneId: "nef", position: { x: 12, y: 4 } },
      { composantId: "c", zoneId: "sacristie", position: { x: 1, y: 1 } },
    ]);
    assert.deepEqual([...marqueursHorsZone(avecMarqueurs)], [1, 2]);
  });

  it("désigne le marqueur le plus proche à la tolérance près", () => {
    const c = contenu([
      { composantId: "a", zoneId: "nef", position: { x: 2, y: 2 } },
      { composantId: "b", zoneId: "nef", position: { x: 2.3, y: 2 } },
    ]);
    assert.equal(marqueurSous(c, { x: 2.2, y: 2 }, 0.5), 1);
    assert.equal(marqueurSous(c, { x: 1.9, y: 2 }, 0.5), 0);
    assert.equal(marqueurSous(c, { x: 5, y: 5 }, 0.5), -1);
  });

  it("liste les composants placés et à placer, par zone, avec le hors zone", () => {
    const c = contenu([
      { composantId: "baes", zoneId: "nef", position: { x: 2, y: 2 } },
      { composantId: "tableau", zoneId: "sacristie", position: { x: 3, y: 3 } },
    ]);
    const tous = etatsComposants(c, [extincteur, baes, tableau]);
    assert.deepEqual(tous.map(e => [e.composant.id, e.index, e.horsZone]), [["extincteur", -1, false], ["baes", 0, false], ["tableau", 1, true]]);
    assert.deepEqual(etatsComposants(c, [extincteur, baes, tableau], "nef").map(e => e.composant.id), ["extincteur", "baes"]);
    assert.deepEqual(avancementComposants(c, [extincteur, baes, tableau]), { total: 3, places: 2 });
  });

  it("nomme un marqueur, ou dit que son composant n'est plus placable ici", () => {
    const noms = nomsComposants([extincteur, baes]);
    assert.equal(nomMarqueur(noms, "extincteur"), "Extincteur CO2 2 kg");
    assert.equal(nomMarqueur(noms, "inconnu"), "Composant retiré ou déplacé");
  });

  it("signale les marqueurs obsolètes de la version lue", () => {
    assert.equal(messageObsoletes(0), "");
    assert.equal(messageObsoletes(undefined), "");
    assert.match(messageObsoletes(1), /^1 marqueur de composant n'est plus affiché : .*Il sera retiré de la prochaine version enregistrée\.$/);
    assert.match(messageObsoletes(3), /^3 marqueurs de composant ne sont plus affichés : .*objet de valeur\. Ils seront retirés/);
  });
});

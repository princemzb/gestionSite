// Tests du dessin des pièces, ouvertures, escaliers et ascenseurs (S21-08, ADR-11).
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  abandonnerTrace, accrocher, ajouterSommet, autresNiveaux, avancement, changerZone, commencerTrace, contoursAccrochage, defautEscalier,
  defautOuverture, defautPiece, deplacerElement, formaterSurface, deplacerSommet, elementSous, fermeLeTrace, fermerTrace, glisserOuverture, insererSommet,
  largeurOuverture, libelleType, messageDessin, modifierEscalier, modifierOuverture, nomNiveau, nomZone, pieceEnFace, poserOuverture,
  premierDefaut, regler, supprimerElement, supprimerSommet, zonesALaMarge, zonesDessinables, zonesRestantes,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/dessin.mjs";

const rdc = "niveau-rdc";
const cadre = {
  niveauId: rdc,
  zones: [
    { id: "nef", nom: "Nef", niveauId: rdc, partiesCommunes: false },
    { id: "sacristie", nom: "Sacristie", niveauId: rdc, partiesCommunes: false },
    { id: "chapelle", nom: "Chapelle", niveauId: rdc, partiesCommunes: false },
    { id: "crypte", nom: "Crypte", niveauId: "niveau-ss", partiesCommunes: false },
    { id: "communes", nom: "Parties communes", niveauId: null, partiesCommunes: true },
    { id: "parvis", nom: "Parvis", niveauId: null, partiesCommunes: false },
  ],
  niveaux: [{ id: "niveau-tribune", nom: "Tribune", ordre: 1 }, { id: rdc, nom: "Rez-de-chaussée", ordre: 0 }, { id: "niveau-ss", nom: "Sous-sol", ordre: -1 }],
};
const rectangle = (x, y, l, h) => [{ x, y }, { x: x + l, y }, { x: x + l, y: y + h }, { x, y: y + h }];
const vide = { contenu: { schema: 1, pieces: [], ouvertures: [], escaliers: [], composants: [], fond: null }, saisie: null, trace: null };

function tracer(document, points, genre = "piece", options = { zoneId: "nef" }) {
  let d = commencerTrace(document, genre, options);
  for (const p of points) {
    d = ajouterSommet(d, p);
  }

  return d;
}

// Nef (0,0)-(10,8) et sacristie (10,0)-(14,4), mur commun ; porte entre les deux.
function deuxPieces() {
  let d = fermerTrace(tracer(vide, rectangle(0, 0, 10, 8)), cadre).document;
  d = fermerTrace(tracer(d, rectangle(10, 0, 4, 4), "piece", { zoneId: "sacristie" }), cadre).document;
  return d;
}

describe("zones et niveaux", () => {
  it("distingue zones dessinables, zones à la marge et zones restantes", () => {
    assert.deepEqual(zonesDessinables(cadre).map(z => z.id), ["nef", "sacristie", "chapelle"]);
    assert.deepEqual(zonesALaMarge(cadre).map(z => z.id), ["communes", "parvis"]);
    const masse = { ...cadre, niveauId: null };
    assert.deepEqual(zonesDessinables(masse).map(z => z.id), ["parvis"]);
    assert.deepEqual(zonesALaMarge(masse).map(z => z.id), ["communes"]);
    const d = deuxPieces();
    assert.deepEqual(zonesRestantes(d.contenu, cadre).map(z => z.id), ["chapelle"]);
    assert.deepEqual(avancement(d.contenu, cadre), { total: 3, faites: 2, suivante: cadre.zones[2] });
    assert.equal(avancement(vide.contenu, { ...cadre, zones: [] }).suivante, null);
  });

  it("propose les autres niveaux du bâtiment, aucun sur un plan masse", () => {
    assert.deepEqual(autresNiveaux(cadre).map(n => n.id), ["niveau-ss", "niveau-tribune"]);
    assert.deepEqual(autresNiveaux({ ...cadre, niveauId: null }), []);
    assert.equal(nomNiveau(cadre, "niveau-ss"), "Sous-sol");
    assert.equal(nomNiveau(cadre, "x"), "Niveau");
    assert.equal(nomZone(cadre, "nef"), "Nef");
    assert.equal(nomZone(cadre, "x"), "Zone");
    assert.equal(libelleType("Fenetre"), "Fenêtre");
    assert.equal(libelleType("Autre"), "Autre");
    assert.equal(formaterSurface(rectangle(0, 0, 4.6, 6.5)), "29,9 m²");
  });
});

describe("tracé", () => {
  it("ajoute des sommets au centimètre, sans répéter le dernier ni sortir du repère", () => {
    let d = commencerTrace({ ...vide, saisie: { fondId: "f" } }, "piece");
    assert.equal(d.saisie, null);
    d = ajouterSommet(d, { x: 1.004, y: 2 });
    assert.equal(ajouterSommet(d, { x: 1.001, y: 2 }), d);
    assert.equal(ajouterSommet(d, { x: 9000, y: 2 }), d);
    assert.equal(ajouterSommet(vide, { x: 1, y: 1 }), vide);
    assert.deepEqual(d.trace.points, [{ x: 1, y: 2 }]);
    assert.throws(() => commencerTrace(vide, "mur"), RangeError);
  });

  it("règle les options du tracé sans toucher à ses sommets", () => {
    const d = tracer(vide, [{ x: 0, y: 0 }], "escalier", {});
    assert.equal(d.trace.type, "Escalier");
    const r = regler(d, { type: "Ascenseur", versNiveauId: "niveau-ss", points: [] });
    assert.equal(r.trace.type, "Ascenseur");
    assert.equal(r.trace.points.length, 1);
    assert.equal(regler(vide, { type: "Ascenseur" }), vide);
  });

  it("ferme sur le premier sommet à partir de trois", () => {
    const d = tracer(vide, rectangle(0, 0, 4, 4).slice(0, 3));
    assert.ok(fermeLeTrace(d.trace, { x: 0.05, y: 0 }, 0.1));
    assert.ok(!fermeLeTrace(d.trace, { x: 1, y: 0 }, 0.1));
    assert.ok(!fermeLeTrace(tracer(vide, [{ x: 0, y: 0 }]).trace, { x: 0, y: 0 }, 0.1));
    assert.ok(!fermeLeTrace(null, { x: 0, y: 0 }, 0.1));
  });

  it("ajoute la pièce fermée à son rang, le tracé clos", () => {
    const resultat = fermerTrace(tracer(vide, rectangle(0, 0, 10, 8)), cadre);
    assert.equal(resultat.index, 0);
    assert.equal(resultat.document.trace, null);
    assert.deepEqual(resultat.document.contenu.pieces[0], { zoneId: "nef", contour: rectangle(0, 0, 10, 8) });
  });

  it("refuse une pièce sans zone, d'un autre niveau, déjà dessinée, invalide ou qui chevauche", () => {
    assert.deepEqual(fermerTrace(vide, cadre), { defaut: "trace" });
    assert.deepEqual(fermerTrace(tracer(vide, rectangle(0, 0, 4, 4), "piece", {}), cadre), { defaut: "zone" });
    assert.deepEqual(fermerTrace(tracer(vide, rectangle(0, 0, 4, 4), "piece", { zoneId: "crypte" }), cadre), { defaut: "zone-niveau" });
    assert.deepEqual(fermerTrace(tracer(vide, rectangle(0, 0, 4, 4), "piece", { zoneId: "communes" }), cadre), { defaut: "zone-niveau" });
    const d = deuxPieces();
    assert.deepEqual(fermerTrace(tracer(d, rectangle(20, 0, 4, 4)), cadre), { defaut: "zone-dessinee" });
    assert.deepEqual(fermerTrace(tracer(d, [{ x: 20, y: 0 }, { x: 21, y: 0 }]), cadre), { defaut: "sommets" });
    assert.deepEqual(fermerTrace(tracer(d, rectangle(13, 3, 4, 4), "piece", { zoneId: "chapelle" }), cadre), { defaut: { code: "chevauche", avec: "sacristie" } });
    assert.equal(fermerTrace(tracer(d, rectangle(14, 0, 4, 4), "piece", { zoneId: "chapelle" }), cadre).index, 2);
  });

  it("borne le nombre de pièces et de sommets", () => {
    const plein = { ...vide, contenu: { ...vide.contenu, pieces: Array.from({ length: 300 }, () => ({ zoneId: "x", contour: [] })) } };
    assert.deepEqual(fermerTrace(tracer(plein, rectangle(0, 0, 4, 4)), cadre), { defaut: "nombre" });
    const lourd = { ...vide, contenu: { ...vide.contenu, escaliers: [{ contour: Array.from({ length: 19_999 }, () => ({ x: 0, y: 0 })) }] } };
    assert.deepEqual(fermerTrace(tracer(lourd, rectangle(0, 0, 4, 4)), cadre), { defaut: "points" });
  });

  it("ferme un escalier vers un autre niveau, jamais sans niveau d'arrivée", () => {
    const options = { type: "Escalier", versNiveauId: "niveau-ss" };
    const resultat = fermerTrace(tracer(deuxPieces(), rectangle(1, 1, 2, 3), "escalier", options), cadre);
    assert.deepEqual(resultat.document.contenu.escaliers[0], { type: "Escalier", contour: rectangle(1, 1, 2, 3), versNiveauId: "niveau-ss" });
    assert.deepEqual(fermerTrace(tracer(vide, rectangle(1, 1, 2, 3), "escalier", { type: "Escalier" }), cadre), { defaut: "niveau" });
    assert.deepEqual(fermerTrace(tracer(vide, rectangle(1, 1, 2, 3), "escalier", { ...options, versNiveauId: rdc }), cadre), { defaut: "niveau" });
    assert.deepEqual(fermerTrace(tracer(vide, rectangle(1, 1, 2, 3), "escalier", { ...options, type: "Rampe" }), cadre), { defaut: "type" });
    const plein = { ...vide, contenu: { ...vide.contenu, escaliers: Array.from({ length: 100 }, () => ({ contour: [] })) } };
    assert.deepEqual(fermerTrace(tracer(plein, rectangle(1, 1, 2, 3), "escalier", options), cadre), { defaut: "nombre" });
  });

  it("abandonne le tracé", () => {
    assert.equal(abandonnerTrace(tracer(vide, [{ x: 0, y: 0 }])).trace, null);
    assert.equal(abandonnerTrace(vide), vide);
  });
});

describe("ouvertures", () => {
  it("pose une porte centrée sur l'arête touchée, reliée à la pièce d'en face", () => {
    const resultat = poserOuverture(deuxPieces(), "Porte", { x: 10.1, y: 2 }, 0.3);
    const porte = resultat.document.contenu.ouvertures[resultat.index];
    assert.equal(porte.type, "Porte");
    assert.equal(largeurOuverture(porte), 0.9);
    assert.equal(porte.de.x, 10);
    assert.deepEqual(porte.zones.toSorted(), ["nef", "sacristie"]);
    assert.equal(defautOuverture(resultat.document.contenu, 0), null);
  });

  it("garde l'ouverture sur l'arête près d'un angle, et la réduit à une arête courte", () => {
    const d = fermerTrace(tracer(vide, [{ x: 0, y: 0 }, { x: 0.6, y: 0 }, { x: 0.6, y: 5 }, { x: 0, y: 5 }]), cadre).document;
    const fenetre = poserOuverture(d, "Fenetre", { x: 0.55, y: 0 }, 0.2).document.contenu.ouvertures[0];
    assert.deepEqual([fenetre.de, fenetre.a], [{ x: 0, y: 0 }, { x: 0.6, y: 0 }]);
    assert.deepEqual(fenetre.zones, ["nef"]);
    const baie = poserOuverture(deuxPieces(), "Baie", { x: 0.1, y: 7.9 }, 0.3).document.contenu.ouvertures[0];
    assert.equal(largeurOuverture(baie), 1.4);
    assert.ok(Math.min(baie.de.x, baie.a.x) >= 0);
  });

  it("refuse hors d'une arête, un type inconnu, une arête de moins de 20 cm, au-delà de 1 000", () => {
    assert.deepEqual(poserOuverture(deuxPieces(), "Porte", { x: 5, y: 4 }, 0.3), { defaut: "hors-arete" });
    assert.deepEqual(poserOuverture(deuxPieces(), "Trappe", { x: 10, y: 2 }, 0.3), { defaut: "type" });
    const court = fermerTrace(tracer(vide, [{ x: 0, y: 0 }, { x: 0.1, y: 0 }, { x: 3, y: 3 }, { x: 0, y: 3 }]), cadre).document;
    assert.deepEqual(poserOuverture(court, "Porte", { x: 0.05, y: 0 }, 0.01), { defaut: "arete-courte" });
    const plein = { ...vide, contenu: { ...vide.contenu, ouvertures: Array.from({ length: 1000 }, () => ({})) } };
    assert.deepEqual(poserOuverture(plein, "Porte", { x: 0, y: 0 }, 1), { defaut: "nombre" });
  });

  it("modifie le type, la largeur et la pièce d'en face", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 10, y: 2 }, 0.3).document;
    d = modifierOuverture(d, 0, { type: "Baie", largeur: 1.6 });
    assert.equal(d.contenu.ouvertures[0].type, "Baie");
    assert.equal(largeurOuverture(d.contenu.ouvertures[0]), 1.6);
    const premiere = d.contenu.ouvertures[0].zones[0];
    assert.deepEqual(modifierOuverture(d, 0, { enFace: null }).contenu.ouvertures[0].zones, [premiere]);
    assert.deepEqual(modifierOuverture(d, 0, { enFace: "chapelle" }).contenu.ouvertures[0].zones, [premiere, "chapelle"]);
    assert.equal(pieceEnFace(d.contenu, "nef", { x: 30, y: 30 }), null);
  });

  it("fait glisser une ouverture le long de son arête, sans en sortir", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 5, y: 0 }, 0.3).document;
    const avant = d.contenu.ouvertures[0];
    d = glisserOuverture(d, 0, 1);
    assert.equal(Math.abs(d.contenu.ouvertures[0].de.x - avant.de.x), 1);
    d = glisserOuverture(d, 0, 50);
    assert.equal(Math.max(d.contenu.ouvertures[0].de.x, d.contenu.ouvertures[0].a.x), 10);
    // Ouverture hors de toute arête : simple translation.
    const libre = { ...vide, contenu: { ...vide.contenu, ouvertures: [{ type: "Porte", de: { x: 0, y: 0 }, a: { x: 1, y: 0 }, zones: [] }] } };
    assert.deepEqual(glisserOuverture(libre, 0, 0.5).contenu.ouvertures[0].de, { x: 0.5, y: 0 });
  });

  it("contrôle type, largeur, zones et arête comme le serveur", () => {
    const d = poserOuverture(deuxPieces(), "Porte", { x: 5, y: 0 }, 0.3).document;
    const avec = changements => ({ ...d.contenu, ouvertures: [{ ...d.contenu.ouvertures[0], ...changements }] });
    assert.equal(defautOuverture(avec({ type: "Trappe" }), 0), "type");
    assert.equal(defautOuverture(avec({ a: d.contenu.ouvertures[0].de }), 0), "largeur");
    assert.equal(defautOuverture(avec({ zones: [] }), 0), "zones");
    assert.equal(defautOuverture(avec({ zones: undefined }), 0), "zones");
    assert.equal(defautOuverture(avec({ zones: ["nef", "nef"] }), 0), "zones");
    assert.equal(defautOuverture(avec({ zones: ["chapelle"] }), 0), "zones");
    assert.equal(defautOuverture(avec({ de: { x: 4, y: 1 }, a: { x: 5, y: 1 } }), 0), "arete");
  });
});

describe("édition des éléments posés", () => {
  const nef = { genre: "piece", index: 0 };

  it("déplace un sommet ; les ouvertures de la pièce suivent leur mur", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 5, y: 0 }, 0.3).document;
    d = deplacerSommet(d, nef, 1, { x: 9.5, y: -1 });
    assert.deepEqual(d.contenu.pieces[0].contour[1], { x: 9.5, y: -1 });
    assert.equal(defautOuverture(d.contenu, 0), null);
    assert.equal(deplacerSommet(d, nef, 1, { x: 9.501, y: -1 }), d);
    assert.equal(deplacerSommet(d, nef, 1, { x: 9000, y: 0 }), d);
  });

  it("insère et supprime un sommet (trois au moins), ouvertures replacées sur l'arête la plus proche", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 5, y: 0 }, 0.3).document;
    d = insererSommet(d, nef, 0);
    assert.deepEqual(d.contenu.pieces[0].contour[1], { x: 5, y: 0 });
    assert.equal(defautOuverture(d.contenu, 0), null);
    d = supprimerSommet(d, nef, 1);
    assert.equal(d.contenu.pieces[0].contour.length, 4);
    assert.equal(defautOuverture(d.contenu, 0), null);
    const triangle = fermerTrace(tracer(vide, [{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 0, y: 2 }]), cadre).document;
    assert.equal(supprimerSommet(triangle, nef, 0), triangle);
    const plein = fermerTrace(tracer(vide, Array.from({ length: 500 }, (_, k) => ({ x: 10 * Math.cos(2 * Math.PI * k / 500), y: 10 * Math.sin(2 * Math.PI * k / 500) }))), cadre).document;
    assert.equal(insererSommet(plein, nef, 0), plein);
  });

  it("déplace une pièce avec ses ouvertures, une liaison, une ouverture", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 5, y: 0 }, 0.3).document;
    d = fermerTrace(tracer(d, rectangle(1, 1, 2, 3), "escalier", { type: "Ascenseur", versNiveauId: "niveau-tribune" }), cadre).document;
    const deplace = deplacerElement(d, nef, 0, -0.5);
    assert.deepEqual(deplace.contenu.pieces[0].contour[0], { x: 0, y: -0.5 });
    assert.equal(deplace.contenu.ouvertures[0].de.y, -0.5);
    assert.equal(deplacerElement(d, { genre: "escalier", index: 0 }, 1, 0).contenu.escaliers[0].contour[0].x, 2);
    assert.equal(deplacerElement(d, { genre: "ouverture", index: 0 }, 0, 1).contenu.ouvertures[0].de.y, 1);
    assert.equal(deplacerElement(d, nef, 4999, 0), d);
    assert.throws(() => deplacerElement(d, { genre: "mur", index: 0 }, 1, 0), RangeError);
    assert.equal(deplacerSommet(d, { genre: "ouverture", index: 0 }, 0, { x: 1, y: 1 }), d);
  });

  it("supprime une pièce et les ouvertures posées sur elle seule", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 10, y: 2 }, 0.3).document;
    d = poserOuverture(d, "Fenetre", { x: 5, y: 0 }, 0.3).document;
    const sans = supprimerElement(d, nef);
    assert.deepEqual(sans.contenu.pieces.map(p => p.zoneId), ["sacristie"]);
    assert.deepEqual(sans.contenu.ouvertures.map(o => o.zones), [["sacristie"]]);
    assert.equal(supprimerElement(d, { genre: "ouverture", index: 1 }).contenu.ouvertures.length, 1);
  });

  it("change la zone d'une pièce, reprise par ses ouvertures, et règle un escalier", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 10, y: 2 }, 0.3).document;
    assert.equal(changerZone(d, 1, "sacristie"), d);
    d = changerZone(d, 1, "chapelle");
    assert.equal(d.contenu.pieces[1].zoneId, "chapelle");
    assert.ok(d.contenu.ouvertures[0].zones.includes("chapelle"));
    d = fermerTrace(tracer(d, rectangle(1, 1, 2, 3), "escalier", { type: "Escalier", versNiveauId: "niveau-ss" }), cadre).document;
    d = modifierEscalier(d, 0, { type: "Ascenseur", versNiveauId: "niveau-tribune", contour: [] });
    assert.deepEqual([d.contenu.escaliers[0].type, d.contenu.escaliers[0].versNiveauId, d.contenu.escaliers[0].contour.length], ["Ascenseur", "niveau-tribune", 4]);
  });
});

describe("contrôle d'ensemble et messages", () => {
  it("rend le premier défaut, dans l'ordre du serveur", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 10, y: 2 }, 0.3).document;
    assert.equal(premierDefaut(d.contenu, cadre), null);
    assert.deepEqual(premierDefaut(deplacerElement(d, { genre: "piece", index: 1 }, -1, 0).contenu, cadre), {
      genre: "piece", index: 0, defaut: { code: "chevauche", avec: "sacristie" },
    });
    d = fermerTrace(tracer(d, rectangle(1, 1, 2, 3), "escalier", { type: "Escalier", versNiveauId: "niveau-ss" }), cadre).document;
    assert.deepEqual(premierDefaut(modifierEscalier(d, 0, { versNiveauId: null }).contenu, cadre), { genre: "escalier", index: 0, defaut: "niveau" });
    assert.equal(defautEscalier(d.contenu, cadre, 0), null);
    assert.equal(defautPiece(d.contenu, cadre, 0), null);
    assert.deepEqual(premierDefaut(deplacerElement(d, { genre: "ouverture", index: 0 }, -3, 0).contenu, cadre), { genre: "ouverture", index: 0, defaut: "arete" });
    const trop = { ...d.contenu, ouvertures: Array.from({ length: 1001 }, () => ({})) };
    assert.deepEqual(premierDefaut(trop, cadre), { genre: "plan", index: 0, defaut: "nombre" });
    const lourd = { ...d.contenu, escaliers: [{ contour: Array.from({ length: 20_001 }, () => ({ x: 0, y: 0 })) }] };
    assert.deepEqual(premierDefaut(lourd, cadre), { genre: "plan", index: 0, defaut: "points" });
  });

  it("explique chaque défaut", () => {
    assert.match(messageDessin({ code: "chevauche", avec: "nef" }, cadre), /chevauche « Nef » de plus de 5 cm/);
    assert.match(messageDessin("croise", cadre), /se croise/);
    assert.match(messageDessin("niveau", cadre), /niveau d'arrivée/);
    assert.equal(messageDessin("inconnu", cadre), "Élément invalide.");
  });
});

describe("désignation et accrochage", () => {
  it("désigne un sommet de la sélection, une ouverture, un escalier, puis la plus petite pièce", () => {
    let d = poserOuverture(deuxPieces(), "Porte", { x: 10, y: 2 }, 0.3).document;
    d = fermerTrace(tracer(d, rectangle(1, 1, 2, 3), "escalier", { type: "Escalier", versNiveauId: "niveau-ss" }), cadre).document;
    const c = d.contenu;
    assert.deepEqual(elementSous(c, { x: 10.02, y: 0 }, 0.1, { genre: "piece", index: 0 }), { genre: "piece", index: 0, sommet: 1 });
    assert.deepEqual(elementSous(c, { x: 10.02, y: 2 }, 0.1, { genre: "ouverture", index: 0 }), { genre: "ouverture", index: 0 });
    assert.deepEqual(elementSous(c, { x: 2, y: 2 }, 0.1), { genre: "escalier", index: 0 });
    assert.deepEqual(elementSous(c, { x: 12, y: 1 }, 0.1), { genre: "piece", index: 1 });
    assert.deepEqual(elementSous(c, { x: 5, y: 5 }, 0.1, { genre: "piece", index: 7 }), { genre: "piece", index: 0 });
    assert.equal(elementSous(c, { x: 50, y: 50 }, 0.1), null);
  });

  it("accroche au sommet, sinon à l'arête, sinon au point touché", () => {
    const contours = contoursAccrochage(deuxPieces().contenu);
    assert.equal(contours.length, 2);
    assert.equal(contoursAccrochage(deuxPieces().contenu, { genre: "piece", index: 0 }).length, 1);
    assert.deepEqual(accrocher({ x: 10.05, y: 0.04 }, contours, 0.1), { point: { x: 10, y: 0 }, accroche: "sommet" });
    assert.deepEqual(accrocher({ x: 5, y: 0.06 }, contours, 0.1), { point: { x: 5, y: 0 }, accroche: "arete" });
    assert.deepEqual(accrocher({ x: 5.004, y: 4 }, contours, 0.1), { point: { x: 5, y: 4 }, accroche: null });
    assert.deepEqual(accrocher({ x: 5, y: 0.06 }, [[{ x: 0, y: 0 }]], 0.1), { point: { x: 5, y: 0.06 }, accroche: null });
  });
});

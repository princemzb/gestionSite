// Tests du document de l'éditeur de plans de bâtiment (S21-07, ADR-11) : calibration en cours, enregistrement, refus.
import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  abandonnerSaisie, appliquerDecalage, decalageFond, aire, bornesAffichees, bornesContenu, centre, commencerCalibration, conflit, controleFond, corpsEnregistrement,
  descriptionFond, documentDepuisVersion, echelleFond, effacerCote, estModifie, fixerDistance, formaterDate, messageRefus, niveauInferieur,
  normaliserContenu, pointsSvg, poserPoint, resumeVersion, retirerFond, validerCalibration,
} from "../../../src/GestionSite.Web/wwwroot/plans/noyau/plan.mjs";

const image = { largeur: 4096, hauteur: 3072 };
const fondId = "0190f5a2-6666-7000-8000-000000000006";
const calibre = {
  fondId,
  calibration: { de: { x: 0, y: 0 }, a: { x: 1000, y: 0 }, distance: 10 },
  controle: { de: { x: 0, y: 0 }, a: { x: 0, y: 500 }, distance: 5.03 },
};
const carre = [{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 4 }, { x: 0, y: 4 }];

function calibrer(document) {
  let d = commencerCalibration(document, fondId);
  d = poserPoint(d, "calibration", { x: 0, y: 0 });
  d = poserPoint(d, "calibration", { x: 1000, y: 0 });
  return fixerDistance(d, "calibration", 10);
}

describe("document", () => {
  it("complète un contenu de schéma 1 et part d'une version vide", () => {
    assert.deepEqual(normaliserContenu(undefined), {
      schema: 1, nord: null, altitude: null, hauteurSousPlafond: null, pieces: [], ouvertures: [], escaliers: [], composants: [], fond: null,
    });
    const document = documentDepuisVersion({ version: 2, contenu: { schema: 1, nord: 12, pieces: [{ zoneId: "z", contour: carre }], fond: calibre } });
    assert.equal(document.contenu.nord, 12);
    assert.equal(document.contenu.fond, calibre);
    assert.equal(document.saisie, null);
    assert.equal(documentDepuisVersion(null).contenu.pieces.length, 0);
  });

  it("retire les marqueurs obsolètes, que le serveur refuserait dans la version suivante", () => {
    const composants = [{ composantId: "retire", position: { x: 1, y: 1 } }, { composantId: "actif", position: { x: 2, y: 2 } }];
    const document = documentDepuisVersion({ contenu: { composants }, marqueursObsoletes: ["retire"] });
    assert.deepEqual(document.contenu.composants.map(m => m.composantId), ["actif"]);
    assert.equal(documentDepuisVersion({ contenu: { composants }, marqueursObsoletes: [] }).contenu.composants.length, 2);
  });

  it("dit si le contenu a changé depuis la version lue", () => {
    const lu = documentDepuisVersion({ contenu: {} });
    assert.equal(estModifie(lu, lu), false);
    assert.equal(estModifie(retirerFond(documentDepuisVersion({ contenu: { fond: calibre } })), lu), false);
    assert.equal(estModifie(documentDepuisVersion({ contenu: { fond: calibre } }), lu), true);
  });
});

describe("calibration en cours", () => {
  const vide = documentDepuisVersion({ contenu: {} });

  it("pose le premier puis le second point, puis recommence la cote", () => {
    let d = commencerCalibration(vide, fondId);
    assert.deepEqual(d.saisie.calibration, { de: null, a: null, distance: null });
    d = fixerDistance(d, "calibration", 12);
    d = poserPoint(d, "calibration", { x: 1, y: 1 });
    d = poserPoint(d, "calibration", { x: 500, y: 1 });
    assert.deepEqual(d.saisie.calibration, { de: { x: 1, y: 1 }, a: { x: 500, y: 1 }, distance: 12 });
    d = poserPoint(d, "calibration", { x: 9, y: 9 });
    assert.deepEqual(d.saisie.calibration, { de: { x: 9, y: 9 }, a: null, distance: 12 });
    assert.deepEqual(effacerCote(d, "calibration").saisie.calibration, { de: null, a: null, distance: null });
  });

  it("ne change rien sans calibration commencée", () => {
    assert.equal(poserPoint(vide, "calibration", { x: 1, y: 1 }), vide);
    assert.equal(fixerDistance(vide, "controle", 3), vide);
    assert.equal(effacerCote(vide, "controle"), vide);
    assert.equal(abandonnerSaisie(vide), vide);
  });

  it("refuse une cote inconnue", () => {
    assert.throws(() => poserPoint(commencerCalibration(vide, fondId), "nord", { x: 0, y: 0 }), RangeError);
  });

  it("reprend les cotes enregistrées du même fond, pas celles d'un autre fond", () => {
    const enregistre = documentDepuisVersion({ contenu: { fond: calibre } });
    assert.deepEqual(commencerCalibration(enregistre, fondId).saisie.controle, calibre.controle);
    assert.deepEqual(commencerCalibration(enregistre, "autre").saisie.calibration, { de: null, a: null, distance: null });
    const sansControle = documentDepuisVersion({ contenu: { fond: { ...calibre, controle: null } } });
    assert.deepEqual(commencerCalibration(sansControle, fondId).saisie.controle, { de: null, a: null, distance: null });
  });

  it("valide la calibration dans le contenu, sans contrôle ou avec un contrôle complet", () => {
    const sansControle = validerCalibration(calibrer(vide), image);
    assert.deepEqual(sansControle.document.contenu.fond, { fondId, calibration: calibre.calibration, controle: null, decalage: null });
    assert.equal(sansControle.document.saisie, null);
    let avecControle = poserPoint(poserPoint(calibrer(vide), "controle", { x: 0, y: 0 }), "controle", { x: 0, y: 500 });
    avecControle = fixerDistance(avecControle, "controle", 5.03);
    assert.deepEqual(validerCalibration(avecControle, image).document.contenu.fond, { ...calibre, decalage: null });
  });

  it("garde le recalage d'un fond recalibré, pas celui d'un autre fond", () => {
    const recale = { contenu: { ...vide.contenu, fond: { ...calibre, decalage: { x: 1.5, y: -2 } } }, saisie: null, trace: null };
    assert.deepEqual(validerCalibration(calibrer(recale), image).document.contenu.fond.decalage, { x: 1.5, y: -2 });
    let autre = commencerCalibration(recale, "0190f5a2-7777-7000-8000-000000000007");
    autre = fixerDistance(poserPoint(poserPoint(autre, "calibration", { x: 0, y: 0 }), "calibration", { x: 1000, y: 0 }), "calibration", 10);
    assert.equal(validerCalibration(autre, image).document.contenu.fond.decalage, null);
  });

  it("refuse une calibration incomplète, un contrôle commencé, ou rien à valider", () => {
    assert.deepEqual(validerCalibration(commencerCalibration(vide, fondId), image).defaut, { cote: "calibration", defaut: "de" });
    const controleCommence = poserPoint(calibrer(vide), "controle", { x: 0, y: 0 });
    assert.deepEqual(validerCalibration(controleCommence, image).defaut, { cote: "controle", defaut: "a" });
    assert.deepEqual(validerCalibration(fixerDistance(calibrer(vide), "controle", 3), image).defaut, { cote: "controle", defaut: "de" });
    assert.deepEqual(validerCalibration(vide, image).defaut, { cote: "calibration", defaut: "de" });
  });

  it("abandonne la saisie sans toucher au contenu", () => {
    const d = abandonnerSaisie(calibrer(vide));
    assert.equal(d.saisie, null);
    assert.equal(d.contenu, vide.contenu);
  });
});

describe("enregistrement", () => {
  it("envoie la version lue, l'identifiant d'envoi, le motif et le contenu", () => {
    const document = documentDepuisVersion({ contenu: { fond: calibre } });
    assert.deepEqual(corpsEnregistrement(document, 4, "c", "  Relevé  "), { commandeId: "c", version: 4, motif: "Relevé", contenu: document.contenu });
    assert.equal(corpsEnregistrement(document, 0, "c", "   ").motif, null);
    assert.equal(corpsEnregistrement(document, 0, "c").motif, null);
  });
});

describe("échelle et contrôle du fond", () => {
  it("donne les mètres par pixel et l'écart du contrôle enregistré", () => {
    const contenu = normaliserContenu({ fond: calibre });
    assert.equal(echelleFond(contenu), 0.01);
    assert.ok(Math.abs(controleFond(contenu).ecart - 0.03 / 5.03) < 1e-9); // 5,00 m mesurés pour 5,03 m
    assert.equal(echelleFond(normaliserContenu({})), null);
    assert.equal(controleFond(normaliserContenu({ fond: { ...calibre, controle: null } })), null);
  });
});

describe("géométrie affichée", () => {
  const contenu = normaliserContenu({
    pieces: [{ zoneId: "a", contour: carre }],
    escaliers: [{ type: "Escalier", contour: [{ x: 10, y: 10 }, { x: 12, y: 10 }, { x: 12, y: 13 }], versNiveauId: "n" }],
    ouvertures: [{ type: "Porte", de: { x: -1, y: 0 }, a: { x: 0, y: 0 }, zones: ["a"] }],
    composants: [{ composantId: "c", position: { x: 2, y: 20 } }],
  });

  it("borne toute la géométrie, et le fond avec elle", () => {
    assert.deepEqual(bornesContenu(contenu), { minX: -1, minY: 0, maxX: 12, maxY: 20 });
    assert.equal(bornesContenu(normaliserContenu({})), null);
    assert.deepEqual(bornesAffichees(contenu, { minX: 0, minY: 0, maxX: 40, maxY: 30 }), { minX: -1, minY: 0, maxX: 40, maxY: 30 });
  });

  it("écrit les points SVG, l'aire et le centre d'un contour", () => {
    assert.equal(pointsSvg(carre), "0,0 4,0 4,4 0,4");
    assert.equal(aire(carre), 16);
    assert.deepEqual(centre(carre), { x: 2, y: 2 });
    assert.deepEqual(centre([{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 4, y: 0 }]), { x: 2, y: 0 }); // contour plat
  });
});

describe("niveau inférieur", () => {
  const niveaux = [{ id: "rdc", ordre: 0 }, { id: "sous-sol", ordre: -1 }, { id: "tribune", ordre: 1 }, { id: "cave", ordre: -2 }];

  it("prend le niveau d'ordre le plus proche en dessous", () => {
    assert.equal(niveauInferieur(niveaux, "tribune").id, "rdc");
    assert.equal(niveauInferieur(niveaux, "rdc").id, "sous-sol");
  });

  it("rien sous le niveau le plus bas ni pour un plan masse", () => {
    assert.equal(niveauInferieur(niveaux, "cave"), null);
    assert.equal(niveauInferieur(niveaux, null), null);
  });
});

describe("textes", () => {
  it("formate une date à l'heure de Paris", () => {
    assert.equal(formaterDate("2026-10-28T13:05:00Z"), "28/10/2026 14:05");
    assert.equal(formaterDate("pas une date"), "");
  });

  it("résume la version comme la maquette A", () => {
    const contenu = normaliserContenu({ fond: calibre });
    assert.equal(resumeVersion({ version: 4, le: "2026-10-28T13:05:00Z", auteur: "Rose R." }, contenu),
      "Version 4 · enregistrée le 28/10/2026 14:05 par Rose R. · échelle calibrée (cote de contrôle 0,6 %)");
    assert.equal(resumeVersion({ version: 1 }, normaliserContenu({})), "Version 1 · enregistrée · plan sur grille métrique");
    assert.equal(resumeVersion({ version: 2 }, normaliserContenu({ fond: { ...calibre, controle: null } })),
      "Version 2 · enregistrée · échelle calibrée, sans cote de contrôle");
    assert.equal(resumeVersion({ version: 0 }, normaliserContenu({})), "Aucune version enregistrée");
    assert.equal(resumeVersion(null, normaliserContenu({})), "Aucune version enregistrée");
  });

  it("décrit un fond importé", () => {
    assert.equal(descriptionFond({ largeurAffichage: 4096, hauteurAffichage: 2896, pdf: { page: 2, pages: 3, echelle: 100 }, le: "2026-10-28T13:05:00Z" }),
      "PDF, page 2 sur 3, 1:100 · importé le 28/10/2026 14:05");
    assert.equal(descriptionFond({ largeurAffichage: 1600, hauteurAffichage: 900, pdf: null }), "Image 1600 × 900 px");
    assert.equal(descriptionFond({ largeurAffichage: 1600, hauteurAffichage: 900, pdf: { page: 1, pages: 1, echelle: null } }), "PDF, page 1 sur 1");
  });
});

describe("refus du serveur", () => {
  it("reconnaît le conflit de version et nomme la version gagnante", () => {
    const probleme = { code: "version_depassee", versionCourante: 5, auteur: "Rose R.", modifieLe: "2026-10-28T13:05:00Z" };
    assert.deepEqual(conflit(409, probleme), { versionCourante: 5, auteur: "Rose R.", modifieLe: "2026-10-28T13:05:00Z" });
    assert.equal(conflit(409, { code: "commande_reutilisee" }), null);
    assert.equal(conflit(422, probleme), null);
    const message = messageRefus(409, probleme);
    assert.ok(message.includes("par Rose R.") && message.includes("le 28/10/2026 14:05") && message.includes("version 5") && message.includes("brouillon est conservé"));
    assert.equal(messageRefus(409, { code: "version_depassee", versionCourante: 2 }).includes(" par "), false);
  });

  it("explique chaque refus", () => {
    assert.match(messageRefus(401, null), /reconnectez-vous/);
    assert.match(messageRefus(403, null), /ne modifiez pas/);
    assert.match(messageRefus(404, null), /n'existe pas/);
    assert.match(messageRefus(409, { code: "commande_reutilisee" }), /déjà servi/);
    assert.match(messageRefus(409, null), /modifié entre-temps/);
    assert.match(messageRefus(409, { code: "plan_existant", planId: "p" }), /déjà un plan/);
    assert.match(messageRefus(400, null), /rechargez la page/);
    assert.match(messageRefus(413, null), /25 Mo/);
    assert.equal(messageRefus(422, { code: "quota_atteint", title: "Quota atteint." }), "Quota atteint.");
    assert.equal(messageRefus(422, { code: "pdf_protege", title: "PDF protégé." }), "PDF protégé.");
    assert.equal(messageRefus(422, { code: "page_absente", title: "Page absente." }), "Page absente.");
    assert.equal(messageRefus(422, { code: "pdf_illisible" }), "Fichier refusé.");
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.fond.calibration.distance" }), /cote de calibration/);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.fond.fondId" }), /fond de plan/);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.pieces[0].contour" }), /pièce n° 1/);
    assert.match(messageRefus(422, { code: "donnee_invalide" }), /saisie/);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "version" }), /version lue/);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.nord" }), /nord/);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.composants[3].position" }), /marqueur de composant n° 4/);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.fond.decalage" }), /recalage du fond\./);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.ouvertures[0].zones" }), /ouverture n° 1\./);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "contenu.escaliers" }), /escalier ou ascenseur\./);
    assert.match(messageRefus(422, { code: "donnee_invalide", champ: "inconnu" }), /: saisie\./);
    assert.match(messageRefus(503, null), /indisponible/);
  });
});

describe("recalage du fond (S21-08)", () => {
  const contenu = {
    schema: 1, nord: null, altitude: null, hauteurSousPlafond: null,
    pieces: [{ zoneId: "z", contour: carre }],
    ouvertures: [{ type: "Porte", de: { x: 1, y: 0 }, a: { x: 1.9, y: 0 }, zones: ["z"] }],
    escaliers: [{ type: "Escalier", contour: carre, versNiveauId: "n" }],
    composants: [{ composantId: "c", position: { x: 2, y: 2 } }],
    fond: { ...calibre, decalage: { x: 1, y: 1 } },
  };

  it("lit le décalage, nul sans recalage", () => {
    assert.deepEqual(decalageFond(contenu), { x: 1, y: 1 });
    assert.deepEqual(decalageFond({ ...contenu, fond: calibre }), { x: 0, y: 0 });
    assert.deepEqual(decalageFond({ ...contenu, fond: null }), { x: 0, y: 0 });
  });

  it("pose le fond au décalage et déplace la géométrie tracée de la même translation", () => {
    const d = appliquerDecalage({ contenu, saisie: null, trace: null }, { x: 3.504, y: -0.5 });
    assert.deepEqual(d.contenu.fond.decalage, { x: 3.5, y: -0.5 });
    assert.deepEqual(d.contenu.pieces[0].contour[0], { x: 2.5, y: -1.5 });
    assert.deepEqual(d.contenu.ouvertures[0].a, { x: 4.4, y: -1.5 });
    assert.deepEqual(d.contenu.escaliers[0].contour[2], { x: 6.5, y: 2.5 });
    assert.deepEqual(d.contenu.composants[0].position, { x: 4.5, y: 0.5 });
  });

  it("ne change rien sans fond", () => {
    const d = { contenu: { ...contenu, fond: null }, saisie: null, trace: null };
    assert.equal(appliquerDecalage(d, { x: 1, y: 1 }), d);
  });
});

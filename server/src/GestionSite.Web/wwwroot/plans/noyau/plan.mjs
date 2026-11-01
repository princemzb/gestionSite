// Éditeur de plans de bâtiment (US-21, S21-07, ADR-11) : document de l'éditeur, calibration en cours, enregistrement,
// refus du serveur et textes affichés. Fonctions pures, sans DOM ni réseau.
// Document = { contenu (schéma 1, CONTRATS), saisie (calibration en cours, ou null) } : c'est l'état que l'historique
// (annuler, rétablir) et le brouillon local conservent.

import { defautFond, ecartControle, formaterEcart, metresParPixel, normaliserCote } from "./calibration.mjs";
import { bornesPoints, unirBornes } from "./vue.mjs";

export const SCHEMA = 1;

/** Cotes que l'utilisateur pose sur le fond. */
export const CIBLES = Object.freeze(["calibration", "controle"]);

const date = new Intl.DateTimeFormat("fr-FR", {
  timeZone: "Europe/Paris", day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
});

/** Contenu de schéma 1 complet (listes vides plutôt qu'absentes, fond null pour un plan vierge). */
export function normaliserContenu(contenu) {
  const c = contenu ?? {};
  return {
    schema: SCHEMA,
    nord: c.nord ?? null,
    altitude: c.altitude ?? null,
    hauteurSousPlafond: c.hauteurSousPlafond ?? null,
    pieces: c.pieces ?? [],
    ouvertures: c.ouvertures ?? [],
    escaliers: c.escaliers ?? [],
    composants: c.composants ?? [],
    fond: c.fond ?? null,
  };
}

/** Document de l'éditeur pour une version lue (GET building-plans/{id}). */
export function documentDepuisVersion(lue) {
  return { contenu: normaliserContenu(lue?.contenu), saisie: null };
}

/** Vrai si le contenu du document diffère de la version lue (un enregistrement est proposé). */
export function estModifie(document, reference) {
  return JSON.stringify(document.contenu) !== JSON.stringify(reference.contenu);
}

function coteVide() {
  return { de: null, a: null, distance: null };
}

/**
 * Commence (ou reprend) la calibration d'un fond : les cotes déjà enregistrées pour ce même fond sont reprises, sinon
 * elles sont vides (un autre fond se calibre de nouveau, RP-21.4).
 */
export function commencerCalibration(document, fondId) {
  const actuel = document.contenu.fond;
  const memeFond = actuel?.fondId === fondId;
  return {
    ...document,
    saisie: {
      fondId,
      calibration: memeFond ? { ...actuel.calibration } : coteVide(),
      controle: memeFond && actuel.controle ? { ...actuel.controle } : coteVide(),
    },
  };
}

function exigerCible(cible) {
  if (!CIBLES.includes(cible)) {
    throw new RangeError("Cote inconnue.");
  }

  return cible;
}

/**
 * Pose un point de la cote visée (pixels de l'image) : le premier, puis le second ; un troisième recommence la cote. La
 * distance déjà saisie est gardée.
 */
export function poserPoint(document, cible, point) {
  const saisie = document.saisie;
  if (!saisie) {
    return document;
  }

  const cote = saisie[exigerCible(cible)];
  const suivante = !cote.de || cote.a ? { ...cote, de: point, a: null } : { ...cote, a: point };
  return { ...document, saisie: { ...saisie, [cible]: suivante } };
}

/** Distance réelle saisie pour la cote visée (null pour l'effacer). */
export function fixerDistance(document, cible, distance) {
  const saisie = document.saisie;
  if (!saisie) {
    return document;
  }

  return { ...document, saisie: { ...saisie, [exigerCible(cible)]: { ...saisie[cible], distance } } };
}

/** Efface la cote visée (points et distance). */
export function effacerCote(document, cible) {
  const saisie = document.saisie;
  if (!saisie) {
    return document;
  }

  return { ...document, saisie: { ...saisie, [exigerCible(cible)]: coteVide() } };
}

function controleSaisi(controle) {
  return Boolean(controle && (controle.de || controle.a || controle.distance != null));
}

/**
 * Valide la calibration en cours sur l'image du fond : rend { document } (fond calibré dans le contenu, saisie close)
 * ou { defaut: { cote, defaut } } sans rien changer. Une cote de contrôle commencée doit être complète.
 */
export function validerCalibration(document, image) {
  const saisie = document.saisie;
  if (!saisie) {
    return { defaut: { cote: "calibration", defaut: "de" } };
  }

  const fond = { calibration: saisie.calibration, controle: controleSaisi(saisie.controle) ? saisie.controle : null };
  const defaut = defautFond(fond, image);
  if (defaut) {
    return { defaut };
  }

  const calibre = {
    fondId: saisie.fondId,
    calibration: normaliserCote(fond.calibration, image),
    controle: fond.controle ? normaliserCote(fond.controle, image) : null,
  };
  return { document: { contenu: { ...document.contenu, fond: calibre }, saisie: null } };
}

/** Ferme la calibration en cours sans rien changer au contenu. */
export function abandonnerSaisie(document) {
  return document.saisie ? { ...document, saisie: null } : document;
}

/** Plan vierge sur grille métrique : plus de fond. */
export function retirerFond(document) {
  return { contenu: { ...document.contenu, fond: null }, saisie: null };
}

/** Corps de l'enregistrement (PUT building-plans/{id}) à partir de la version lue. */
export function corpsEnregistrement(document, versionLue, commandeId, motif) {
  const texte = typeof motif === "string" ? motif.trim() : "";
  return { commandeId, version: versionLue, motif: texte === "" ? null : texte, contenu: document.contenu };
}

/** Mètres par pixel du fond calibré du contenu, ou null (plan vierge). */
export function echelleFond(contenu) {
  return contenu.fond ? metresParPixel(contenu.fond.calibration) : null;
}

/** Écart de la cote de contrôle enregistrée (calibration du contenu), ou null sans contrôle. */
export function controleFond(contenu) {
  const fond = contenu.fond;
  return fond?.controle ? ecartControle(fond.controle, metresParPixel(fond.calibration)) : null;
}

/** Bornes de la géométrie du contenu (pièces, escaliers, ouvertures, marqueurs), ou null s'il n'y en a pas. */
export function bornesContenu(contenu) {
  const points = [
    ...contenu.pieces.flatMap(p => p.contour),
    ...contenu.escaliers.flatMap(e => e.contour),
    ...contenu.ouvertures.flatMap(o => [o.de, o.a]),
    ...contenu.composants.map(m => m.position),
  ];
  return bornesPoints(points);
}

/** Bornes de tout ce qui s'affiche : géométrie et fond calibré (bornes données par l'appelant). */
export function bornesAffichees(contenu, bornesDuFond) {
  return unirBornes(bornesContenu(contenu), bornesDuFond);
}

/** Attribut points d'un polygone SVG. */
export function pointsSvg(contour) {
  return contour.map(p => `${p.x},${p.y}`).join(" ");
}

/** Aire d'un contour (m², formule du lacet). */
export function aire(contour) {
  let double = 0;
  for (let i = 0; i < contour.length; i++) {
    const p = contour[i];
    const q = contour[(i + 1) % contour.length];
    double += p.x * q.y - q.x * p.y;
  }

  return Math.abs(double) / 2;
}

/** Point où poser le nom d'une pièce : centre de gravité du contour (moyenne des sommets si l'aire est nulle). */
export function centre(contour) {
  let double = 0;
  let cx = 0;
  let cy = 0;
  for (let i = 0; i < contour.length; i++) {
    const p = contour[i];
    const q = contour[(i + 1) % contour.length];
    const produit = p.x * q.y - q.x * p.y;
    double += produit;
    cx += (p.x + q.x) * produit;
    cy += (p.y + q.y) * produit;
  }

  if (Math.abs(double) < 1e-9) {
    return { x: contour.reduce((s, p) => s + p.x, 0) / contour.length, y: contour.reduce((s, p) => s + p.y, 0) / contour.length };
  }

  return { x: cx / (3 * double), y: cy / (3 * double) };
}

/**
 * Niveau inférieur à un niveau du bâtiment (ordre le plus grand en dessous du sien), pour son calque ; null pour le
 * niveau le plus bas ou un plan masse.
 */
export function niveauInferieur(niveaux, niveauId) {
  const courant = niveaux.find(n => n.id === niveauId);
  if (!courant) {
    return null;
  }

  return niveaux.filter(n => n.ordre < courant.ordre).sort((a, b) => b.ordre - a.ordre)[0] ?? null;
}

/** Date et heure à Paris (« 28/10/2026 14:05 »). */
export function formaterDate(iso) {
  const valeur = new Date(iso);
  return Number.isNaN(valeur.getTime()) ? "" : date.format(valeur).replace(",", "");
}

/**
 * Sous-titre du plan : version, date et auteur de l'enregistrement, état de l'échelle (maquette A). Version 0 : rien
 * n'est encore enregistré.
 */
export function resumeVersion(lue, contenu) {
  if (!lue || lue.version === 0) {
    return "Aucune version enregistrée";
  }

  const parties = [`Version ${lue.version}`];
  const quand = lue.le ? formaterDate(lue.le) : "";
  parties.push(`enregistrée${quand ? ` le ${quand}` : ""}${lue.auteur ? ` par ${lue.auteur}` : ""}`);
  if (!contenu.fond) {
    parties.push("plan sur grille métrique");
  } else {
    const controle = controleFond(contenu);
    parties.push(controle ? `échelle calibrée (cote de contrôle ${formaterEcart(controle.ecart)})` : "échelle calibrée, sans cote de contrôle");
  }

  return parties.join(" · ");
}

/** Description d'un fond importé (liste GET …/backgrounds) : « PDF, page 2 sur 3, 1:100 · importé le … ». */
export function descriptionFond(fond) {
  const origine = fond.pdf
    ? `PDF, page ${fond.pdf.page} sur ${fond.pdf.pages}${fond.pdf.echelle ? `, 1:${fond.pdf.echelle}` : ""}`
    : `Image ${fond.largeurAffichage} × ${fond.hauteurAffichage} px`;
  const quand = fond.le ? formaterDate(fond.le) : "";
  return quand ? `${origine} · importé le ${quand}` : origine;
}

const champs = {
  "contenu.fond.fondId": "fond de plan",
  "contenu.fond.calibration": "cote de calibration",
  "contenu.fond.controle": "cote de contrôle",
  fichier: "fichier",
  page: "page du PDF",
  echelle: "échelle déclarée",
  motif: "motif",
};

function libelleChamp(champ) {
  if (typeof champ !== "string") {
    return "saisie";
  }

  const connu = Object.keys(champs).sort((a, b) => b.length - a.length).find(c => champ === c || champ.startsWith(`${c}.`));
  return connu ? champs[connu] : "géométrie du plan";
}

/**
 * Conflit d'enregistrement (409 « version_depassee ») : version gagnante, son auteur et sa date ; null pour tout autre
 * refus.
 */
export function conflit(statut, probleme) {
  if (statut !== 409 || probleme?.code !== "version_depassee") {
    return null;
  }

  return { versionCourante: probleme.versionCourante, auteur: probleme.auteur ?? null, modifieLe: probleme.modifieLe ?? null };
}

/** Message à l'utilisateur pour un refus du serveur (statut HTTP et ProblemDetails relayés par l'application web). */
export function messageRefus(statut, probleme) {
  const enConflit = conflit(statut, probleme);
  if (enConflit) {
    const qui = enConflit.auteur ? ` par ${enConflit.auteur}` : "";
    const quand = enConflit.modifieLe ? ` le ${formaterDate(enConflit.modifieLe)}` : "";
    return `Ce plan a été modifié entre-temps${qui}${quand} (version ${enConflit.versionCourante}) : rien n'a été écrasé. Votre brouillon est conservé sur cet appareil.`;
  }

  switch (statut) {
    case 401:
      return "Votre session a expiré : reconnectez-vous. Votre brouillon est conservé sur cet appareil.";
    case 403:
      return "Vous ne modifiez pas les plans de ce site.";
    case 404:
      return "Ce plan n'existe pas ou n'est plus accessible.";
    case 409:
      return probleme?.code === "commande_reutilisee"
        ? "Cet envoi a déjà servi : rechargez la page."
        : "Ce plan a été modifié entre-temps : rechargez-le.";
    case 422:
      if (probleme?.code === "quota_atteint" || (typeof probleme?.code === "string" && probleme.code.startsWith("pdf_")) || probleme?.code === "page_absente") {
        return probleme.title ?? "Fichier refusé.";
      }

      return `Saisie refusée par le serveur : ${libelleChamp(probleme?.champ)}.`;
    default:
      return "Service momentanément indisponible : réessayez dans un instant. Votre brouillon est conservé sur cet appareil.";
  }
}

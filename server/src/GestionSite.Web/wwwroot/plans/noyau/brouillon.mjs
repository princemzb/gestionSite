// Éditeur de plans de bâtiment (US-21, S21-07, RP-21.8, ADR-11) : brouillon local, simple confort.
// Une clé par compte, organisation et plan : le brouillon d'un compte n'est jamais lu ni gardé pour un autre compte
// (tablette partagée). Le stockage (localStorage dans le navigateur) est passé en paramètre ; chaque accès est protégé
// (navigation privée, quota atteint, stockage désactivé) : un échec ne bloque jamais l'éditeur.

export const PREFIXE = "gestionsite.brouillon-plan.v1:";

/** Taille maximale d'un brouillon (celle d'un enregistrement accepté par le serveur). */
export const TAILLE_MAX = 4 * 1024 * 1024;

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function exigerGuid(valeur, nom) {
  if (typeof valeur !== "string" || !GUID.test(valeur)) {
    throw new RangeError(`Identifiant invalide : ${nom}.`);
  }

  return valeur.toLowerCase();
}

/** Clé du brouillon d'un plan pour un compte dans une organisation. */
export function cleBrouillon(compteId, organisationId, planId) {
  return `${PREFIXE}${exigerGuid(compteId, "compte")}:${exigerGuid(organisationId, "organisation")}:${exigerGuid(planId, "plan")}`;
}

/** Compte propriétaire d'une clé de brouillon, ou null pour une clé étrangère à l'éditeur. */
export function compteDeCle(cle) {
  if (typeof cle !== "string" || !cle.startsWith(PREFIXE)) {
    return null;
  }

  const compte = cle.slice(PREFIXE.length).split(":")[0];
  return GUID.test(compte) ? compte.toLowerCase() : null;
}

/**
 * Brouillon enregistré pour cette clé, ou null : absent, illisible, ou appartenant à un autre compte, une autre
 * organisation ou un autre plan (vérifié dans le contenu, pas seulement dans la clé).
 */
export function lireBrouillon(stockage, cle) {
  let texte;
  try {
    texte = stockage.getItem(cle);
  } catch {
    return null;
  }

  if (typeof texte !== "string") {
    return null;
  }

  let brouillon;
  try {
    brouillon = JSON.parse(texte);
  } catch {
    return null;
  }

  const attendue = brouillon && typeof brouillon === "object" && typeof brouillon.compteId === "string"
    && typeof brouillon.organisationId === "string" && typeof brouillon.planId === "string"
    ? cleDuBrouillon(brouillon)
    : null;
  if (attendue !== cle || !Number.isInteger(brouillon.versionLue) || brouillon.versionLue < 0 || !documentLisible(brouillon.document)
    || typeof brouillon.modifieLe !== "string") {
    return null;
  }

  return brouillon;
}

const objet = valeur => valeur !== null && typeof valeur === "object" && !Array.isArray(valeur);

// Document de l'éditeur (plan.mjs) : contenu de schéma 1 avec ses listes, saisie en cours ou null. Un brouillon d'un
// autre format (ancien, tronqué) est ignoré plutôt que de bloquer l'ouverture de l'éditeur.
function documentLisible(document) {
  const contenu = objet(document) ? document.contenu : null;
  return objet(contenu) && contenu.schema === 1
    && ["pieces", "ouvertures", "escaliers", "composants"].every(liste => Array.isArray(contenu[liste]))
    && (contenu.fond === null || objet(contenu.fond))
    && (document.saisie === null || objet(document.saisie));
}

function cleDuBrouillon(brouillon) {
  try {
    return cleBrouillon(brouillon.compteId, brouillon.organisationId, brouillon.planId);
  } catch {
    return null;
  }
}

/**
 * Écrit le brouillon (document de l'éditeur et version lue) ; rend false si le stockage le refuse ou si le brouillon
 * dépasse 4 Mo.
 */
export function ecrireBrouillon(stockage, { compteId, organisationId, planId, versionLue, document, modifieLe }) {
  const cle = cleBrouillon(compteId, organisationId, planId);
  const texte = JSON.stringify({ compteId: compteId.toLowerCase(), organisationId: organisationId.toLowerCase(), planId: planId.toLowerCase(), versionLue, document, modifieLe });
  if (texte.length > TAILLE_MAX) {
    return false;
  }

  try {
    stockage.setItem(cle, texte);
    return true;
  } catch {
    return false;
  }
}

/** Efface un brouillon ; rend false si le stockage le refuse. */
export function effacerBrouillon(stockage, cle) {
  try {
    stockage.removeItem(cle);
    return true;
  } catch {
    return false;
  }
}

// Clés des brouillons de l'éditeur présentes dans le stockage (lecture protégée).
function clesBrouillons(stockage) {
  const cles = [];
  try {
    for (let i = 0; i < stockage.length; i++) {
      const cle = stockage.key(i);
      if (typeof cle === "string" && cle.startsWith(PREFIXE)) {
        cles.push(cle);
      }
    }
  } catch {
    return cles;
  }

  return cles;
}

/**
 * Efface les brouillons des autres comptes (et ceux dont la clé est illisible) : rend le nombre de brouillons effacés.
 * Appelée à l'ouverture de l'éditeur, elle complète l'effacement à la déconnexion (Clear-Site-Data).
 */
export function purgerAutresComptes(stockage, compteId) {
  const moi = exigerGuid(compteId, "compte");
  let effaces = 0;
  for (const cle of clesBrouillons(stockage)) {
    if (compteDeCle(cle) !== moi && effacerBrouillon(stockage, cle)) {
      effaces++;
    }
  }

  return effaces;
}

/** Efface tous les brouillons de l'éditeur, quel que soit le compte : rend le nombre effacé. */
export function purgerTout(stockage) {
  let effaces = 0;
  for (const cle of clesBrouillons(stockage)) {
    if (effacerBrouillon(stockage, cle)) {
      effaces++;
    }
  }

  return effaces;
}

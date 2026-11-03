// Éditeur de plans de bâtiment (US-21, S21-09, RP-21.8, ADR-11) : historique des versions et restauration. Fonctions
// pures, sans DOM ni réseau : textes de la liste paginée, corps de la restauration (motif obligatoire), bilan et refus.

import { formaterDate, messageRefus } from "./plan.mjs";

/** Versions demandées par page (l'API en accepte de 1 à 200). */
export const VERSIONS_PAR_PAGE = 20;

/** Ligne d'une version dans l'historique : « Version 3 · 28/10/2026 14:05 · Rose R. ». */
export function libelleVersion(version) {
  const quand = version.le ? formaterDate(version.le) : "";
  return [`Version ${version.version}`, quand, version.auteur].filter(Boolean).join(" · ");
}

/** Précisions d'une version : motif entre guillemets, version restaurée ; chaîne vide sans l'un ni l'autre. */
export function precisionsVersion(version) {
  const parties = [];
  if (version.restaureDe != null) {
    parties.push(`restauration de la version ${version.restaureDe}`);
  }

  if (version.motif) {
    parties.push(`« ${version.motif} »`);
  }

  return parties.join(" · ");
}

/** Numéro à passer en `avant` pour la page suivante (plus ancienne), ou null si la page reçue est la dernière. */
export function avantSuivant(page, limite = VERSIONS_PAR_PAGE) {
  return Array.isArray(page) && page.length >= limite && page.length > 0 ? page.at(-1).version : null;
}

/** Ajoute une page à la liste déjà affichée, sans doublon (un renvoi de la même page ne répète rien). */
export function ajouterPage(versions, page) {
  const connues = new Set(versions.map(v => v.version));
  return [...versions, ...(page ?? []).filter(v => !connues.has(v.version))];
}

/**
 * Corps de la restauration (POST …/versions/{n}/restauration) à partir de la version lue (la dernière) : null si le motif
 * est vide (obligatoire, RP-21.8).
 */
export function corpsRestauration(versionLue, commandeId, motif) {
  const texte = typeof motif === "string" ? motif.trim() : "";
  return texte === "" ? null : { commandeId, version: versionLue, motif: texte };
}

/**
 * Bilan d'une restauration réussie : nouvelle version, marqueurs retirés de la copie (avertissement « marqueur_retire » :
 * composant retiré, passé sur un autre niveau ou devenu objet de valeur). Rend { texte, genre }.
 */
export function bilanRestauration(source, reponse) {
  const retires = (reponse?.avertissements ?? []).filter(a => a.code === "marqueur_retire").length;
  const texte = `Version ${source} restaurée : elle devient la version ${reponse?.version}.`;
  if (retires === 0) {
    return { texte, genre: "succes" };
  }

  const pluriel = retires > 1;
  return {
    texte: `${texte} ${retires} marqueur${pluriel ? "s" : ""} de composant ${pluriel ? "ont été retirés" : "a été retiré"} de la copie : composant retiré de l'inventaire, passé sur un autre niveau ou devenu objet de valeur.`,
    genre: "avertissement",
  };
}

/** Message à l'utilisateur pour un refus de restauration (conflit, motif, session) ; les autres refus comme un enregistrement. */
export function messageRefusRestauration(statut, probleme) {
  if (statut === 409 && probleme?.code === "version_depassee") {
    const qui = probleme.auteur ? ` par ${probleme.auteur}` : "";
    const quand = probleme.modifieLe ? ` le ${formaterDate(probleme.modifieLe)}` : "";
    return `Ce plan a été modifié entre-temps${qui}${quand} (version ${probleme.versionCourante}) : rien n'a été restauré. Rechargez la page, puis recommencez.`;
  }

  if (statut === 422 && probleme?.champ === "motif") {
    return "Motif obligatoire : indiquez pourquoi vous restaurez cette version (une ligne, 500 caractères au plus).";
  }

  if (statut === 401) {
    return "Votre session a expiré : reconnectez-vous.";
  }

  return messageRefus(statut, probleme);
}

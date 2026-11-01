// Éditeur de plans de bâtiment (US-21, S21-07, ADR-11) : annuler et rétablir.
// Historique immuable d'états du document (contenu du plan et saisie en cours) : chaque modification ajoute l'état
// précédent au passé et vide l'avenir. Les états ne sont jamais modifiés en place : l'éditeur en crée de nouveaux.

/** Nombre d'étapes gardées pour « Annuler » (les plus anciennes sont oubliées). */
export const LIMITE_HISTORIQUE = 100;

/** Historique initial : l'état chargé, rien à annuler ni à rétablir. */
export function creerHistorique(present, limite = LIMITE_HISTORIQUE) {
  if (!Number.isInteger(limite) || limite < 1) {
    throw new RangeError("Limite d'historique invalide.");
  }

  return { passe: [], present, futur: [], limite };
}

/** Nouvel état après une modification ; identique au présent (même objet) : rien n'est ajouté. */
export function appliquer(historique, etat) {
  if (etat === historique.present) {
    return historique;
  }

  const passe = [...historique.passe, historique.present].slice(-historique.limite);
  return { ...historique, passe, present: etat, futur: [] };
}

export function peutAnnuler(historique) {
  return historique.passe.length > 0;
}

export function peutRetablir(historique) {
  return historique.futur.length > 0;
}

/** Retour à l'état précédent (sans effet s'il n'y en a pas). */
export function annuler(historique) {
  if (!peutAnnuler(historique)) {
    return historique;
  }

  const precedent = historique.passe.at(-1);
  return { ...historique, passe: historique.passe.slice(0, -1), present: precedent, futur: [historique.present, ...historique.futur] };
}

/** Rétablit l'état annulé (sans effet s'il n'y en a pas). */
export function retablir(historique) {
  if (!peutRetablir(historique)) {
    return historique;
  }

  const [suivant, ...reste] = historique.futur;
  return { ...historique, passe: [...historique.passe, historique.present], present: suivant, futur: reste };
}

/** Recommence l'historique sur un état (plan rechargé ou enregistré) : plus rien à annuler. */
export function reinitialiser(historique, present) {
  return creerHistorique(present, historique.limite);
}

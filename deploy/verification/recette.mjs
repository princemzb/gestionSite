// Mode recette commun aux vérifications usXX.mjs (OUT-02, décision du porteur du 03/11) : avec RECETTE=1, le parcours se
// déroule à l'écran, à vitesse humaine (navigateur visible, slowMo, pauses), un bandeau injecté dans la page affiche l'étape
// en cours et le résultat de chaque contrôle, chaque contexte est filmé, une capture est prise à chaque contrôle, puis un
// rapport HTML conclut « Recette réussie » ou « Recette échouée ». Sans RECETTE, rien ne change : Chromium sans interface,
// aucune pause, ni vidéo ni rapport, sortie « OK / ÉCHEC » identique.
// Le bandeau ne passe par aucun script de la page : Playwright l'évalue par le protocole de débogage (hors CSP), il est
// mis en forme par le CSSOM (autorisé par « style-src 'self' », contrairement à une balise <style> ou à un attribut style)
// et rangé dans une racine fantôme fermée : il ne compte ni dans les textes lus par les vérifications, ni pour les
// sélecteurs, et laisse passer les clics (pointer-events: none).
// Réglages : RECETTE_DOSSIER (dossier des rapports, défaut deploy/verification/recettes/, ignoré par git),
// RECETTE_LENTEUR (ms entre deux actions, défaut 400), RECETTE_PAUSE (ms après chaque contrôle, défaut 1200).
import { chromium } from "playwright";
import { mkdirSync, renameSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const actif = process.env.RECETTE === "1";
const lenteur = Number(process.env.RECETTE_LENTEUR ?? 400);
const pause = Number(process.env.RECETTE_PAUSE ?? 1200);
const attendre = ms => new Promise(r => setTimeout(r, ms));
const echapper = t => String(t).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
// Méthodes d'une page qui en font la page « courante » (celle que montrent le bandeau et les captures) ; les actions
// passant par page.mouse, page.keyboard ou une session CDP suivent en général une de ces méthodes sur la même page.
const ACTIONS = ["goto", "reload", "click", "fill", "check", "selectOption", "setInputFiles", "press", "textContent", "inputValue",
  "getAttribute", "locator", "isVisible", "waitForSelector", "waitForFunction", "waitForLoadState", "evaluate", "setViewportSize", "screenshot"];

// Dessiné dans la page par Playwright (monde principal, hors CSP) ; aucune balise <style>, aucun attribut style.
function dessinerBandeau([us, etape, resultat, couleur]) {
  let hote = document.getElementById("recette-bandeau");
  if (!hote) {
    hote = document.createElement("div");
    hote.id = "recette-bandeau";
    hote.setAttribute("aria-hidden", "true");
    Object.assign(hote.style, { position: "fixed", left: "0", right: "0", bottom: "0", zIndex: "2147483647", pointerEvents: "none" });
    const ombre = hote.attachShadow({ mode: "closed" });
    const boite = document.createElement("div");
    Object.assign(boite.style, {
      margin: "0 auto", maxWidth: "1100px", padding: "10px 16px", background: "rgba(17, 24, 39, 0.92)", color: "#fff",
      font: "15px/1.35 system-ui, sans-serif", borderTop: "4px solid #9ca3af", boxShadow: "0 -4px 16px rgba(0,0,0,.25)",
    });
    const ligne1 = document.createElement("div");
    Object.assign(ligne1.style, { fontWeight: "600", opacity: "0.85", fontSize: "13px" });
    const ligne2 = document.createElement("div");
    Object.assign(ligne2.style, { fontWeight: "700", marginTop: "2px" });
    boite.append(ligne1, ligne2);
    ombre.append(boite);
    hote.recette = { boite, ligne1, ligne2 };
    document.documentElement.append(hote);
  }
  const { boite, ligne1, ligne2 } = hote.recette;
  ligne1.textContent = `Recette ${us} · ${etape}`;
  ligne2.textContent = resultat;
  ligne2.style.color = couleur;
  boite.style.borderTopColor = couleur;
}

function horodatage(d = new Date()) {
  const n = v => String(v).padStart(2, "0");
  return `${d.getFullYear()}-${n(d.getMonth() + 1)}-${n(d.getDate())}-${n(d.getHours())}${n(d.getMinutes())}${n(d.getSeconds())}`;
}

/** Outils de vérification d'une US (nom : « us08 »…) ; en mode recette, le dossier du rapport devient le dossier courant. */
export function recette(us) {
  const debut = new Date();
  const journal = [];
  const contextes = [];
  const libelles = new WeakMap();
  const etat = { etape: "démarrage", resultat: "…", couleur: "#9ca3af", controles: 0 };
  let navigateur;
  let courante;
  let fini = false;
  let dossier;
  if (actif) {
    const racine = resolve(process.env.RECETTE_DOSSIER ?? join(dirname(fileURLToPath(import.meta.url)), "recettes"));
    dossier = join(racine, `recette-${us}-${horodatage(debut)}`);
    mkdirSync(join(dossier, "captures"), { recursive: true });
    process.chdir(dossier); // les captures nommées des scripts rejoignent le rapport
    const surErreur = async e => {
      console.log(`ÉCHEC erreur : ${e?.message ?? e}`);
      await consigner(`Erreur : ${(e?.message ?? String(e)).split("\n")[0]}`, false, (e?.message ?? "").split("\n").slice(1, 4).join(" "));
      await conclure(false);
      process.exit(1);
    };
    process.on("uncaughtException", surErreur);
    process.on("unhandledRejection", surErreur);
  }

  const afficher = async page => {
    if (!actif || !page || page.isClosed()) return;
    try {
      await Object.getPrototypeOf(page).evaluate.call(page, dessinerBandeau, [us.toUpperCase(), etat.etape, etat.resultat, etat.couleur]);
    } catch {
      // page en cours de navigation : le bandeau sera redessiné au chargement suivant
    }
  };

  const suivre = (page, libelle) => {
    libelles.set(page, libelle);
    courante ??= page;
    // Surcharge propre à cette page (le prototype partagé de Playwright reste intact) : une action la rend courante.
    const prototype = Object.getPrototypeOf(page);
    for (const nom of ACTIONS) page[nom] = (...args) => { courante = page; return prototype[nom].apply(page, args); };
    page.on("load", () => { afficher(page); });
  };

  async function consigner(nom, reussi, detail = "") {
    etat.resultat = `${reussi ? "✓" : "✗"} ${nom}${detail ? ` (${detail})` : ""}`;
    etat.couleur = reussi ? "#4ade80" : "#f87171";
    if (reussi) etat.controles++;
    await afficher(courante);
    let capture = null;
    if (courante && !courante.isClosed()) {
      capture = `captures/${String(journal.filter(e => e.type === "controle").length + 1).padStart(2, "0")}.png`;
      try { await Object.getPrototypeOf(courante).screenshot.call(courante, { path: capture }); } catch { capture = null; }
    }
    journal.push({ type: "controle", nom, reussi, detail, capture, page: libelles.get(courante) ?? "", t: new Date() });
  }

  async function conclure(reussie) {
    if (fini) return;
    fini = true;
    const videos = [];
    for (const [i, { contexte, libelle }] of contextes.entries()) {
      const pages = contexte.pages();
      try { await contexte.close(); } catch { /* déjà fermé */ }
      for (const [j, page] of pages.entries()) {
        try {
          const nom = `videos/${String(i + 1).padStart(2, "0")}-${libelle.replace(/[^\p{L}\d]+/gu, "-").replace(/^-|-$/g, "")}${j ? `-${j + 1}` : ""}.webm`;
          renameSync(await page.video().path(), nom);
          videos.push({ nom, libelle });
        } catch { /* pas de vidéo pour cette page */ }
      }
    }
    try { await navigateur?.close(); } catch { /* déjà fermé */ }
    rmSync("videos-brutes", { recursive: true, force: true });
    const conclusion = reussie ? "Recette réussie" : "Recette échouée";
    const lignes = journal.map(e => e.type === "etape"
      ? `<tr class="etape"><td colspan="4">${echapper(e.titre)}</td></tr>`
      : `<tr class="${e.reussi ? "ok" : "ko"}"><td class="statut">${e.reussi ? "✓" : "✗"}</td><td>${echapper(e.nom)}${e.detail ? `<div class="detail">${echapper(e.detail)}</div>` : ""}</td>`
        + `<td>${echapper(e.page)}<div class="detail">${e.t.toLocaleTimeString("fr-FR")}</div></td>`
        + `<td>${e.capture ? `<a href="${e.capture}"><img src="${e.capture}" alt="Capture" loading="lazy"></a>` : "—"}</td></tr>`).join("\n");
    const controles = journal.filter(e => e.type === "controle");
    const html = `<!doctype html><html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Recette ${echapper(us.toUpperCase())}</title><style>
body{font:15px/1.45 system-ui,sans-serif;margin:0 auto;max-width:1200px;padding:16px;color:#111827;background:#fff}
h1{margin:.2em 0}.conclusion{font-size:22px;font-weight:700;padding:12px 16px;border-radius:8px;margin:12px 0}
.reussie{background:#dcfce7;color:#14532d}.echouee{background:#fee2e2;color:#7f1d1d}
table{border-collapse:collapse;width:100%}td{border-bottom:1px solid #e5e7eb;padding:6px 8px;vertical-align:top}
tr.etape td{background:#f3f4f6;font-weight:700;padding-top:12px}.statut{font-size:20px;width:28px;text-align:center}
tr.ok .statut{color:#15803d}tr.ko .statut{color:#b91c1c}tr.ko{background:#fef2f2}.detail{color:#6b7280;font-size:13px}
img{width:220px;border:1px solid #d1d5db;border-radius:4px}video{width:100%;max-width:720px;display:block;margin:6px 0 16px}
</style></head><body>
<h1>Recette ${echapper(us.toUpperCase())}</h1>
<p>Parcours automatisé en préproduction, joué à vitesse humaine dans un navigateur visible (Playwright, Chromium ${echapper(navigateur?.version() ?? "")}).
Début ${debut.toLocaleString("fr-FR")}, durée ${Math.round((Date.now() - debut) / 1000)} s ; organisation fictive de vérification.</p>
<p class="conclusion ${reussie ? "reussie" : "echouee"}">${conclusion} : ${controles.filter(e => e.reussi).length} contrôle(s) réussi(s) sur ${controles.length}.</p>
<h2>Étapes</h2><table>${lignes}</table>
<h2>Vidéos</h2>${videos.map(v => `<h3>${echapper(v.libelle)}</h3><video controls preload="metadata" src="${v.nom}"></video><a href="${v.nom}">${v.nom}</a>`).join("\n") || "<p>Aucune vidéo.</p>"}
</body></html>`;
    writeFileSync("rapport.html", html);
    console.log(`${conclusion}. Rapport : ${join(dossier, "rapport.html")}`);
    for (const v of videos) console.log(`Vidéo (${v.libelle}) : ${join(dossier, v.nom)}`);
  }

  return {
    actif,
    /** Chromium : sans interface hors recette ; visible et ralenti en recette. */
    async lancer(options = {}) {
      navigateur = await chromium.launch(actif ? { ...options, headless: false, slowMo: lenteur } : options);
      return navigateur;
    },
    /** Nouveau contexte ; en recette, filmé (taille de la fenêtre) et suivi par le bandeau. */
    async contexte(options = {}, libelle = `Navigateur ${contextes.length + 1}`) {
      const video = actif ? { recordVideo: { dir: join(dossier, "videos-brutes"), size: options.viewport ?? { width: 1280, height: 720 } } } : {};
      const contexte = await navigateur.newContext({ ...options, ...video });
      if (actif) {
        contextes.push({ contexte, libelle });
        contexte.on("page", page => suivre(page, libelle));
        mkdirSync(join(dossier, "videos"), { recursive: true });
      }
      return contexte;
    },
    /** Début d'une étape du parcours (titre affiché dans le bandeau et le rapport). */
    async etape(titre) {
      if (!actif) return;
      console.log(`— ${titre}`);
      Object.assign(etat, { etape: titre, resultat: "en cours…", couleur: "#9ca3af" });
      journal.push({ type: "etape", titre, t: new Date() });
      await afficher(courante);
      await attendre(pause * 2 / 3);
    },
    /** Contrôle : « OK / ÉCHEC » à la console ; un échec arrête la vérification (code 1) après le rapport en recette. */
    async ok(nom, cond, detail = "") {
      console.log((cond ? "OK    " : "ÉCHEC ") + nom + (detail ? ` (${detail})` : ""));
      if (!actif) {
        if (!cond) process.exit(1);
        return;
      }
      await consigner(nom, Boolean(cond), detail);
      await attendre(pause);
      if (!cond) {
        await conclure(false);
        process.exit(1);
      }
    },
    /** Fin du parcours : ferme le navigateur ; en recette, vidéos finalisées et rapport « Recette réussie ». */
    async terminer() {
      if (!actif) {
        await navigateur.close();
        return;
      }
      Object.assign(etat, { etape: "fin du parcours", resultat: `✓ Recette réussie : ${etat.controles} contrôle(s)`, couleur: "#4ade80" });
      await afficher(courante);
      await attendre(pause * 2);
      await conclure(true);
    },
  };
}

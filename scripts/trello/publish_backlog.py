#!/usr/bin/env python3
"""Publie docs/pilotage/trello-backlog.json dans Trello, de façon idempotente.

Usage :
    python3 scripts/trello/publish_backlog.py            # simulation, aucune écriture
    python3 scripts/trello/publish_backlog.py --apply    # publication réelle

Identifiants : Trousseau macOS, services gestionsite-trello-key et gestionsite-trello-token.
Les identifiants Trello créés sont réécrits dans le JSON (boardId, listIds, labelIds, trelloId).

Après publication, Trello fait foi pour la liste, les membres et l'état des checklists :
une relance met à jour titre, description et labels, sans déplacer les cartes.
"""
import argparse
import http.client
import json
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BACKLOG = ROOT / "docs" / "pilotage" / "trello-backlog.json"
REPO_URL = "https://github.com/princemzb/gestionSite/blob/main"
API = "https://api.trello.com/1"
WORKSPACE_NAME = "gestionsite_royal"

# Couleurs Trello disponibles : green, yellow, orange, red, purple, blue, sky, lime, pink, black (+ _light/_dark).
LABEL_COLORS = {
    "BA": "purple", "POC": "orange", "Préparation": "yellow", "Socle": "black",
    "Identité": "blue", "Patrimoine": "green", "Maintenance": "sky", "Terrain": "lime",
    "Stock": "green_dark", "Budget": "orange_dark", "IA": "pink", "Spatial": "purple_dark",
    "Pilotage": "blue_dark", "SaaS": "sky_dark", "Exploitation": "black_dark",
    "Livraison": "lime_dark", "Décision requise": "red", "Incident critique": "red_dark",
    "SP-001": "yellow_light", "SP-002": "yellow_light", "SP-003": "yellow_light",
}

CHECKLIST_NAME = "Définition de terminé"
TYPE_LABELS = {"macro_story": "Macro-story", "preparation": "Carte de préparation", "outillage": "Outillage"}


class TrelloError(Exception):
    """Échec d'un appel Trello (HTTP ou réseau)."""


def is_transient(err):
    """Erreur réseau passagère (délai, coupure) ; pas un défaut de certificat ou de DNS."""
    reason = getattr(err, "reason", err)
    return isinstance(reason, (TimeoutError, socket.timeout, ConnectionError, http.client.HTTPException))


def load():
    return json.loads(BACKLOG.read_text(encoding="utf-8"))


def save(data, api):
    """Enregistre les identifiants après chaque création, pour qu'une reprise ne duplique rien."""
    if api.apply:
        BACKLOG.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def keychain(service):
    out = subprocess.run(["security", "find-generic-password", "-s", service, "-w"],
                         capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"Identifiant introuvable dans le Trousseau : {service}")
    return out.stdout.strip()


class Trello:
    def __init__(self, apply):
        self.apply = apply
        self.auth = {"key": keychain("gestionsite-trello-key"),
                     "token": keychain("gestionsite-trello-token")}
        self.writes = 0

    def call(self, method, path, **params):
        if method != "GET":
            self.writes += 1
            if not self.apply:
                return {"id": f"<simulé:{path}>"}
        # Paramètres dans le corps pour les écritures : un long commentaire ne dépasse pas la taille d'URL.
        auth = urllib.parse.urlencode(self.auth)
        if method == "GET":
            req = urllib.request.Request(f"{API}{path}?{auth}&{urllib.parse.urlencode(params, doseq=True)}")
        else:
            req = urllib.request.Request(f"{API}{path}?{auth}", method=method,
                                         data=urllib.parse.urlencode(params, doseq=True).encode())
        # GET et PUT sont rejouables : nouvelles tentatives sur erreurs transitoires.
        # POST/DELETE ne sont rejoués que sur 429 (requête refusée avant traitement).
        idempotent = method in {"GET", "PUT"}
        for attempt in range(5):
            last = attempt == 4
            try:
                with urllib.request.urlopen(req, timeout=30) as resp:
                    time.sleep(0.12)  # limite Trello : 100 requêtes / 10 s par jeton
                    return json.loads(resp.read() or b"null")
            except urllib.error.HTTPError as err:
                if not last and (err.code == 429 or (idempotent and err.code >= 500)):
                    err.close()
                    time.sleep(2 ** attempt)
                    continue
                try:
                    body = err.read().decode(errors="replace")[:300]
                except (OSError, http.client.HTTPException):
                    body = "(corps de réponse illisible)"
                finally:
                    err.close()
                raise TrelloError(f"Erreur Trello {err.code} sur {method} {path} : {body}") from err
            except (OSError, http.client.HTTPException) as err:  # URLError, socket.timeout (3.9), ConnectionError
                if idempotent and not last and is_transient(err):
                    time.sleep(2 ** attempt)
                    continue
                raise TrelloError(f"Trello injoignable sur {method} {path} : {err}") from err
            except json.JSONDecodeError as err:
                raise TrelloError(f"Réponse Trello illisible sur {method} {path}") from err


def card_description(card, cards_by_id):
    est = card["estimatePersonDays"]
    deps = [cards_by_id.get(d, {}) for d in card.get("dependencies", [])]
    dep_text = ", ".join(
        f"[{d['id']}](https://trello.com/c/{d['trelloShortLink']})" if d.get("trelloShortLink") else d.get("id", "?")
        for d in deps) or "aucune"
    lines = [
        f"**Identifiant :** {card['id']} · **Type :** {TYPE_LABELS.get(card['type'], card['type'])}",
        f"**Lot :** {card.get('lot') or '—'} · **Sprint :** {card.get('sprint') or '—'}",
        f"**Estimation :** {est['low']}–{est['high']} j-p"
        + (f" ({est.get('includes') or est.get('accounting')})" if est.get("includes") or est.get("accounting") else ""),
        f"**Responsable :** {card.get('owner') or 'à affecter'}",
        f"**Dépendances :** {dep_text}",
    ]
    if card.get("parentStories"):
        lines.append(f"**Stories parentes :** {', '.join(card['parentStories'])}")
    if card.get("rules"):
        lines.append(f"**Règles :** {', '.join(card['rules'])} · **Tests :** {', '.join(card.get('testSuites', []))}")
    lines += ["", "### Critères d’acceptation"] + [f"- {c}" for c in card["acceptanceCriteria"]]
    if card.get("prerequisites"):
        lines += ["", f"**Prérequis :** {card['prerequisites']}"]
    if card.get("progress"):
        lines += ["", "### Avancement", card["progress"]]
    lines += ["", "### Références",
              card.get("description") or "",
              f"[Backlog]({REPO_URL}/docs/pilotage/BACKLOG.md) · [Règles]({REPO_URL}/docs/metier/REGLES.md) · "
              f"[Conditions de passage]({REPO_URL}/docs/pilotage/TRELLO.md)"]
    return "\n".join(lines).strip()


def ensure_board(api, data):
    if data.get("boardId"):
        print(f"Tableau existant : {data['boardId']}")
        return data["boardId"]
    print(f"+ Créer le tableau « {data['boardName']} » dans l'espace {WORKSPACE_NAME}")
    board = api.call("POST", "/boards", name=data["boardName"], idOrganization=WORKSPACE_NAME,
                     defaultLists="false", defaultLabels="false", prefs_permissionLevel="org")
    data["boardId"] = board["id"]
    save(data, api)
    return board["id"]


def open_lists(api, board_id):
    """Listes ouvertes du tableau, dans l'ordre du tableau : {id: nom}."""
    return {l["id"]: l["name"] for l in api.call("GET", f"/boards/{board_id}/lists", filter="open")}


def ensure_lists(api, data, board_id):
    ids = data.setdefault("listIds", {})
    existing = {} if not api.apply and board_id.startswith("<") else {
        name: list_id for list_id, name in open_lists(api, board_id).items()}
    for pos, name in enumerate(data["lists"], start=1):
        if name in ids or name in existing:
            ids[name] = ids.get(name) or existing[name]
            continue
        print(f"+ Liste « {name} »")
        ids[name] = api.call("POST", "/lists", name=name, idBoard=board_id, pos=pos * 1000)["id"]
        save(data, api)
    return ids


def ensure_labels(api, data, board_id):
    ids = data.setdefault("labelIds", {})
    wanted = sorted({l for c in data["cards"] for l in c["labels"]}
                    | {c["sprint"] for c in data["cards"] if c.get("sprint")}
                    | {"Décision requise", "Incident critique"})
    existing = {} if not api.apply and board_id.startswith("<") else {
        l["name"]: l["id"] for l in api.call("GET", f"/boards/{board_id}/labels", fields="name")}
    for name in wanted:
        if name in ids or name in existing:
            ids[name] = ids.get(name) or existing[name]
            continue
        print(f"+ Label « {name} »")
        ids[name] = api.call("POST", "/labels", name=name, color=LABEL_COLORS.get(name, "sky"),
                             idBoard=board_id)["id"]
        save(data, api)
    return ids


def card_labels(card, label_ids):
    names = list(card["labels"]) + ([card["sprint"]] if card.get("sprint") else [])
    return ",".join(label_ids[n] for n in names)


def ensure_checklist(api, data, card):
    """Crée la checklist et ses éléments manquants ; reprend une création interrompue."""
    if not card.get("checklist") or card.get("checklistComplete"):
        return
    if not card.get("checklistId"):
        found = [] if card["trelloId"].startswith("<") else [
            c["id"] for c in api.call("GET", f"/cards/{card['trelloId']}/checklists", fields="name")
            if c["name"] == CHECKLIST_NAME]
        card["checklistId"] = found[0] if found else api.call(
            "POST", "/checklists", idCard=card["trelloId"], name=CHECKLIST_NAME)["id"]
        save(data, api)
    existing = set() if card["checklistId"].startswith("<") else {
        i["name"] for i in api.call("GET", f"/checklists/{card['checklistId']}/checkItems")}
    for item in card["checklist"]:
        if item not in existing:
            api.call("POST", f"/checklists/{card['checklistId']}/checkItems", name=item)
    card["checklistComplete"] = True
    save(data, api)


def publish_cards(api, data, list_ids, label_ids, member_id):
    cards_by_id = {c["id"]: c for c in data["cards"]}
    created = updated = 0
    for pos, card in enumerate(data["cards"], start=1):
        desc = card_description(card, cards_by_id)
        if card.get("trelloId"):
            api.call("PUT", f"/cards/{card['trelloId']}", name=card["name"], desc=desc,
                     idLabels=card_labels(card, label_ids))
            ensure_checklist(api, data, card)
            updated += 1
            continue
        print(f"+ Carte {card['name']} → {card['list']}")
        params = dict(name=card["name"], desc=desc, idList=list_ids[card["list"]],
                      idLabels=card_labels(card, label_ids), pos=pos * 1000)
        if card.get("owner") and member_id:
            params["idMembers"] = member_id
        new = api.call("POST", "/cards", **params)
        card["trelloId"], card["trelloShortLink"] = new["id"], new.get("shortLink")
        save(data, api)
        created += 1
        ensure_checklist(api, data, card)
    # Second passage : liens de dépendances maintenant que tous les shortLinks existent.
    if created and api.apply:
        for card in data["cards"]:
            if card.get("dependencies"):
                api.call("PUT", f"/cards/{card['trelloId']}", desc=card_description(card, cards_by_id))
    return created, updated


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="publier réellement (sinon simulation)")
    args = parser.parse_args()

    try:
        publish(args.apply)
    except TrelloError as err:
        sys.exit(str(err))


def publish(apply):
    data = load()
    api = Trello(apply)
    me = api.call("GET", "/members/me", fields="username")
    print(f"{'PUBLICATION' if apply else 'SIMULATION'} — compte Trello : {me['username']}\n")

    board_id = ensure_board(api, data)
    list_ids = ensure_lists(api, data, board_id)
    label_ids = ensure_labels(api, data, board_id)
    created, updated = publish_cards(api, data, list_ids, label_ids, me["id"])

    print(f"\nCartes créées : {created} · mises à jour : {updated} · écritures Trello : {api.writes}")
    if apply:
        data["status"] = "published"
        save(data, api)
        print(f"Identifiants enregistrés dans {BACKLOG.relative_to(ROOT)}")
        print(f"Tableau : https://trello.com/b/{board_id}")
    else:
        print("Aucune écriture effectuée. Relancer avec --apply pour publier.")


if __name__ == "__main__":
    main()

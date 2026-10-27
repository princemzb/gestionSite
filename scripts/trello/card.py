#!/usr/bin/env python3
"""Consulte, commente ou déplace une carte Trello du backlog par son identifiant métier.

Usage :
    python3 scripts/trello/card.py status
    python3 scripts/trello/card.py comment US-05 "Texte du commentaire" [--apply]
    python3 scripts/trello/card.py move PREP-01 "Livré" --proof "Preuve…" [--apply]

Sans --apply, les écritures sont simulées et affichées. Un passage en Revue, Recette,
Prêt à livrer ou Livré exige une preuve d'au moins 20 caractères ; l'outil ne vérifie
que sa présence, les conditions de passage de docs/pilotage/TRELLO.md restent à la
charge de l'opérateur. Un passage en Bloqué consigne la liste précédente.

La preuve est publiée avant le déplacement et porte un marqueur « [US-05 → Livré #a1b2c3] »
(empreinte de la preuve) : une carte n'entre jamais dans une liste à preuve sans sa preuve,
une relance identique après erreur termine l'opération sans dupliquer le commentaire, et une
nouvelle entrée dans la même liste exige une nouvelle preuve. Si le déplacement échoue
définitivement, la preuve déjà publiée reste sur la carte : relancer jusqu'au succès.
"""
import argparse
import hashlib
import json
import sys
import unicodedata

from publish_backlog import BACKLOG, Trello, TrelloError, open_lists

# Listes dont l'entrée exige une preuve (docs/pilotage/TRELLO.md, « Conditions de passage »).
PROOF_REQUIRED = {"Revue", "Recette", "Prêt à livrer", "Livré"}
MIN_PROOF_LENGTH = 20
MAX_COMMENT_LENGTH = 16384  # limite Trello


def nfc(text):
    return unicodedata.normalize("NFC", text).strip()


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="action", required=True)
    sub.add_parser("status", help="afficher les cartes par liste")
    comment = sub.add_parser("comment", help="commenter une carte")
    comment.add_argument("card")
    comment.add_argument("text")
    comment.add_argument("--apply", action="store_true", help="écrire réellement dans Trello")
    move = sub.add_parser("move", help="déplacer une carte")
    move.add_argument("card")
    move.add_argument("target")
    move.add_argument("--proof", default="", help="commentaire de preuve du passage")
    move.add_argument("--apply", action="store_true", help="écrire réellement dans Trello")
    return parser.parse_args()


def find_card(data, card_id):
    card_id = nfc(card_id).upper()
    card = next((c for c in data["cards"] if c["id"] == card_id), None)
    if card is None:
        sys.exit(f"Carte inconnue : {card_id}")
    if not card.get("trelloId"):
        sys.exit(f"Carte non publiée dans Trello : {card_id} (lancer publish_backlog.py --apply)")
    return card_id, card["trelloId"]


def checked_comment(text):
    text = nfc(text)
    if not text:
        sys.exit("Commentaire vide.")
    if len(text) > MAX_COMMENT_LENGTH:
        sys.exit(f"Commentaire trop long ({len(text)} > {MAX_COMMENT_LENGTH} caractères).")
    return text


def post_comment(api, trello_id, text):
    if not api.apply:
        print(f"[simulation] commentaire sur {trello_id} :\n{text}")
    api.call("POST", f"/cards/{trello_id}/actions/comments", text=text)


def has_comment_starting_with(api, trello_id, prefix):
    actions = api.call("GET", f"/cards/{trello_id}/actions", filter="commentCard", limit=1000)
    return any(a["data"]["text"].startswith(prefix) for a in actions)


def status(api, data):
    names = open_lists(api, data["boardId"])
    by_list = {}
    for card in api.call("GET", f"/boards/{data['boardId']}/cards", fields="name,idList"):
        by_list.setdefault(card["idList"], []).append(card["name"])
    sections = [(names[i], by_list.get(i, [])) for i in names]  # ordre du tableau
    orphans = [n for i, cards in by_list.items() if i not in names for n in cards]
    sections.append(("Liste archivée ou inconnue", orphans))
    for list_name, cards in sections:
        if cards:
            print(f"\n{list_name} ({len(cards)})")
            print("\n".join(f"  {n}" for n in sorted(cards)))


def move(api, data, card_id, target, proof):
    target = nfc(target)
    if target not in data["listIds"]:
        sys.exit(f"Liste inconnue : {target} (listes : {', '.join(data['lists'])})")
    proof = nfc(proof)
    if target in PROOF_REQUIRED and len(proof) < MIN_PROOF_LENGTH:
        sys.exit(f"Une preuve d'au moins {MIN_PROOF_LENGTH} caractères est obligatoire pour « {target} ».")
    card_id, trello_id = find_card(data, card_id)
    target_id = data["listIds"][target]
    card = api.call("GET", f"/cards/{trello_id}", fields="idList,closed")
    if card["closed"]:
        sys.exit(f"{card_id} est archivée dans Trello : la désarchiver avant de la déplacer.")

    # Empreinte calculée sur la preuve saisie, avant tout ajout automatique, pour rester stable à la relance.
    digest = hashlib.sha256(proof.encode()).hexdigest()[:6]
    marker = f"[{card_id} → {target} #{digest}]"
    if target == "Bloqué" and card["idList"] != target_id:
        names = {v: k for k, v in data["listIds"].items()}
        previous = names.get(card["idList"]) or open_lists(api, data["boardId"]).get(card["idList"], "inconnue")
        proof = f"Liste précédente : {previous}. {proof}"
    text = checked_comment(f"{marker} {proof}") if proof else None

    wrote = False
    if text and not has_comment_starting_with(api, trello_id, marker):
        post_comment(api, trello_id, text)
        wrote = True
    if card["idList"] != target_id:
        if not api.apply:
            print(f"[simulation] déplacement de {card_id} ({trello_id}) vers « {target} » ({target_id})")
        api.call("PUT", f"/cards/{trello_id}", idList=target_id, pos="top")
        wrote = True
    if not wrote:
        print(f"{card_id} est déjà dans « {target} » avec sa preuve : aucune écriture.")
    return wrote


def main():
    args = parse_args()
    data = json.loads(BACKLOG.read_text(encoding="utf-8"))
    api = Trello(apply=getattr(args, "apply", False))
    try:
        if args.action == "status":
            status(api, data)
            return
        if args.action == "comment":
            _, trello_id = find_card(data, args.card)
            post_comment(api, trello_id, checked_comment(args.text))
        elif not move(api, data, args.card, args.target, args.proof):
            return
    except TrelloError as err:
        advice = {"move": "Relancer la même commande : elle reprend sans dupliquer la preuve.",
                  "comment": "Vérifier sur la carte si le commentaire est arrivé avant de relancer."}
        sys.exit(f"{err}\n{advice.get(args.action, '')}".strip())
    print(f"{args.card} : {args.action} {'effectué' if api.apply else 'simulé (relancer avec --apply)'}")


if __name__ == "__main__":
    main()

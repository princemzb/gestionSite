#!/usr/bin/env python3
"""Consulte, commente ou déplace une carte Trello du backlog par son identifiant métier.

Usage :
    python3 scripts/trello/card.py status
    python3 scripts/trello/card.py comment US-05 "Texte du commentaire" [--apply]
    python3 scripts/trello/card.py move PREP-01 "Livré" --proof "Preuve…" [--apply]

Sans --apply, les écritures sont simulées. Un passage en Revue, Recette, Prêt à livrer
ou Livré exige une preuve d'au moins 20 caractères ; l'outil ne vérifie que sa présence,
les conditions de passage de docs/pilotage/TRELLO.md restent à la charge de l'opérateur.
Un passage en Bloqué consigne automatiquement la liste précédente.
"""
import argparse
import json
import sys

from publish_backlog import BACKLOG, Trello, TrelloError

PROOF_REQUIRED = {"Revue", "Recette", "Prêt à livrer", "Livré"}
MIN_PROOF_LENGTH = 20


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


def trello_id(data, card_id):
    card = next((c for c in data["cards"] if c["id"] == card_id), None)
    if card is None:
        sys.exit(f"Carte inconnue : {card_id}")
    if not card.get("trelloId"):
        sys.exit(f"Carte non publiée dans Trello : {card_id} (lancer publish_backlog.py --apply)")
    return card["trelloId"]


def status(api, data):
    by_list = {}
    for card in api.call("GET", f"/boards/{data['boardId']}/cards", fields="name,idList"):
        by_list.setdefault(card["idList"], []).append(card["name"])
    for name in data["lists"]:
        names = sorted(by_list.get(data["listIds"][name], []))
        if names:
            print(f"\n{name} ({len(names)})")
            print("\n".join(f"  {n}" for n in names))


def move(api, data, card_id, target, proof):
    if target not in data["listIds"]:
        sys.exit(f"Liste inconnue : {target} (listes : {', '.join(data['lists'])})")
    proof = proof.strip()
    if target in PROOF_REQUIRED and len(proof) < MIN_PROOF_LENGTH:
        sys.exit(f"Une preuve d'au moins {MIN_PROOF_LENGTH} caractères est obligatoire pour « {target} ».")
    card = trello_id(data, card_id)
    if target == "Bloqué":
        current = api.call("GET", f"/cards/{card}", fields="idList")["idList"]
        previous = {v: k for k, v in data["listIds"].items()}.get(current, "inconnue")
        proof = f"Liste précédente : {previous}. {proof}".strip()
    comment_id = api.call("POST", f"/cards/{card}/actions/comments", text=proof)["id"] if proof else None
    try:
        api.call("PUT", f"/cards/{card}", idList=data["listIds"][target], pos="top")
    except TrelloError:
        # Retirer la preuve d'un passage qui n'a pas eu lieu, pour qu'une reprise ne la duplique pas.
        if comment_id and api.apply:
            api.call("DELETE", f"/actions/{comment_id}")
        raise


def main():
    args = parse_args()
    data = json.loads(BACKLOG.read_text(encoding="utf-8"))
    api = Trello(apply=getattr(args, "apply", False))
    try:
        if args.action == "status":
            status(api, data)
            return
        if args.action == "comment":
            if not args.text.strip():
                sys.exit("Commentaire vide.")
            api.call("POST", f"/cards/{trello_id(data, args.card)}/actions/comments", text=args.text.strip())
        else:
            move(api, data, args.card, args.target, args.proof)
    except TrelloError as err:
        sys.exit(str(err))
    print(f"{args.card} : {args.action} {'effectué' if api.apply else 'simulé (relancer avec --apply)'}")


if __name__ == "__main__":
    main()

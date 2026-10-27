#!/usr/bin/env python3
"""Déplace ou commente une carte Trello du backlog à partir de son identifiant métier.

Usage :
    python3 scripts/trello/card.py comment US-05 "Texte du commentaire"
    python3 scripts/trello/card.py move PREP-01 "Livré" ["Commentaire de preuve"]
    python3 scripts/trello/card.py status

Un déplacement exige les conditions de passage de docs/pilotage/TRELLO.md ;
le commentaire de preuve accompagne chaque passage en Revue, Recette ou Livré.
"""
import sys

from publish_backlog import BACKLOG, Trello
import json

PROOF_REQUIRED = {"Revue", "Recette", "Prêt à livrer", "Livré"}


def main():
    if len(sys.argv) < 2 or sys.argv[1] not in {"comment", "move", "status"}:
        sys.exit(__doc__)
    data = json.loads(BACKLOG.read_text(encoding="utf-8"))
    cards = {c["id"]: c for c in data["cards"]}
    api = Trello(apply=True)
    action = sys.argv[1]

    if action == "status":
        names = {v: k for k, v in data["listIds"].items()}
        for card in api.call("GET", f"/boards/{data['boardId']}/cards", fields="name,idList"):
            print(f"{names.get(card['idList'], '?'):<18} {card['name']}")
        return

    card_id = sys.argv[2]
    if card_id not in cards:
        sys.exit(f"Carte inconnue : {card_id}")
    trello_id = cards[card_id]["trelloId"]

    if action == "comment":
        api.call("POST", f"/cards/{trello_id}/actions/comments", text=sys.argv[3])
    else:
        target = sys.argv[3]
        if target not in data["listIds"]:
            sys.exit(f"Liste inconnue : {target}")
        proof = sys.argv[4] if len(sys.argv) > 4 else None
        if target in PROOF_REQUIRED and not proof:
            sys.exit(f"Un commentaire de preuve est obligatoire pour passer en « {target} ».")
        if proof:
            api.call("POST", f"/cards/{trello_id}/actions/comments", text=proof)
        api.call("PUT", f"/cards/{trello_id}", idList=data["listIds"][target], pos="top")
    print(f"{card_id} : {action} effectué")


if __name__ == "__main__":
    main()

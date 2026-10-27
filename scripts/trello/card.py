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

Le déplacement (rejouable) précède le commentaire de preuve : une carte déjà dans la
liste cible n'est ni déplacée ni recommentée.
"""
import argparse
import json
import sys
import unicodedata

from publish_backlog import BACKLOG, Trello, TrelloError

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


def trello_id(data, card_id):
    card = next((c for c in data["cards"] if c["id"] == card_id), None)
    if card is None:
        sys.exit(f"Carte inconnue : {card_id}")
    if not card.get("trelloId"):
        sys.exit(f"Carte non publiée dans Trello : {card_id} (lancer publish_backlog.py --apply)")
    return card["trelloId"]


def post_comment(api, card, text):
    text = nfc(text)
    if not text:
        sys.exit("Commentaire vide.")
    if len(text) > MAX_COMMENT_LENGTH:
        sys.exit(f"Commentaire trop long ({len(text)} > {MAX_COMMENT_LENGTH} caractères).")
    if not api.apply:
        print(f"[simulation] commentaire sur {card} :\n{text}")
    api.call("POST", f"/cards/{card}/actions/comments", text=text)


def board_lists(api, data):
    return {l["id"]: l["name"] for l in api.call("GET", f"/boards/{data['boardId']}/lists", filter="open")}


def status(api, data):
    names = board_lists(api, data)
    by_list = {}
    for card in api.call("GET", f"/boards/{data['boardId']}/cards", fields="name,idList"):
        by_list.setdefault(card["idList"], []).append(card["name"])
    for list_id, list_name in names.items():  # ordre du tableau, listes ajoutées dans Trello comprises
        cards = sorted(by_list.get(list_id, []))
        if cards:
            print(f"\n{list_name} ({len(cards)})")
            print("\n".join(f"  {n}" for n in cards))


def current_list(api, card):
    return api.call("GET", f"/cards/{card}", fields="idList")["idList"]


def move(api, data, card_id, target, proof):
    target, proof = nfc(target), nfc(proof)
    if target not in data["listIds"]:
        sys.exit(f"Liste inconnue : {target} (listes : {', '.join(data['lists'])})")
    if target in PROOF_REQUIRED and len(proof) < MIN_PROOF_LENGTH:
        sys.exit(f"Une preuve d'au moins {MIN_PROOF_LENGTH} caractères est obligatoire pour « {target} ».")
    card, target_id = trello_id(data, card_id), data["listIds"][target]
    before = current_list(api, card)
    if before == target_id:
        print(f"{card_id} est déjà dans « {target} » : aucune écriture.")
        return False
    if target == "Bloqué":
        proof = f"Liste précédente : {board_lists(api, data).get(before, 'inconnue')}. {proof}"
    if not api.apply:
        print(f"[simulation] déplacement de {card_id} ({card}) vers « {target} » ({target_id})")
    try:
        api.call("PUT", f"/cards/{card}", idList=target_id, pos="top")
    except TrelloError:
        # Une erreur peut survenir après application côté Trello : vérifier l'état réel.
        if not api.apply or current_list(api, card) != target_id:
            raise
    if proof:
        try:
            post_comment(api, card, proof)
        except TrelloError as err:
            sys.exit(f"{card_id} déplacée vers « {target} » mais preuve non publiée ({err}). "
                     f"Relancer : card.py comment {card_id} \"…\" --apply")
    return True


def main():
    args = parse_args()
    data = json.loads(BACKLOG.read_text(encoding="utf-8"))
    api = Trello(apply=getattr(args, "apply", False))
    try:
        if args.action == "status":
            status(api, data)
            return
        if args.action == "comment":
            post_comment(api, trello_id(data, args.card), args.text)
        elif not move(api, data, args.card, args.target, args.proof):
            return
    except TrelloError as err:
        sys.exit(str(err))
    print(f"{args.card} : {args.action} {'effectué' if api.apply else 'simulé (relancer avec --apply)'}")


if __name__ == "__main__":
    main()

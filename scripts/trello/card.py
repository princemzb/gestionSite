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

La preuve est publiée avant le déplacement, avec le marqueur « [US-05 → Livré] ». Elle
ne compte que si son texte exact a été publié depuis le dernier déplacement de la carte :
une relance identique après erreur reprend sans doublon, une preuve différente est ajoutée,
et chaque nouvelle entrée dans une liste exige sa propre preuve. Si le déplacement échoue définitivement, la preuve publiée reste sur la carte :
relancer jusqu'au succès.
"""
import argparse
import sys
import unicodedata

from publish_backlog import Trello, TrelloError, load, open_lists

# Listes dont l'entrée exige une preuve (docs/pilotage/TRELLO.md, « Conditions de passage »).
PROOF_REQUIRED = {"Revue", "Recette", "Prêt à livrer", "Livré"}
MIN_PROOF_LENGTH = 20
MAX_COMMENT_LENGTH = 16384  # limite Trello
PREVIOUS_LIST = "(liste précédente : "


def nfc(text):
    return unicodedata.normalize("NFC", text).strip()


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    writes = argparse.ArgumentParser(add_help=False)
    writes.add_argument("--apply", action="store_true", help="écrire réellement dans Trello")
    sub = parser.add_subparsers(dest="action", required=True)
    sub.add_parser("status", help="afficher les cartes par liste")
    comment = sub.add_parser("comment", parents=[writes], help="commenter une carte")
    comment.add_argument("card")
    comment.add_argument("text")
    move = sub.add_parser("move", parents=[writes], help="déplacer une carte")
    move.add_argument("card")
    move.add_argument("target")
    move.add_argument("--proof", default="", help="commentaire de preuve du passage")
    parser.set_defaults(apply=False)
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
    length = len(text.encode("utf-16-le")) // 2  # Trello compte en unités UTF-16
    if length > MAX_COMMENT_LENGTH:
        sys.exit(f"Commentaire trop long ({length} > {MAX_COMMENT_LENGTH} caractères).")
    return text


def post_comment(api, trello_id, text):
    if not api.apply:
        print(f"[simulation] commentaire sur {trello_id} :\n{text}")
    api.call("POST", f"/cards/{trello_id}/actions/comments", text=text)


def has_fresh_proof(api, trello_id, base, in_target, blocked=False):
    """Vrai si cette preuve exacte a été publiée pour l'entrée en cours dans la liste cible.

    `base` est « marqueur + preuve saisie » ; une mention de liste précédente peut suivre.

    Les actions arrivent de la plus récente à la plus ancienne. Si la carte est déjà dans
    la cible, la preuve valide précède le déplacement qui l'y a fait entrer.
    """
    actions = api.call("GET", f"/cards/{trello_id}/actions", filter="commentCard,updateCard:idList",
                       limit=200, fields="type,data", memberCreator="false")
    moves_to_pass = 1 if in_target else 0
    for action in actions:
        if action["type"] == "updateCard":
            if not moves_to_pass:
                return False
            moves_to_pass -= 1
        elif action["data"]["text"] == base or (
                blocked and action["data"]["text"].startswith(f"{base} {PREVIOUS_LIST}")):
            return True
    return False


def status(api, data):
    names = open_lists(api, data["boardId"])
    by_list = {}
    for card in api.call("GET", f"/boards/{data['boardId']}/cards", fields="name,idList,pos"):
        by_list.setdefault(card["idList"], []).append(card)
    sections = [(names[i], by_list.get(i, [])) for i in names]  # ordre du tableau
    orphans = [c for i, cards in by_list.items() if i not in names for c in cards]
    sections.append(("Liste archivée ou inconnue", orphans))
    for list_name, cards in sections:
        if cards:
            print(f"\n{list_name} ({len(cards)})")
            print("\n".join(f"  {c['name']}" for c in sorted(cards, key=lambda c: c["pos"])))


def move(api, data, card_id, target, proof):
    target = nfc(target)
    if target not in data["listIds"]:
        sys.exit(f"Liste inconnue : {target} (listes : {', '.join(data['lists'])})")
    proof = nfc(proof)
    if target in PROOF_REQUIRED and len(proof) < MIN_PROOF_LENGTH:
        sys.exit(f"Une preuve d'au moins {MIN_PROOF_LENGTH} caractères est obligatoire pour « {target} ».")
    card_id, trello_id = find_card(data, card_id)
    target_id = data["listIds"][target]
    if target_id not in open_lists(api, data["boardId"]):
        sys.exit(f"La liste « {target} » n'est plus ouverte dans Trello : mettre à jour listIds.")
    card = api.call("GET", f"/cards/{trello_id}", fields="idList,closed")
    if card["closed"]:
        sys.exit(f"{card_id} est archivée dans Trello : la désarchiver avant de la déplacer.")
    in_target = card["idList"] == target_id

    base = f"[{card_id} → {target}] {proof}".strip()
    text = base if proof else None
    if target == "Bloqué" and not in_target:
        names = {v: k for k, v in data["listIds"].items()}
        previous = names.get(card["idList"]) or open_lists(api, data["boardId"]).get(card["idList"], "inconnue")
        text = f"{base} {PREVIOUS_LIST}{previous})"
    if text:
        text = checked_comment(text)

    wrote = None
    if text and not has_fresh_proof(api, trello_id, base, in_target, blocked=target == "Bloqué"):
        post_comment(api, trello_id, text)
        wrote = "preuve ajoutée, carte déjà en place"
    if not in_target:
        if not api.apply:
            print(f"[simulation] déplacement de {card_id} ({trello_id}) vers « {target} » ({target_id})")
        api.call("PUT", f"/cards/{trello_id}", idList=target_id, pos="top")
        wrote = "déplacement effectué"
    if not wrote:
        print(f"{card_id} est déjà dans « {target} »" + (" avec cette preuve" if text else "") + " : aucune écriture.")
    return wrote


def main():
    args = parse_args()
    try:
        data = load()
    except TrelloError as err:
        sys.exit(str(err))
    api = Trello(apply=args.apply)
    try:
        if args.action == "status":
            status(api, data)
            return
        if args.action == "comment":
            _, trello_id = find_card(data, args.card)
            post_comment(api, trello_id, checked_comment(args.text))
        else:
            outcome = move(api, data, args.card, args.target, args.proof)
            if not outcome:
                return
            print(f"{args.card} : {outcome if api.apply else 'simulé (relancer avec --apply)'}")
            return
    except TrelloError as err:
        advice = {"move": "Si l'erreur est passagère (délai, 5xx), relancer la même commande : "
                          "elle reprend sans dupliquer la preuve. Sinon, corriger la cause d'abord.",
                  "comment": "Vérifier sur la carte si le commentaire est arrivé avant de relancer."}
        sys.exit(f"{err}\n{advice.get(args.action, '')}".strip())
    print(f"{args.card} : {args.action} {'effectué' if api.apply else 'simulé (relancer avec --apply)'}")


if __name__ == "__main__":
    main()

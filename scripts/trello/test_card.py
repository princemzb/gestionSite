"""Tests de card.move contre une fausse API Trello (aucun appel réseau).

Exécution : python3 -m unittest discover -s scripts/trello
"""
import unittest

import card
from publish_backlog import TrelloError

LISTS = ["À affiner", "En cours", "Bloqué", "Revue", "Livré"]
DATA = {
    "boardId": "B",
    "lists": LISTS,
    "listIds": {name: f"L{i}" for i, name in enumerate(LISTS)},
    "cards": [{"id": "US-05", "trelloId": "C1"}, {"id": "US-31"}],
}
PROOF = "PR #12, CI verte, revue sans constat"


class FakeTrello:
    """Simule l'état d'une carte et enregistre les écritures."""

    def __init__(self, list_name="En cours", closed=False, fail_put=False):
        self.apply = True
        self.list_id = DATA["listIds"][list_name]
        self.closed = closed
        self.fail_put = fail_put
        self.comments = []
        self.writes = []

    def call(self, method, path, **params):
        if method == "GET" and path == "/cards/C1":
            return {"idList": self.list_id, "closed": self.closed}
        if method == "GET" and path == "/cards/C1/actions":
            return [{"data": {"text": t}} for t in self.comments]
        if method == "GET" and path == "/boards/B/lists":
            return [{"id": v, "name": k} for k, v in DATA["listIds"].items()]
        self.writes.append((method, path))
        if method == "POST":
            self.comments.append(params["text"])
            return {"id": "A1"}
        if method == "PUT":
            if self.fail_put:
                raise TrelloError("Erreur Trello 503")
            self.list_id = params["idList"]
            return {}
        raise AssertionError(f"appel inattendu {method} {path}")


class MoveTest(unittest.TestCase):
    def test_proof_is_posted_before_move(self):
        api = FakeTrello()
        self.assertTrue(card.move(api, DATA, "US-05", "Revue", PROOF))
        self.assertEqual([m for m, _ in api.writes], ["POST", "PUT"])
        self.assertEqual(api.list_id, DATA["listIds"]["Revue"])
        self.assertTrue(api.comments[0].startswith("[US-05 → Revue #"))

    def test_rerun_after_failed_move_does_not_duplicate_proof(self):
        api = FakeTrello(fail_put=True)
        with self.assertRaises(TrelloError):
            card.move(api, DATA, "US-05", "Revue", PROOF)
        api.fail_put = False
        card.move(api, DATA, "US-05", "Revue", PROOF)
        self.assertEqual(len(api.comments), 1)
        self.assertEqual(api.list_id, DATA["listIds"]["Revue"])

    def test_completed_move_rerun_writes_nothing(self):
        api = FakeTrello()
        card.move(api, DATA, "US-05", "Revue", PROOF)
        api.writes.clear()
        self.assertFalse(card.move(api, DATA, "US-05", "Revue", PROOF))
        self.assertEqual(api.writes, [])

    def test_second_entry_with_new_proof_posts_it(self):
        api = FakeTrello()
        card.move(api, DATA, "US-05", "Revue", PROOF)
        card.move(api, DATA, "US-05", "En cours", "")
        card.move(api, DATA, "US-05", "Revue", "PR #15, CI verte, seconde revue")
        self.assertEqual(len(api.comments), 2)

    def test_blocked_records_previous_list_and_rerun_is_stable(self):
        api = FakeTrello(list_name="Revue")
        card.move(api, DATA, "US-05", "Bloqué", "attente du compte Sonar")
        self.assertIn("Liste précédente : Revue.", api.comments[0])
        card.move(api, DATA, "US-05", "Bloqué", "attente du compte Sonar")
        self.assertEqual(len(api.comments), 1)

    def test_proof_required_for_review_lists(self):
        api = FakeTrello()
        with self.assertRaises(SystemExit):
            card.move(api, DATA, "US-05", "Livré", "   ok   ")
        self.assertEqual(api.writes, [])

    def test_archived_card_is_refused(self):
        api = FakeTrello(closed=True)
        with self.assertRaises(SystemExit):
            card.move(api, DATA, "US-05", "Revue", PROOF)
        self.assertEqual(api.writes, [])

    def test_unpublished_card_and_case_insensitive_id(self):
        with self.assertRaises(SystemExit):
            card.find_card(DATA, "US-31")
        self.assertEqual(card.find_card(DATA, " us-05 "), ("US-05", "C1"))

    def test_too_long_proof_is_refused_before_any_write(self):
        api = FakeTrello()
        with self.assertRaises(SystemExit):
            card.move(api, DATA, "US-05", "Revue", "x" * 20000)
        self.assertEqual(api.writes, [])


if __name__ == "__main__":
    unittest.main()

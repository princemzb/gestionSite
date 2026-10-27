"""Tests de card.py et de la politique de reprise de Trello.call (aucun appel réseau).

Exécution : python3 -m unittest discover -s scripts/trello
"""
import io
import unittest
import urllib.error
from unittest import mock

import card
import publish_backlog
from publish_backlog import TrelloError

def tid(n):
    """Identifiant Trello réaliste (24 hexadécimaux)."""
    return f"{n:024x}"


LISTS = ["À affiner", "En cours", "Bloqué", "Revue", "Livré"]
BOARD, CARD = tid(0xB0), tid(0xC1)
DATA = {
    "boardId": BOARD,
    "lists": LISTS,
    "listIds": {name: tid(0x100 + i) for i, name in enumerate(LISTS)},
    "cards": [{"id": "US-05", "trelloId": CARD}, {"id": "US-31"}],
}
PROOF = "PR #12, CI verte, revue sans constat"


class FakeTrello:
    """Simule une carte et son historique d'actions (du plus récent au plus ancien)."""

    def __init__(self, list_name="En cours", closed=False, fail_put=False):
        self.apply = True
        self.list_id = DATA["listIds"][list_name]
        self.closed = closed
        self.fail_put = fail_put
        self.actions = []
        self.writes = []

    @property
    def comments(self):
        return [a["data"]["text"] for a in self.actions if a["type"] == "commentCard"]

    def call(self, method, path, **params):
        publish_backlog.safe_path(path, method)  # mêmes chemins et méthodes que la vraie API
        if method == "GET" and path == f"/cards/{CARD}":
            return {"idList": self.list_id, "closed": self.closed}
        if method == "GET" and path == f"/cards/{CARD}/actions":
            return list(self.actions)
        if method == "GET" and path == f"/boards/{BOARD}/lists":
            return [{"id": v, "name": k} for k, v in DATA["listIds"].items()]
        self.writes.append((method, path))
        if method == "POST":
            self.actions.insert(0, {"type": "commentCard", "data": {"text": params["text"]}})
            return {"id": tid(0xA1)}
        if method == "PUT":
            if self.fail_put:
                raise TrelloError("Erreur Trello 503")
            self.list_id = params["idList"]
            self.actions.insert(0, {"type": "updateCard", "data": {}})
            return {}
        raise AssertionError(f"appel inattendu {method} {path}")


class MoveTest(unittest.TestCase):
    def test_proof_is_posted_before_move(self):
        api = FakeTrello()
        self.assertTrue(card.move(api, DATA, "US-05", "Revue", PROOF))
        self.assertEqual([m for m, _ in api.writes], ["POST", "PUT"])
        self.assertEqual(api.list_id, DATA["listIds"]["Revue"])
        self.assertTrue(api.comments[0].startswith("[US-05 → Revue] "))

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

    def test_reentry_requires_a_new_proof_even_with_same_text(self):
        api = FakeTrello()
        card.move(api, DATA, "US-05", "Revue", PROOF)
        card.move(api, DATA, "US-05", "En cours", "")
        card.move(api, DATA, "US-05", "Revue", PROOF)
        self.assertEqual(len(api.comments), 2)

    def test_second_blocking_records_the_new_previous_list(self):
        api = FakeTrello(list_name="Revue")
        card.move(api, DATA, "US-05", "Bloqué", "")
        card.move(api, DATA, "US-05", "En cours", "")
        card.move(api, DATA, "US-05", "Bloqué", "")
        self.assertEqual(api.comments[1], "[US-05 → Bloqué] (liste précédente : Revue)")
        self.assertEqual(api.comments[0], "[US-05 → Bloqué] (liste précédente : En cours)")

    def test_blocking_rerun_after_success_is_stable(self):
        api = FakeTrello(list_name="Revue")
        card.move(api, DATA, "US-05", "Bloqué", "attente du compte Sonar")
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
        self.assertEqual(card.find_card(DATA, " us-05 "), ("US-05", CARD))

    def test_too_long_proof_is_refused_before_any_write(self):
        api = FakeTrello()
        with self.assertRaises(SystemExit):
            card.move(api, DATA, "US-05", "Revue", "x" * 20000)
        self.assertEqual(api.writes, [])


class ProofMatchingTest(unittest.TestCase):
    def test_spoofed_marker_comment_does_not_count_as_proof(self):
        api = FakeTrello()
        api.actions.insert(0, {"type": "commentCard", "data": {"text": "[US-05 → Livré] ok"}})
        card.move(api, DATA, "US-05", "Livré", PROOF)
        self.assertEqual(api.comments[0], f"[US-05 → Livré] {PROOF}")

    def test_previous_list_suffix_only_tolerated_for_blocked(self):
        api = FakeTrello()
        api.actions.insert(0, {"type": "commentCard",
                               "data": {"text": f"[US-05 → Livré] {PROOF} (liste précédente : Revue) – à vérifier"}})
        card.move(api, DATA, "US-05", "Livré", PROOF)
        self.assertEqual(api.comments[0], f"[US-05 → Livré] {PROOF}")

    def test_corrected_proof_on_rerun_is_posted(self):
        api = FakeTrello(fail_put=True)
        with self.assertRaises(TrelloError):
            card.move(api, DATA, "US-05", "Livré", "preuve erronée mais assez longue")
        api.fail_put = False
        card.move(api, DATA, "US-05", "Livré", PROOF)
        self.assertEqual(len(api.comments), 2)
        self.assertEqual(api.list_id, DATA["listIds"]["Livré"])

    def test_new_reason_on_already_blocked_card_is_recorded(self):
        api = FakeTrello()
        card.move(api, DATA, "US-05", "Bloqué", "attente Sonar")
        self.assertTrue(card.move(api, DATA, "US-05", "Bloqué", "attente du compte client"))
        self.assertEqual(api.comments[0], "[US-05 → Bloqué] attente du compte client")

    def test_length_is_counted_in_utf16_units(self):
        with self.assertRaises(SystemExit):
            card.checked_comment("😀" * 8193)  # 8193 caractères, 16386 unités UTF-16
        self.assertEqual(card.checked_comment("😀" * 8192), "😀" * 8192)


class TransientErrorTest(unittest.TestCase):
    def test_classification(self):
        import ssl
        self.assertTrue(publish_backlog.is_transient(TimeoutError()))
        self.assertTrue(publish_backlog.is_transient(urllib.error.URLError(ConnectionResetError())))
        self.assertFalse(publish_backlog.is_transient(urllib.error.URLError(ssl.SSLCertVerificationError())))
        self.assertFalse(publish_backlog.is_transient(urllib.error.URLError("nodename nor servname provided")))


class SafePathTest(unittest.TestCase):
    ID = "6ae091cac28a188f1758318e"

    def test_known_segments_and_ids_are_accepted(self):
        for method, path in (("GET", "/members/me"), ("POST", f"/cards/{self.ID}/actions/comments"),
                             ("GET", f"/checklists/{self.ID}/checkItems")):
            self.assertEqual(publish_backlog.safe_path(path, method), path)

    def test_traversal_and_injection_are_refused(self):
        for path in (f"/cards/../tokens/{self.ID}", "/cards/abc?key=x", f"/cards/{self.ID}/../../webhooks",
                     "cards/x", f"/cards/{self.ID.upper()}", "/tokens", f"/organizations/{self.ID}/members",
                     "/me/boards/me", f"/cards/{self.ID}/checkItems", f"/boards/{self.ID}/checklists",
                     f"/actions/{self.ID}"):
            with self.assertRaises(TrelloError):
                publish_backlog.safe_path(path)

    def test_methods_are_restricted_per_path(self):
        self.assertEqual(publish_backlog.safe_path(f"/cards/{self.ID}", "PUT"), f"/cards/{self.ID}")
        for method, path in (("DELETE", f"/cards/{self.ID}"), ("POST", "/members/me"), ("PUT", "/boards")):
            with self.assertRaises(TrelloError):
                publish_backlog.safe_path(path, method)

    def test_backlog_with_malformed_id_is_refused(self):
        bad = "../webhooks"
        for data in ({"listIds": ["x"], "cards": []},
                     {"cards": ["US-05"]},
                     {"boardId": bad, "cards": []},
                     {"listIds": {"Prêt": bad}, "cards": []},
                     {"labelIds": {"IA": bad}, "cards": []},
                     {"cards": [{"trelloId": bad}]},
                     {"cards": [{"checklistId": bad}]},
                     {"cards": [{"trelloId": 123}]}):
            with self.assertRaises(TrelloError, msg=data):
                publish_backlog.validate_ids(data)

    def test_backlog_with_valid_or_missing_ids_is_accepted(self):
        publish_backlog.validate_ids({"boardId": self.ID, "listIds": None, "labelIds": {"IA": self.ID},
                                      "cards": [{"trelloId": None}, {"trelloId": ""}, {"trelloId": self.ID}]})

    def test_full_simulation_from_scratch_makes_no_network_call(self):
        backlog = {"boardName": "Test", "lists": ["À affiner"], "cards": [
            {"id": "US-01", "name": "[US-01] Test", "type": "macro_story", "list": "À affiner", "labels": ["BA"],
             "estimatePersonDays": {"low": 1, "high": 2}, "dependencies": [], "acceptanceCriteria": ["c"],
             "checklist": ["a", "b"]}]}
        member = mock.MagicMock()
        member.__enter__.return_value.read.return_value = b'{"id": "%s", "username": "u"}' % self.ID.encode()
        with mock.patch.object(publish_backlog, "keychain", return_value="x"), \
                mock.patch.object(publish_backlog, "load", return_value=backlog), \
                mock.patch.object(publish_backlog.time, "sleep"), \
                mock.patch.object(publish_backlog.urllib.request, "urlopen", return_value=member) as urlopen, \
                mock.patch("sys.stdout", new=io.StringIO()):
            publish_backlog.publish(apply=False)
        self.assertEqual(urlopen.call_count, 1)  # seul GET /members/me
        self.assertIn("/members/me", urlopen.call_args.args[0].full_url)

    def test_unexpected_id_returned_by_api_is_refused(self):
        response = mock.MagicMock()
        response.__enter__.return_value.read.return_value = b'{"id": "../tokens"}'
        with mock.patch.object(publish_backlog, "keychain", return_value="x"), \
                mock.patch.object(publish_backlog.urllib.request, "urlopen", return_value=response), \
                mock.patch.object(publish_backlog.time, "sleep"):
            with self.assertRaises(TrelloError):
                publish_backlog.Trello(apply=True).call("POST", "/cards")

    def test_simulated_writes_follow_the_same_path_rules(self):
        with mock.patch.object(publish_backlog, "keychain", return_value="x"):
            api = publish_backlog.Trello(apply=False)
        self.assertEqual(api.call("POST", "/cards")["id"], publish_backlog.SIMULATED_ID)
        with self.assertRaises(TrelloError):
            api.call("POST", "/cards/../tokens")

    def test_card_tool_reports_malformed_backlog_cleanly(self):
        with mock.patch.object(card, "load", side_effect=TrelloError("Identifiants Trello invalides")), \
                mock.patch("sys.argv", ["card.py", "status"]):
            with self.assertRaises(SystemExit) as raised:
                card.main()
        self.assertIn("Identifiants Trello invalides", str(raised.exception))


VALID = tid(0xAB)
PATHS = {"GET": "/members/me", "POST": "/cards", "PUT": f"/cards/{tid(0xC1)}"}


def http_error(code):
    return urllib.error.HTTPError("https://api.trello.com", code, "err", {}, io.BytesIO(b"detail"))


class RetryPolicyTest(unittest.TestCase):
    """Les écritures non rejouables (POST) ne sont jamais renvoyées après une erreur ambiguë."""

    def setUp(self):
        patches = [mock.patch.object(publish_backlog, "keychain", return_value="x"),
                   mock.patch.object(publish_backlog.time, "sleep")]
        for p in patches:
            p.start()
            self.addCleanup(p.stop)
        self.api = publish_backlog.Trello(apply=True)

    def run_call(self, method, errors):
        ok = mock.MagicMock()
        ok.__enter__.return_value.read.return_value = b'{"id": "%s"}' % VALID.encode()
        with mock.patch.object(publish_backlog.urllib.request, "urlopen",
                               side_effect=list(errors) + [ok]) as urlopen:
            try:
                return self.api.call(method, PATHS[method]), urlopen.call_count
            except TrelloError:
                return None, urlopen.call_count

    def test_post_is_not_retried_on_server_error(self):
        self.assertEqual(self.run_call("POST", [http_error(503)]), (None, 1))

    def test_post_is_not_retried_on_timeout(self):
        self.assertEqual(self.run_call("POST", [TimeoutError()]), (None, 1))

    def test_post_is_retried_on_rate_limit(self):
        self.assertEqual(self.run_call("POST", [http_error(429)]), ({"id": VALID}, 2))

    def test_put_is_retried_on_server_error_and_timeout(self):
        self.assertEqual(self.run_call("PUT", [http_error(502), TimeoutError()]), ({"id": VALID}, 3))

    def test_client_error_is_not_retried(self):
        self.assertEqual(self.run_call("PUT", [http_error(400)]), (None, 1))

    def test_retries_are_bounded(self):
        self.assertEqual(self.run_call("GET", [http_error(503) for _ in range(5)]), (None, 5))


if __name__ == "__main__":
    unittest.main()

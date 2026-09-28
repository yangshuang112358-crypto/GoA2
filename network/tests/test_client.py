import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "client"))
from player import Player


class ClientStateTests(unittest.TestCase):
    def setUp(self):
        self.client = Player({})
        self.client.accept({"Type": "Welcome", "Seat": 2, "Generation": 7,
                            "Snapshot": {"MatchId": "match", "Revision": 10}})

    def test_late_snapshot_cannot_rewind(self):
        self.client.accept({"Type": "Snapshot", "Generation": 7, "Snapshot": {"MatchId": "match", "Revision": 9}})
        self.assertEqual(10, self.client.view["Revision"])

    def test_late_result_completes_original_without_rewinding(self):
        self.client.pending["original"] = {"frozen": True}
        self.client.accept({"Type": "Result", "Generation": 7, "CommandId": "original", "Code": "ok",
                            "Snapshot": {"MatchId": "match", "Revision": 8}})
        self.assertEqual(10, self.client.view["Revision"])
        self.assertNotIn("original", self.client.pending)
        self.assertEqual("ok", self.client.results["original"]["Code"])

    def test_old_generation_cannot_complete_new_pending(self):
        self.client.pending["id"] = {}
        self.client.accept({"Type": "Result", "Generation": 6, "CommandId": "id", "Snapshot": {"MatchId": "match", "Revision": 100}})
        self.assertIn("id", self.client.pending)
        self.assertEqual(10, self.client.view["Revision"])

    def test_other_match_cannot_replace_view(self):
        self.client.accept({"Type": "Snapshot", "Generation": 7, "Snapshot": {"MatchId": "other", "Revision": 100}})
        self.assertEqual("match", self.client.view["MatchId"])

    def test_same_revision_does_not_replace_view(self):
        original = self.client.view
        self.client.accept({"Type": "Snapshot", "Generation": 7, "Snapshot": {"MatchId": "match", "Revision": 10}})
        self.assertIs(original, self.client.view)

    def test_disconnect_blocks_submission(self):
        self.client.disconnect()
        with self.assertRaisesRegex(RuntimeError, "disconnected"):
            self.client.submit("Pass")

    def test_retry_preserves_frozen_message(self):
        frozen = {"Type": "Intent", "CommandId": "id", "MatchId": "match", "ExpectedRevision": 3, "Kind": "Pass"}
        self.client.pending["id"] = copy.deepcopy(frozen)
        sent = []
        self.client.send = lambda message: sent.append(copy.deepcopy(message))
        self.client.wait = lambda predicate: None
        self.client.retry("id")
        self.assertEqual([frozen], sent)

    def test_authentication_cannot_change_seat(self):
        with self.assertRaisesRegex(ValueError, "identity changed"):
            self.client.accept({"Type": "Welcome", "Seat": 0, "Generation": 8})


if __name__ == "__main__":
    unittest.main()

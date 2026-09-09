from __future__ import annotations

import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from uuid import uuid4

from tests.contract_support import field


class MultiplayerIdentityContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex

    def test_each_claimed_seat_receives_only_its_private_hand(self) -> None:
        player_zero = self.app.join(0, self.room_id)
        player_one = self.app.join(1, self.room_id)

        zero_state = self.app.state(self.room_id, player_zero["token"])
        one_state = self.app.state(self.room_id, player_one["token"])

        self.assertNotIn("seat_token_hashes", zero_state)
        self.assertNotIn("seat_token_hashes", zero_state["public"])
        self.assertNotIn(player_zero["token"], repr(zero_state))
        self.assertEqual(0, field(zero_state, "controlled_seat"))
        self.assertEqual(1, field(one_state, "controlled_seat"))
        self.assertNotEqual(
            field(zero_state, "private_view")["hand"],
            field(one_state, "private_view")["hand"],
        )

    def test_claimed_room_rejects_missing_forged_and_duplicate_identity(self) -> None:
        player = self.app.join(0, self.room_id)

        with self.assertRaises(ValueError):
            self.app.actor_seat(self.room_id, None, 0)
        with self.assertRaises(ValueError):
            self.app.actor_seat(self.room_id, "forged", 0)
        with self.assertRaises(ValueError):
            self.app.join(0, self.room_id)
        self.assertEqual(
            0,
            self.app.actor_seat(self.room_id, player["token"], 3),
        )

    def test_reconnect_reuses_existing_token(self) -> None:
        player = self.app.join(2, self.room_id)
        rejoined = self.app.join(2, self.room_id, player["token"])

        self.assertEqual(player["token"], rejoined["token"])
        self.assertEqual(2, rejoined["seat"])

    def test_saved_token_reconnects_after_json_application_restart(self) -> None:
        from goa2.backend.bootstrap import build_application

        with TemporaryDirectory() as directory:
            path = Path(directory) / "rooms.json"
            first = build_application(rooms_file=path)
            player = first.join(3, self.room_id)

            restarted = build_application(rooms_file=path)
            rejoined = restarted.join(3, self.room_id, player["token"])

            self.assertEqual(player["token"], rejoined["token"])
            self.assertEqual(3, rejoined["seat"])

            with self.assertRaises(ValueError):
                restarted.join(3, self.room_id, "forged-token-value-that-is-long")
            with self.assertRaises(ValueError):
                restarted.actor_seat(self.room_id, None, 3)

    def test_caller_token_cannot_claim_unowned_seat(self) -> None:
        supplied = "caller-supplied-token-that-must-not-be-trusted"

        with self.assertRaises(ValueError):
            self.app.join(1, self.room_id, supplied)

        joined = self.app.join(1, self.room_id)
        self.assertNotEqual(supplied, joined["token"])


if __name__ == "__main__":
    unittest.main()

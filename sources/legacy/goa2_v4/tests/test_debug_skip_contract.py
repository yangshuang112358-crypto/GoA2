from __future__ import annotations

import unittest
from uuid import uuid4

from tests.contract_support import field


class DebugSkipContractTests(unittest.TestCase):
    def setUp(self) -> None:
        from goa2.backend.bootstrap import build_application

        self.app = build_application()
        self.room_id = uuid4().hex
        initial = self.app.state(self.room_id)
        self.revealed = self.app.force_confirm(
            self.room_id,
            field(initial, "revision"),
        )

    def test_debug_skip_current_uses_normal_skip_resolution(self) -> None:
        active = field(self.revealed, "public")["active_seat"]

        skipped = self.app.debug_skip_current(
            self.room_id,
            field(self.revealed, "revision"),
        )

        self.assertIn(active, field(skipped, "public")["resolved_seats"])

    def test_debug_skip_all_finishes_every_remaining_card(self) -> None:
        skipped = self.app.debug_skip_all(
            self.room_id,
            field(self.revealed, "revision"),
        )

        self.assertEqual("card_selection", field(skipped, "public")["phase"])
        self.assertEqual(2, field(skipped, "public")["turn"])

    def test_debug_skip_all_is_atomic_when_action_is_pending(self) -> None:
        active = field(self.revealed, "public")["active_seat"]
        options = field(self.revealed, "private_view")["action_options"]
        option = next(item for item in options if item["kind"] != "primary")
        chosen = self.app.choose_action(
            active,
            option["kind"],
            option["source_slot"],
            self.room_id,
            field(self.revealed, "revision"),
        )
        before = self.app.state(self.room_id)

        with self.assertRaises(ValueError):
            self.app.debug_skip_all(
                self.room_id,
                field(chosen, "revision"),
            )

        self.assertEqual(before, self.app.state(self.room_id))


if __name__ == "__main__":
    unittest.main()

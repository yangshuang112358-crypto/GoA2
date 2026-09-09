from __future__ import annotations

import unittest
from uuid import uuid4

from tests.contract_support import field, skip_current_action


class RoundLifecycleContractTests(unittest.TestCase):
    def test_fourth_turn_automatically_resolves_round_end(self) -> None:
        from goa2.backend.bootstrap import build_application

        app = build_application()
        room_id = uuid4().hex
        payload = app.state(room_id)
        for turn in range(1, 5):
            payload = app.force_confirm(room_id, field(payload, "revision"))
            for _ in range(4):
                payload = skip_current_action(app, room_id, payload)
            self.assertEqual("card_selection", field(field(payload, "public"), "phase"))

        self.assertEqual(2, field(field(payload, "public"), "round"))
        self.assertEqual(1, field(field(payload, "public"), "turn"))
        for seat in field(field(payload, "public"), "seats"):
            self.assertEqual(
                0,
                len(app._rooms.read(room_id).state.seats[seat["seat"]].used_card_ids),
            )

    def test_fourth_turn_automatically_waits_for_frontline_captain_choice(self) -> None:
        from dataclasses import replace
        from goa2.backend.bootstrap import build_application
        from goa2.domain import Team

        app = build_application()
        room_id = uuid4().hex
        payload = app.state(room_id)
        for turn in range(1, 5):
            payload = app.force_confirm(room_id, field(payload, "revision"))
            if turn == 4:
                app._rooms.transact(
                    room_id,
                    lambda current: replace(
                        current,
                        state=replace(
                            current.state,
                            minions=tuple(
                                minion
                                for index, minion in enumerate(current.state.minions)
                                if not (minion.team is Team.BLUE and index == 0)
                            ),
                        ),
                    ),
                )
                payload = app.state(room_id)
            for _ in range(4):
                payload = skip_current_action(app, room_id, payload)

        public = field(payload, "public")
        self.assertEqual("round_end", field(public, "phase"))
        self.assertEqual("frontline", field(public, "round_end_step"))
        self.assertEqual("remove_minions", public["pending_captain_choice"]["kind"])
        self.assertEqual("blue", public["pending_captain_choice"]["team"])


if __name__ == "__main__":
    unittest.main()

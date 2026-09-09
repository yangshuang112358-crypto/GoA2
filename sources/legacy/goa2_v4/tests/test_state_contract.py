from __future__ import annotations

import json
import unittest

from tests.contract_support import field, state_snapshot


class StateContractTests(unittest.TestCase):
    def test_room_configuration_is_serializable_and_parameterized(self) -> None:
        from goa2.domain import create_initial_room_state

        state = create_initial_room_state(
            starting_crystal_life=9,
            frontline_victory_marks=7,
        )

        self.assertEqual(9, state.starting_crystal_life)
        self.assertEqual(7, state.frontline_victory_marks)
        self.assertEqual([9, 9], [team.crystal_life for team in state.teams])
        self.assertEqual(state, type(state).from_dict(state.to_dict()))

    def test_initial_state_has_four_numbered_seats(self) -> None:
        from goa2.domain import Team, create_initial_room_state

        state = create_initial_room_state()
        seats = list(field(state, "seats"))

        self.assertEqual([0, 1, 2, 3], [field(seat, "seat", "seat_id") for seat in seats])
        self.assertEqual(0, field(state, "controlled_seat"))
        self.assertEqual(0, field(state, "revision"))
        self.assertIn(state.decision_coin, (Team.BLUE, Team.RED))
        self.assertEqual(3, state.frontline_victory_marks)

    def test_state_round_trips_through_json(self) -> None:
        from goa2.domain import RoomState, create_initial_room_state

        original = create_initial_room_state()
        encoded = json.dumps(state_snapshot(original), ensure_ascii=False, sort_keys=True)
        restored = RoomState.from_dict(json.loads(encoded))

        self.assertEqual(state_snapshot(original), state_snapshot(restored))
        self.assertEqual(encoded, json.dumps(state_snapshot(restored), ensure_ascii=False, sort_keys=True))


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import unittest
from uuid import uuid4

from tests.contract_support import field, skip_current_action


class RoundEndInfrastructureTests(unittest.TestCase):
    def test_region_adjacency_is_derived_from_walkable_map(self) -> None:
        from goa2.backend.bootstrap import build_application
        from goa2.engine import adjacent_regions

        app = build_application()
        room_id = uuid4().hex
        app.state(room_id)
        state = app._rooms.read(room_id).state
        graph = adjacent_regions(state)

        self.assertIn("mid", graph)
        self.assertTrue(graph["mid"])
        for region, neighbors in graph.items():
            for neighbor in neighbors:
                self.assertIn(region, graph[neighbor])

    def test_minion_frontline_scaffold_round_trips_without_rule_assumptions(self) -> None:
        from goa2.domain import RoomState, Team
        from goa2.backend.bootstrap import build_application

        app = build_application()
        room_id = uuid4().hex
        app.state(room_id)
        state = app._rooms.read(room_id).state
        restored = RoomState.from_dict(state.to_dict())

        self.assertEqual(state, restored)
        self.assertEqual(12, len(state.minions))
        self.assertEqual("mid", state.combat_region)
        self.assertIsNone(state.pending_captain_choice)
        self.assertEqual([7, 7], [team.crystal_life for team in state.teams])
        self.assertEqual([0, 1], [team.captain_seat for team in state.teams])
        self.assertEqual([Team.BLUE, Team.RED], [team.team for team in state.teams])

    def test_round_end_entry_is_automatic_and_manual_entry_is_rejected(self) -> None:
        from goa2.backend.bootstrap import build_application

        app = build_application()
        room_id = uuid4().hex
        payload = app.state(room_id)
        for _ in range(4):
            payload = app.force_confirm(room_id, field(payload, "revision"))
            for _ in range(4):
                payload = skip_current_action(app, room_id, payload)

        self.assertEqual("card_selection", field(field(payload, "public"), "phase"))
        self.assertEqual(2, field(field(payload, "public"), "round"))
        self.assertEqual("ready", field(field(payload, "public"), "round_end_step"))
        before = app.state(room_id)
        with self.assertRaises(ValueError):
            app.begin_round_end(room_id, field(payload, "revision"))
        self.assertEqual(before, app.state(room_id))


if __name__ == "__main__":
    unittest.main()
